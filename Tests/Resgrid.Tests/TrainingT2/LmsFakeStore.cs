using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// In-memory LMS repositories with the read semantics of the Dapper ones (department-or-system reads, the open-enrollment
	/// uniqueness, row-version claims, the media claim). Rows are stored and returned as copies so a service that forgets to save
	/// a change is caught.
	/// </summary>
	public class MemRepo<T> : IRepository<T> where T : class, IEntity
	{
		public readonly List<T> Rows = new List<T>();
		private int _next = 1;
		private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

		public static T Copy(T entity) => entity == null ? null : (T)Clone.Invoke(entity, null);

		protected static object Get(T entity, string property) => typeof(T).GetProperty(property)?.GetValue(entity);
		protected static int Dept(T entity) => (int)Get(entity, "DepartmentId");
		protected static string User(T entity) => Get(entity, "UserId") as string;
		protected static bool SameUser(T entity, string userId) => string.Equals(User(entity), userId, StringComparison.OrdinalIgnoreCase);
		protected static int Id(T entity) => (int)entity.IdValue;

		protected IEnumerable<T> Live => Rows;
		protected List<T> Out(IEnumerable<T> rows) => rows.Select(Copy).ToList();
		protected T One(Func<T, bool> predicate) => Copy(Rows.FirstOrDefault(predicate));

		public Task<IEnumerable<T>> GetAllAsync() => Task.FromResult<IEnumerable<T>>(Out(Rows));
		public Task<T> GetByIdAsync(object id) => Task.FromResult(One(r => Id(r) == Convert.ToInt32(id)));
		public Task<IEnumerable<T>> GetAllByDepartmentIdAsync(int departmentId) => Task.FromResult<IEnumerable<T>>(Out(Rows.Where(r => Dept(r) == departmentId)));
		public Task<IEnumerable<T>> GetAllByUserIdAsync(string userId) => Task.FromResult<IEnumerable<T>>(Out(Rows.Where(r => SameUser(r, userId))));

		public virtual Task<T> InsertAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (Id(entity) == 0)
				entity.IdValue = _next++;
			else
				_next = Math.Max(_next, Id(entity) + 1);
			Rows.RemoveAll(r => Id(r) == Id(entity));
			Rows.Add(Copy(entity));
			return Task.FromResult(entity);
		}

		public Task<T> UpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var index = Rows.FindIndex(r => Id(r) == Id(entity));
			if (index < 0)
				throw new InvalidOperationException("Update of a row that does not exist: " + typeof(T).Name + " " + Id(entity));
			Rows[index] = Copy(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken) => Task.FromResult(Rows.RemoveAll(r => Id(r) == Id(entity)) > 0);

		public Task<T> SaveOrUpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false) =>
			Id(entity) == 0 ? InsertAsync(entity, cancellationToken, firstLevelOnly) : UpdateAsync(entity, cancellationToken, firstLevelOnly);

		public Task<bool> DeleteMultipleAsync(T entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		/// <summary>The row-version claim of the Dapper repositories: only the expected version moves, and it moves by one.</summary>
		protected Task<bool> TryVersioned(T entity, long expected, Action<long> set)
		{
			var index = Rows.FindIndex(r => Id(r) == Id(entity) && Dept(r) == Dept(entity));
			if (index < 0 || (long)Get(Rows[index], "RowVersion") != expected)
				return Task.FromResult(false);
			set(expected + 1);
			Rows[index] = Copy(entity);
			return Task.FromResult(true);
		}

		protected static bool DepartmentOrSystem(T entity, int departmentId) => Dept(entity) == departmentId || Dept(entity) == 0;
	}

	public class FakeLmsCourses : MemRepo<LmsCourse>, ILmsCoursesRepository
	{
		public Task<List<LmsCourse>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			Task.FromResult(departmentId <= 0 ? new List<LmsCourse>() : Out(Rows.Where(c => c.DepartmentId == departmentId && (includeDeleted || !c.IsDeleted)).OrderBy(c => c.Title)));
		public Task<List<LmsCourse>> GetSystemCoursesAsync() => Task.FromResult(Out(Rows.Where(c => c.DepartmentId == 0 && c.PublisherKind == (int)LmsPublisherKinds.System).OrderBy(c => c.LmsCourseId)));
		public Task<LmsCourse> GetVisibleByIdAsync(int departmentId, int lmsCourseId) => Task.FromResult(One(c => DepartmentOrSystem(c, departmentId) && c.LmsCourseId == lmsCourseId));
		public Task<List<LmsCourse>> GetVisibleByIdsAsync(int departmentId, IEnumerable<int> lmsCourseIds) =>
			Task.FromResult(Out(Rows.Where(c => DepartmentOrSystem(c, departmentId) && (lmsCourseIds ?? Enumerable.Empty<int>()).Contains(c.LmsCourseId))));
		public Task<LmsCourse> GetBySystemKeyAsync(string systemKey) =>
			Task.FromResult(One(c => c.DepartmentId == 0 && string.Equals(c.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase)));
		public Task<bool> TryUpdateAsync(LmsCourse course, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryVersioned(course, expectedRowVersion, v => course.RowVersion = v);
	}

	public class FakeLmsCourseVersions : MemRepo<LmsCourseVersion>, ILmsCourseVersionsRepository
	{
		public Task<List<LmsCourseVersion>> GetForCourseAsync(int departmentId, int lmsCourseId) =>
			Task.FromResult(Out(Rows.Where(v => DepartmentOrSystem(v, departmentId) && v.LmsCourseId == lmsCourseId).OrderByDescending(v => v.LmsCourseVersionId)));
		public Task<LmsCourseVersion> GetVisibleByIdAsync(int departmentId, int lmsCourseVersionId) =>
			Task.FromResult(One(v => DepartmentOrSystem(v, departmentId) && v.LmsCourseVersionId == lmsCourseVersionId));
		public Task<List<LmsCourseVersion>> GetVisibleByIdsAsync(int departmentId, IEnumerable<int> lmsCourseVersionIds) =>
			Task.FromResult(Out(Rows.Where(v => DepartmentOrSystem(v, departmentId) && (lmsCourseVersionIds ?? Enumerable.Empty<int>()).Contains(v.LmsCourseVersionId))));
		public Task<LmsCourseVersion> GetAnyByIdAsync(int lmsCourseVersionId) => Task.FromResult(One(v => v.LmsCourseVersionId == lmsCourseVersionId));
		public Task<List<LmsCourseVersion>> GetInReviewAsync(int departmentId) =>
			Task.FromResult(Out(Rows.Where(v => v.DepartmentId == departmentId && v.Status == (int)ProtocolVersionStatuses.InReview).OrderBy(v => v.SubmittedOn)));
		public Task<bool> TryUpdateAsync(LmsCourseVersion version, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryVersioned(version, expectedRowVersion, v => version.RowVersion = v);
	}

	public class FakeLmsApprovals : MemRepo<LmsCourseVersionApproval>, ILmsCourseVersionApprovalsRepository
	{
		public Task<List<LmsCourseVersionApproval>> GetForVersionAsync(int departmentId, int lmsCourseVersionId) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && a.LmsCourseVersionId == lmsCourseVersionId).OrderBy(a => a.Round).ThenBy(a => a.StepOrder)));

		public Task<bool> TryDecideAsync(LmsCourseVersionApproval step, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(a => a.LmsCourseVersionApprovalId == step.LmsCourseVersionApprovalId && a.DepartmentId == step.DepartmentId &&
				a.Decision == (int)ProtocolApprovalDecisions.Pending);
			if (row == null)
				return Task.FromResult(false);
			row.Decision = step.Decision;
			row.DecidedByUserId = step.DecidedByUserId;
			row.DecidedOn = step.DecidedOn;
			row.Comment = step.Comment;
			row.SignatureRecordId = step.SignatureRecordId;
			return Task.FromResult(true);
		}

		public Task<int> WithdrawPendingAsync(int departmentId, int lmsCourseVersionId, int round, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var rows = Rows.Where(a => a.DepartmentId == departmentId && a.LmsCourseVersionId == lmsCourseVersionId && a.Round == round && a.Decision == (int)ProtocolApprovalDecisions.Pending).ToList();
			foreach (var row in rows)
			{
				row.Decision = (int)ProtocolApprovalDecisions.Withdrawn;
				row.DecidedOn = utcNow;
			}
			return Task.FromResult(rows.Count);
		}
	}

	public class FakeLmsAudiences : MemRepo<LmsCourseAudience>, ILmsCourseAudiencesRepository
	{
		public Task<List<LmsCourseAudience>> GetForCourseAsync(int departmentId, int lmsCourseId) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && a.LmsCourseId == lmsCourseId)));
		public Task<List<LmsCourseAudience>> GetForDepartmentAsync(int departmentId) => Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId)));

		public async Task ReplaceAsync(int departmentId, int lmsCourseId, IEnumerable<IContentAudienceRule> rules, CancellationToken cancellationToken = default)
		{
			Rows.RemoveAll(a => a.DepartmentId == departmentId && a.LmsCourseId == lmsCourseId);
			foreach (var rule in rules ?? Enumerable.Empty<IContentAudienceRule>())
				await InsertAsync(new LmsCourseAudience { DepartmentId = departmentId, LmsCourseId = lmsCourseId, Kind = rule.Kind, RefId = rule.RefId }, cancellationToken);
		}
	}

	public class FakeLmsOwners : MemRepo<LmsCourseOwner>, ILmsCourseOwnersRepository
	{
		public Task<List<LmsCourseOwner>> GetForCourseAsync(int departmentId, int lmsCourseId) =>
			Task.FromResult(Out(Rows.Where(o => o.DepartmentId == departmentId && o.LmsCourseId == lmsCourseId)));
		public Task<List<LmsCourseOwner>> GetForUserAsync(int departmentId, string userId) => Task.FromResult(Out(Rows.Where(o => o.DepartmentId == departmentId && SameUser(o, userId))));
	}

	public class FakeLmsModules : MemRepo<LmsCourseModule>, ILmsCourseModulesRepository
	{
		public Task<List<LmsCourseModule>> GetForVersionAsync(int departmentId, int lmsCourseVersionId) =>
			Task.FromResult(Out(Rows.Where(m => DepartmentOrSystem(m, departmentId) && m.LmsCourseVersionId == lmsCourseVersionId).OrderBy(m => m.SortOrder).ThenBy(m => m.LmsCourseModuleId)));
		public Task<LmsCourseModule> GetVisibleByIdAsync(int departmentId, int lmsCourseModuleId) =>
			Task.FromResult(One(m => DepartmentOrSystem(m, departmentId) && m.LmsCourseModuleId == lmsCourseModuleId));
	}

	public class FakeLmsLessons : MemRepo<LmsLesson>, ILmsLessonsRepository
	{
		public Task<List<LmsLesson>> GetForVersionAsync(int departmentId, int lmsCourseVersionId) =>
			Task.FromResult(Out(Rows.Where(l => DepartmentOrSystem(l, departmentId) && l.LmsCourseVersionId == lmsCourseVersionId).OrderBy(l => l.SortOrder).ThenBy(l => l.LmsLessonId)));
		public Task<LmsLesson> GetVisibleByIdAsync(int departmentId, int lmsLessonId) => Task.FromResult(One(l => DepartmentOrSystem(l, departmentId) && l.LmsLessonId == lmsLessonId));
	}

	public class FakeLmsBanks : MemRepo<LmsQuestionBank>, ILmsQuestionBanksRepository
	{
		public Task<List<LmsQuestionBank>> GetForDepartmentAsync(int departmentId) => Task.FromResult(Out(Rows.Where(b => b.DepartmentId == departmentId && !b.IsDeleted).OrderBy(b => b.Name)));
		public Task<LmsQuestionBank> GetVisibleByIdAsync(int departmentId, int lmsQuestionBankId) =>
			Task.FromResult(One(b => DepartmentOrSystem(b, departmentId) && b.LmsQuestionBankId == lmsQuestionBankId));
		public Task<LmsQuestionBank> GetBySystemKeyAsync(string systemKey) =>
			Task.FromResult(One(b => b.DepartmentId == 0 && string.Equals(b.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase)));
	}

	public class FakeLmsQuestions : MemRepo<LmsQuestion>, ILmsQuestionsRepository
	{
		public Task<List<LmsQuestion>> GetForBankAsync(int departmentId, int lmsQuestionBankId) =>
			Task.FromResult(Out(Rows.Where(q => DepartmentOrSystem(q, departmentId) && q.LmsQuestionBankId == lmsQuestionBankId).OrderBy(q => q.SortOrder).ThenBy(q => q.LmsQuestionId)));
		public Task<LmsQuestion> GetVisibleByIdAsync(int departmentId, int lmsQuestionId) => Task.FromResult(One(q => DepartmentOrSystem(q, departmentId) && q.LmsQuestionId == lmsQuestionId));
	}

	public class FakeLmsAttempts : MemRepo<LmsAssessmentAttempt>, ILmsAssessmentAttemptsRepository
	{
		public Task<LmsAssessmentAttempt> GetByIdForDepartmentAsync(int departmentId, int lmsAssessmentAttemptId) =>
			Task.FromResult(One(a => a.DepartmentId == departmentId && a.LmsAssessmentAttemptId == lmsAssessmentAttemptId));
		public Task<List<LmsAssessmentAttempt>> GetForEnrollmentLessonAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && a.LmsEnrollmentId == lmsEnrollmentId && a.LmsLessonId == lmsLessonId).OrderByDescending(a => a.LmsAssessmentAttemptId)));
		public Task<List<LmsAssessmentAttempt>> GetForUserLessonsAsync(int departmentId, string userId, IEnumerable<int> lmsLessonIds) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && SameUser(a, userId) && (lmsLessonIds ?? Enumerable.Empty<int>()).Contains(a.LmsLessonId))
				.OrderByDescending(a => a.LmsAssessmentAttemptId)));
		public Task<List<LmsAssessmentAttempt>> GetForUserWithoutSnapshotAsync(int departmentId, string userId) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && SameUser(a, userId)).OrderByDescending(a => a.LmsAssessmentAttemptId))
				.Select(a => { a.SnapshotJson = null; return a; }).ToList());
		public Task<List<LmsAssessmentAttempt>> GetPendingGradingAsync(int departmentId) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && a.Status == (int)LmsAttemptStatuses.PendingGrading).OrderBy(a => a.SubmittedOn)));
		public Task<List<LmsAssessmentAttempt>> GetFinishedForLessonAsync(int departmentId, int lmsLessonId) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && a.LmsLessonId == lmsLessonId &&
				(a.Status == (int)LmsAttemptStatuses.Scored || a.Status == (int)LmsAttemptStatuses.Graded))));
		public Task<bool> TryUpdateAsync(LmsAssessmentAttempt attempt, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryVersioned(attempt, expectedRowVersion, v => attempt.RowVersion = v);
	}

	public class FakeLmsEnrollments : MemRepo<LmsEnrollment>, ILmsEnrollmentsRepository
	{
		public Task<LmsEnrollment> GetByIdForDepartmentAsync(int departmentId, int lmsEnrollmentId) =>
			Task.FromResult(One(e => e.DepartmentId == departmentId && e.LmsEnrollmentId == lmsEnrollmentId));
		public Task<LmsEnrollment> GetOpenAsync(int departmentId, string userId, int lmsCourseId) =>
			Task.FromResult(One(e => e.DepartmentId == departmentId && SameUser(e, userId) && e.LmsCourseId == lmsCourseId && e.IsOpen));
		public Task<List<LmsEnrollment>> GetForUserAsync(int departmentId, string userId) =>
			Task.FromResult(Out(Rows.Where(e => e.DepartmentId == departmentId && SameUser(e, userId)).OrderByDescending(e => e.LmsEnrollmentId)));
		public Task<List<LmsEnrollment>> GetForCourseAsync(int departmentId, int lmsCourseId) =>
			Task.FromResult(Out(Rows.Where(e => e.DepartmentId == departmentId && e.LmsCourseId == lmsCourseId).OrderByDescending(e => e.LmsEnrollmentId)));
		public Task<List<LmsEnrollment>> GetForCoursesAsync(int departmentId, IEnumerable<int> lmsCourseIds) =>
			Task.FromResult(Out(Rows.Where(e => e.DepartmentId == departmentId && (lmsCourseIds ?? Enumerable.Empty<int>()).Contains(e.LmsCourseId))));
		public Task<List<LmsEnrollment>> GetOpenForCourseAnyDepartmentAsync(int lmsCourseId) => Task.FromResult(Out(Rows.Where(e => e.LmsCourseId == lmsCourseId && e.IsOpen)));

		public override Task<LmsEnrollment> InsertAsync(LmsEnrollment entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			// UX_LmsEnrollments_Open: one open enrollment per member and course.
			if (entity.IsOpen && entity.LmsEnrollmentId == 0 && Rows.Any(e => e.IsOpen && e.DepartmentId == entity.DepartmentId && SameUser(e, entity.UserId) && e.LmsCourseId == entity.LmsCourseId))
				throw new InvalidOperationException("UX_LmsEnrollments_Open");
			return base.InsertAsync(entity, cancellationToken, firstLevelOnly);
		}

		public async Task<(LmsEnrollment Enrollment, bool Created)> InsertOpenAsync(LmsEnrollment enrollment, CancellationToken cancellationToken = default)
		{
			var existing = await GetOpenAsync(enrollment.DepartmentId, enrollment.UserId, enrollment.LmsCourseId);
			if (existing != null)
				return (existing, false);
			enrollment.IsOpen = true;
			await InsertAsync(enrollment, cancellationToken);
			return (enrollment, true);
		}

		public Task<bool> TryUpdateAsync(LmsEnrollment enrollment, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryVersioned(enrollment, expectedRowVersion, v => enrollment.RowVersion = v);
		public Task<bool> HasCompletedVersionAsync(string userId, int lmsCourseVersionId) =>
			Task.FromResult(Rows.Any(e => e.LmsCourseVersionId == lmsCourseVersionId && SameUser(e, userId) && e.Status == (int)LmsEnrollmentStatuses.Completed));
		public Task<List<LmsEnrollment>> GetReminderDueAsync(DateTime idleBeforeUtc, int take) =>
			Task.FromResult(Out(Rows.Where(e => e.Status == (int)LmsEnrollmentStatuses.InProgress && e.IsOpen && e.LastActivityOn < idleBeforeUtc &&
				(e.ReminderSentOn == null || e.ReminderSentOn < e.LastActivityOn)).OrderBy(e => e.LastActivityOn).Take(take)));

		public Task<bool> ClaimReminderAsync(int departmentId, int lmsEnrollmentId, DateTime? previousReminderOn, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(e => e.DepartmentId == departmentId && e.LmsEnrollmentId == lmsEnrollmentId && e.ReminderSentOn == previousReminderOn);
			if (row == null)
				return Task.FromResult(false);
			row.ReminderSentOn = utcNow;
			return Task.FromResult(true);
		}
	}

	public class FakeLmsProgress : MemRepo<LmsLessonProgress>, ILmsLessonProgressRepository
	{
		public Task<List<LmsLessonProgress>> GetForEnrollmentAsync(int departmentId, int lmsEnrollmentId) =>
			Task.FromResult(Out(Rows.Where(p => p.DepartmentId == departmentId && p.LmsEnrollmentId == lmsEnrollmentId)));
		public Task<LmsLessonProgress> GetAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId) =>
			Task.FromResult(One(p => p.DepartmentId == departmentId && p.LmsEnrollmentId == lmsEnrollmentId && p.LmsLessonId == lmsLessonId));

		public override Task<LmsLessonProgress> InsertAsync(LmsLessonProgress entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var stored = Rows.FirstOrDefault(p => p.DepartmentId == entity.DepartmentId && p.LmsEnrollmentId == entity.LmsEnrollmentId && p.LmsLessonId == entity.LmsLessonId);
			if (stored != null && entity.LmsLessonProgressId == 0)
			{
				entity.LmsLessonProgressId = stored.LmsLessonProgressId;
				return UpdateAsync(entity, cancellationToken, firstLevelOnly);
			}
			return base.InsertAsync(entity, cancellationToken, firstLevelOnly);
		}
	}

	public class FakeLmsFiles : MemRepo<LmsLessonFile>, ILmsLessonFilesRepository
	{
		public Task<LmsLessonFile> GetByIdForDepartmentAsync(int departmentId, int lmsLessonFileId) =>
			Task.FromResult(One(f => f.DepartmentId == departmentId && f.LmsLessonFileId == lmsLessonFileId));

		/// <summary>Like the Dapper metadata select: the bytes never come back.</summary>
		public Task<List<LmsLessonFile>> GetMetadataByIdsAsync(int departmentId, IEnumerable<int> lmsLessonFileIds)
		{
			var ids = new HashSet<int>(lmsLessonFileIds ?? Enumerable.Empty<int>());
			var rows = Out(Rows.Where(f => f.DepartmentId == departmentId && ids.Contains(f.LmsLessonFileId)));
			rows.ForEach(f => f.Data = null);
			return Task.FromResult(rows);
		}
	}

	public class FakeLmsSurveys : MemRepo<LmsSurveyResponse>, ILmsSurveyResponsesRepository
	{
		public Task<List<LmsSurveyResponse>> GetForLessonAsync(int departmentId, int lmsLessonId) =>
			Task.FromResult(Out(Rows.Where(s => s.DepartmentId == departmentId && s.LmsLessonId == lmsLessonId)));
	}

	public class FakeLmsViewings : MemRepo<LmsCrewViewing>, ILmsCrewViewingsRepository
	{
		public Task<LmsCrewViewing> GetByIdForDepartmentAsync(int departmentId, int lmsCrewViewingId) =>
			Task.FromResult(One(v => v.DepartmentId == departmentId && v.LmsCrewViewingId == lmsCrewViewingId));
	}

	public class FakeLmsAttendees : MemRepo<LmsCrewViewingAttendee>, ILmsCrewViewingAttendeesRepository
	{
		public Task<List<LmsCrewViewingAttendee>> GetForViewingAsync(int departmentId, int lmsCrewViewingId) =>
			Task.FromResult(Out(Rows.Where(a => a.DepartmentId == departmentId && a.LmsCrewViewingId == lmsCrewViewingId).OrderBy(a => a.MarkedOn)));

		public async Task<bool> TryInsertAsync(LmsCrewViewingAttendee attendee, CancellationToken cancellationToken = default)
		{
			if (Rows.Any(a => a.LmsCrewViewingId == attendee.LmsCrewViewingId && SameUser(a, attendee.UserId)))
				return false;
			await InsertAsync(attendee, cancellationToken);
			return true;
		}
	}

	public class FakeLmsPaths : MemRepo<LmsPathAssignment>, ILmsPathAssignmentsRepository
	{
		public Task<List<LmsPathAssignment>> GetForDepartmentAsync(int departmentId) => Task.FromResult(Out(Rows.Where(p => p.DepartmentId == departmentId && p.RemovedOn == null)));
		public Task<List<LmsPathAssignment>> GetForUserAsync(int departmentId, string userId) =>
			Task.FromResult(Out(Rows.Where(p => p.DepartmentId == departmentId && SameUser(p, userId) && p.RemovedOn == null)));

		// T3: the A1 ordered-list link and the path completion stamp.
		public Task<int> LinkSystemPathAsync(string pathKey, int lmsPathId, CancellationToken cancellationToken = default)
		{
			var rows = Rows.Where(p => p.DepartmentId > 0 && !p.LmsPathId.HasValue && string.Equals(p.PathKey, pathKey, StringComparison.OrdinalIgnoreCase)).ToList();
			foreach (var row in rows)
				row.LmsPathId = lmsPathId;
			return Task.FromResult(rows.Count);
		}

		public Task<bool> TryCompleteAsync(int departmentId, int lmsPathAssignmentId, DateTime completedOnUtc, int? personnelCertificationId, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(p => p.DepartmentId == departmentId && p.LmsPathAssignmentId == lmsPathAssignmentId && !p.CompletedOn.HasValue);
			if (row == null)
				return Task.FromResult(false);
			row.CompletedOn = completedOnUtc;
			row.PersonnelCertificationId = personnelCertificationId;
			return Task.FromResult(true);
		}
	}

	public class FakeLmsStates : MemRepo<LmsDepartmentState>, ILmsDepartmentStatesRepository
	{
		public Task<LmsDepartmentState> GetForDepartmentAsync(int departmentId) => Task.FromResult(One(s => s.DepartmentId == departmentId));

		public async Task StampEntitledAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(s => s.DepartmentId == departmentId);
			if (row != null)
			{
				row.LastEntitledOn = utcNow;
				row.UpdatedOn = utcNow;
				return;
			}
			await InsertAsync(new LmsDepartmentState { DepartmentId = departmentId, LastEntitledOn = utcNow, UpdatedOn = utcNow }, cancellationToken);
		}

		public List<int> MediaDepartments = new List<int>();

		public Task<List<int>> GetLapsedWithReadyMediaAsync(DateTime entitledBeforeUtc) =>
			Task.FromResult(Rows.Where(s => s.LastEntitledOn < entitledBeforeUtc && MediaDepartments.Contains(s.DepartmentId)).Select(s => s.DepartmentId).Distinct().ToList());
	}

	public class FakeLmsMedia : MemRepo<LmsMediaAsset>, ILmsMediaAssetsRepository
	{
		public Task<LmsMediaAsset> GetByIdForDepartmentAsync(int departmentId, int lmsMediaAssetId) =>
			Task.FromResult(One(m => m.DepartmentId == departmentId && m.LmsMediaAssetId == lmsMediaAssetId));
		public Task<List<LmsMediaAsset>> GetForDepartmentAsync(int departmentId) =>
			Task.FromResult(Out(Rows.Where(m => m.DepartmentId == departmentId).OrderByDescending(m => m.LmsMediaAssetId)));

		public Task<LmsMediaAsset> ClaimNextAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, CancellationToken cancellationToken = default)
		{
			var row = Rows.Where(m => m.Status == (int)LmsMediaStatuses.Queued || (m.Status == (int)LmsMediaStatuses.Transcoding && m.ClaimedOn < staleClaimBeforeUtc))
				.OrderBy(m => m.CreatedOn).FirstOrDefault();
			if (row == null)
				return Task.FromResult<LmsMediaAsset>(null);
			row.Status = (int)LmsMediaStatuses.Transcoding;
			row.ClaimedOn = utcNow;
			row.Attempts += 1;
			return Task.FromResult(Copy(row));
		}

		public Task<bool> TryMoveAsync(LmsMediaAsset asset, LmsMediaStatuses from, CancellationToken cancellationToken = default)
		{
			var index = Rows.FindIndex(m => m.LmsMediaAssetId == asset.LmsMediaAssetId && m.DepartmentId == asset.DepartmentId && m.Status == (int)from);
			if (index < 0)
				return Task.FromResult(false);
			var row = Rows[index];
			row.Status = asset.Status;
			row.SourceKey = asset.SourceKey;
			row.SourceBytes = asset.SourceBytes;
			row.HlsPrefix = asset.HlsPrefix;
			row.Mp4Key = asset.Mp4Key;
			row.OutputBytes = asset.OutputBytes;
			row.DurationSeconds = asset.DurationSeconds;
			row.LastError = asset.LastError;
			row.ReadyOn = asset.ReadyOn;
			row.ArchivedOn = asset.ArchivedOn;
			return Task.FromResult(true);
		}

		public Task<long> GetStoredBytesAsync(int departmentId) => Task.FromResult(Rows.Where(m => m.DepartmentId == departmentId).Sum(m => m.SourceBytes + m.OutputBytes));
	}
}
