using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 9B: the GDPR export's training_submissions.json: the member's own hours submissions with their review state and history (the
	/// member's and reviewers' notes addressed to them) and the metadata of their evidence files (name, type, size, kind, hash, never the
	/// bytes, which the member downloads from the product).
	/// </summary>
	public class TrainingSubmissionsGdprSection : IGdprExportSection
	{
		private readonly ILearningRecordsRepository _records;
		private readonly ILearningRecordFilesRepository _files;
		private readonly ILearningRecordReviewEventsRepository _events;

		public TrainingSubmissionsGdprSection(ILearningRecordsRepository records, ILearningRecordFilesRepository files, ILearningRecordReviewEventsRepository events)
		{
			_records = records;
			_files = files;
			_events = events;
		}

		public string EntryName => "training_submissions.json";

		public async Task<object> BuildAsync(string userId, int departmentId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<object>();

			bool Mine(string id) => string.Equals(id, userId, StringComparison.OrdinalIgnoreCase);
			var submissions = (await _records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				UserIds = new List<string> { userId },
				SourceKinds = new List<int> { (int)LearningRecordSourceKinds.ExternalSubmission },
				IncludeVoided = true,
				Take = 5000
			}) ?? new List<LearningRecord>()).Where(r => r != null && r.DepartmentId == departmentId && Mine(r.UserId)).ToList();
			var files = (await _files.GetForUserAsync(departmentId, userId) ?? new List<LearningRecordFile>()).Where(f => f != null && f.DepartmentId == departmentId && Mine(f.UserId)).ToList();
			var events = (await _events.GetForUserAsync(departmentId, userId) ?? new List<LearningRecordReviewEvent>()).Where(e => e != null && e.DepartmentId == departmentId && Mine(e.UserId)).ToList();

			return submissions.OrderBy(r => r.CreatedOn).Select(r => new
			{
				r.LearningRecordId,
				r.Title,
				r.CourseCode,
				r.CompletedOn,
				r.Hours,
				r.CategoryId,
				r.CeProvider,
				r.CeCourseNumber,
				r.CeHours,
				r.Notes,
				Status = Enum.IsDefined(typeof(LearningRecordStatuses), r.Status) ? ((LearningRecordStatuses)r.Status).ToString() : null,
				ReviewState = TrainingSubmissionRules.StateOf(r)?.ToString(),
				Provenance = TrainingSubmissionRules.ProvenanceOf(r).ToString(),
				r.SubmittedOn,
				r.VerifiedByUserId,
				r.VerifiedOn,
				r.ReviewNote,
				r.VoidedOn,
				r.VoidReason,
				r.CreatedOn,
				Evidence = files.Where(f => f.LearningRecordId == r.LearningRecordId || f.LearningRecordFileId == r.EvidenceFileId).Select(f => new
				{
					f.LearningRecordFileId,
					f.FileName,
					f.ContentType,
					f.SizeBytes,
					Kind = Enum.IsDefined(typeof(TrainingEvidenceKinds), f.EvidenceKind) ? ((TrainingEvidenceKinds)f.EvidenceKind).ToString() : null,
					f.Sha256,
					f.UploadedOn
				}).ToList(),
				History = events.Where(e => e.LearningRecordId == r.LearningRecordId).OrderBy(e => e.CreatedOn).Select(e => new
				{
					Step = Enum.IsDefined(typeof(TrainingSubmissionEventKinds), e.Kind) ? ((TrainingSubmissionEventKinds)e.Kind).ToString() : null,
					e.ActorUserId,
					e.Note,
					e.Hours,
					e.FileCount,
					e.CreatedOn
				}).ToList()
			}).ToList();
		}
	}

	/// <summary>
	/// Training hours submissions (round 9B, baseline/free): the one entry point for a member's CE / CPD / training hours with evidence and
	/// their optional review. The ledger rules stay in ILearningRecordService (T1's external submission, extended); this service decides who
	/// reviews whom (TrainingSubmissionReviewerScope), keeps the review history (LearningRecordReviewEvents), runs the inbox and the daily
	/// digest, and raises the notices (EventTypes 79-80) and workflow triggers 280-284. A notice or workflow failure never undoes the action.
	/// </summary>
	public class TrainingSubmissionService : ITrainingSubmissionService
	{
		/// <summary>Department-local hour from which the daily reviewer digest goes out.</summary>
		public const int DigestHour = 7;

		/// <summary>Departments read per digest run (worker 85, every five minutes).</summary>
		public const int DigestDepartmentsPerRun = 500;

		private readonly ILearningRecordService _records;
		private readonly ILearningRecordsRepository _ledger;
		private readonly ILearningRecordReviewEventsRepository _reviewEvents;
		private readonly ITrainingReviewDigestsRepository _digests;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IPermissionsService _permissions;
		private readonly ITrainingRecordNoticeService _notices;
		private readonly IEventAggregator _events;

		public TrainingSubmissionService(ILearningRecordService records, ILearningRecordsRepository ledger, ILearningRecordReviewEventsRepository reviewEvents,
			ITrainingReviewDigestsRepository digests, IAuthorizationService authorization, IDepartmentsService departments, IDepartmentGroupsService groups,
			IPersonnelRolesService roles, IPermissionsService permissions, ITrainingRecordNoticeService notices, IEventAggregator events)
		{
			_records = records;
			_ledger = ledger;
			_reviewEvents = reviewEvents;
			_digests = digests;
			_authorization = authorization;
			_departments = departments;
			_groups = groups;
			_roles = roles;
			_permissions = permissions;
			_notices = notices;
			_events = events;
		}

		private static bool IsCode(InvalidOperationException ex) =>
			ex?.Message != null && Regex.IsMatch(ex.Message, "^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private static bool Same(string a, string b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

		private static int Count(IReadOnlyCollection<TrainingEvidenceUpload> evidence) => evidence?.Count(e => e?.Data != null && e.Data.Length > 0) ?? 0;

		#region Reviewers

		public async Task<TrainingSubmissionReviewerScope> GetReviewerScopeAsync(int departmentId, string userId)
		{
			var reviewer = TrainingSubmissionReviewerScope.None(userId);
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return reviewer;

			var scope = await _authorization.GetTrainingManagementScopeAsync(userId, departmentId);
			if (scope == null)
				return reviewer;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			reviewer.IsDepartmentAdmin = department != null && department.IsUserAnAdmin(userId);

			// Record Training Completions (135, the Training Captain / Instructor right) and T1's record reviewers (Assign Training 126).
			var record = scope.Record ?? new TrainingRightScope();
			var assign = scope.AssignRight ?? new TrainingRightScope();
			var allMembers = reviewer.IsDepartmentAdmin || record.ManagesAll || assign.ManagesAll;
			foreach (var members in new[] { record.AdministeredMemberUserIds, assign.AdministeredMemberUserIds })
				if (members != null)
					reviewer.MemberUserIds.UnionWith(members.Where(m => !string.IsNullOrWhiteSpace(m)));

			if (allMembers && !reviewer.IsDepartmentAdmin)
			{
				var settings = await _records.GetSettingsAsync(departmentId) ?? new TrainingRecordSettings();
				if (settings.RoleReviewersOwnGroupOnly)
				{
					// A reviewer named by role reviews only the members of the group they are in (department admins still review everyone).
					allMembers = false;
					var group = await _groups.GetGroupForUserAsync(userId, departmentId);
					if (group?.Members != null)
						reviewer.MemberUserIds.UnionWith(group.Members.Where(m => m != null && !string.IsNullOrWhiteSpace(m.UserId)).Select(m => m.UserId));
				}
			}

			reviewer.AllMembers = allMembers;
			reviewer.MemberUserIds.Remove(userId);
			return reviewer;
		}

		public Task<List<string>> GetReviewerUserIdsAsync(int departmentId, string memberUserId) =>
			ReviewerUserIdsAsync(departmentId, memberUserId, new Dictionary<string, TrainingSubmissionReviewerScope>(StringComparer.OrdinalIgnoreCase));

		/// <summary>
		/// The candidates who could review anybody (active department admins, the member's group admins, the members of the roles the two rights
		/// name), kept when their own reviewer scope covers the member. <paramref name="scopes"/> caches scopes across members (the digest).
		/// </summary>
		private async Task<List<string>> ReviewerUserIdsAsync(int departmentId, string memberUserId, Dictionary<string, TrainingSubmissionReviewerScope> scopes)
		{
			var result = new List<string>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(memberUserId))
				return result;

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<IdentityUser>())
				if (!string.IsNullOrWhiteSpace(admin?.Id))
					candidates.Add(admin.Id);

			var group = await _groups.GetGroupForUserAsync(memberUserId, departmentId);
			foreach (var member in group?.Members ?? new List<DepartmentGroupMember>())
				if (member != null && member.IsAdmin == true && !string.IsNullOrWhiteSpace(member.UserId))
					candidates.Add(member.UserId);

			var create = await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CreateTraining);
			var assign = await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.AssignTraining) ?? create;
			var recordRight = await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.RecordTrainingCompletions);
			foreach (var permission in new[] { assign, recordRight })
			{
				if (permission == null || permission.Action != (int)PermissionActions.DepartmentAdminsAndSelectRoles || string.IsNullOrWhiteSpace(permission.Data))
					continue;
				foreach (var part in permission.Data.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var roleId) || roleId <= 0)
						continue;
					foreach (var roleUser in await _roles.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>())
						if (roleUser != null && roleUser.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(roleUser.UserId))
							candidates.Add(roleUser.UserId);
				}
			}

			foreach (var candidate in candidates.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
			{
				if (Same(candidate, memberUserId) || !active.Contains(candidate))
					continue;
				if (!scopes.TryGetValue(candidate, out var scope))
				{
					scope = await GetReviewerScopeAsync(departmentId, candidate);
					scopes[candidate] = scope;
				}
				if (scope.Covers(memberUserId))
					result.Add(active.TryGetValue(candidate, out var canonical) ? canonical : candidate);
			}

			return result;
		}

		#endregion Reviewers

		#region Member

		public async Task<TrainingSubmissionOutcome> SubmitAsync(int departmentId, string userId, LearningRecordDraft draft, IReadOnlyList<TrainingEvidenceUpload> evidence,
			CancellationToken cancellationToken = default)
		{
			LearningRecord record;
			try
			{
				record = await _records.SubmitExternalAsync(departmentId, userId, draft, evidence, cancellationToken);
			}
			catch (InvalidOperationException ex) when (IsCode(ex))
			{
				return TrainingSubmissionOutcome.Fail(ex.Message);
			}

			if (record == null)
				return TrainingSubmissionOutcome.Fail("learning_record_submissions_disabled");

			var reviewRequired = record.Status == (int)LearningRecordStatuses.Pending;
			var files = Count(evidence);
			await AddEventAsync(record, reviewRequired ? TrainingSubmissionEventKinds.Submitted : TrainingSubmissionEventKinds.SelfReported, userId, null, files, cancellationToken);
			await _notices.NotifyMemberAsync(record, reviewRequired ? TrainingRecordNoticeKinds.SubmissionReceived : TrainingRecordNoticeKinds.SubmissionAccepted, null, cancellationToken);
			if (reviewRequired)
				await NotifyReviewersAsync(record, false, cancellationToken);
			Raise(record, TrainingSubmissionWorkflowChanges.Received, null, files, reviewRequired);
			return new TrainingSubmissionOutcome { Record = record, ReviewRequired = reviewRequired };
		}

		public async Task<TrainingSubmissionOutcome> ResubmitAsync(int departmentId, int learningRecordId, string userId, string note, TrainingSubmissionCorrection correction,
			IReadOnlyList<TrainingEvidenceUpload> evidence, CancellationToken cancellationToken = default)
		{
			var text = TrainingSubmissionRules.Note(note);
			var files = Count(evidence);
			var corrected = correction != null && (correction.Hours.HasValue || correction.CeHours.HasValue || correction.CategoryId.HasValue);
			if (text == null && files == 0 && !corrected)
				return TrainingSubmissionOutcome.Fail("learning_record_resubmit_empty");

			LearningRecord record;
			try
			{
				record = await _records.ResubmitAsync(departmentId, learningRecordId, userId, correction, evidence, cancellationToken);
			}
			catch (InvalidOperationException ex) when (IsCode(ex))
			{
				return TrainingSubmissionOutcome.Fail(ex.Message);
			}

			if (record == null)
				return TrainingSubmissionOutcome.Fail("learning_record_not_found");

			await AddEventAsync(record, TrainingSubmissionEventKinds.Resubmitted, userId, text, files, cancellationToken);
			await NotifyReviewersAsync(record, true, cancellationToken);
			Raise(record, TrainingSubmissionWorkflowChanges.Resubmitted, null, await FileCountAsync(record), true);
			return new TrainingSubmissionOutcome { Record = record, ReviewRequired = true };
		}

		public async Task<List<LearningRecord>> GetMySubmissionsAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<LearningRecord>();

			return (await _records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				UserIds = new List<string> { userId },
				SourceKinds = new List<int> { (int)LearningRecordSourceKinds.ExternalSubmission },
				Take = 1000
			}) ?? new List<LearningRecord>()).Where(r => r != null && r.DepartmentId == departmentId && Same(r.UserId, userId)).ToList();
		}

		#endregion Member

		#region Reviewer actions

		/// <summary>The submission a reviewer acts on: the department's, still pending, its member covered by the reviewer, never their own.</summary>
		private async Task<(LearningRecord Record, string Refusal)> ReviewableAsync(int departmentId, int learningRecordId, string reviewerUserId,
			TrainingSubmissionReviewerScope scope = null)
		{
			var record = departmentId <= 0 ? null : await _records.GetRecordAsync(departmentId, learningRecordId);
			if (record == null || record.DepartmentId != departmentId || !TrainingSubmissionRules.IsSubmission(record))
				return (null, "learning_record_not_found");
			if (Same(record.UserId, reviewerUserId))
				return (record, "learning_record_self_review");
			scope ??= await GetReviewerScopeAsync(departmentId, reviewerUserId);
			if (!scope.Covers(record.UserId))
				return (record, "learning_record_not_reviewer");
			if (record.Status != (int)LearningRecordStatuses.Pending)
				return (record, "learning_record_not_pending");
			return (record, null);
		}

		public Task<TrainingSubmissionOutcome> ApproveAsync(int departmentId, int learningRecordId, string reviewerUserId, string note, decimal? hours,
			CancellationToken cancellationToken = default) =>
			ApproveCoreAsync(departmentId, learningRecordId, reviewerUserId, note, hours, null, cancellationToken);

		private async Task<TrainingSubmissionOutcome> ApproveCoreAsync(int departmentId, int learningRecordId, string reviewerUserId, string note, decimal? hours,
			TrainingSubmissionReviewerScope scope, CancellationToken cancellationToken)
		{
			var (record, refusal) = await ReviewableAsync(departmentId, learningRecordId, reviewerUserId, scope);
			if (refusal != null)
				return TrainingSubmissionOutcome.Fail(refusal);

			var text = TrainingSubmissionRules.Note(note);
			LearningRecord approved;
			try
			{
				approved = await _records.VerifyAsync(departmentId, record.LearningRecordId, reviewerUserId, text, hours, cancellationToken);
			}
			catch (InvalidOperationException ex) when (IsCode(ex))
			{
				return TrainingSubmissionOutcome.Fail(ex.Message);
			}

			if (approved == null)
				return TrainingSubmissionOutcome.Fail("learning_record_not_found");

			await AddEventAsync(approved, TrainingSubmissionEventKinds.Approved, reviewerUserId, text, 0, cancellationToken);
			await _notices.NotifyMemberAsync(approved, TrainingRecordNoticeKinds.SubmissionApproved, text, cancellationToken);
			Raise(approved, TrainingSubmissionWorkflowChanges.Approved, reviewerUserId, await FileCountAsync(approved), true);
			return new TrainingSubmissionOutcome { Record = approved, ReviewRequired = true };
		}

		public async Task<TrainingSubmissionOutcome> DenyAsync(int departmentId, int learningRecordId, string reviewerUserId, string reason,
			CancellationToken cancellationToken = default)
		{
			var text = TrainingSubmissionRules.Note(reason);
			if (text == null)
				return TrainingSubmissionOutcome.Fail("learning_record_reason_required");

			var (record, refusal) = await ReviewableAsync(departmentId, learningRecordId, reviewerUserId);
			if (refusal != null)
				return TrainingSubmissionOutcome.Fail(refusal);

			LearningRecord denied;
			try
			{
				denied = await _records.RejectAsync(departmentId, record.LearningRecordId, reviewerUserId, text, cancellationToken);
			}
			catch (InvalidOperationException ex) when (IsCode(ex))
			{
				return TrainingSubmissionOutcome.Fail(ex.Message);
			}

			if (denied == null)
				return TrainingSubmissionOutcome.Fail("learning_record_not_found");

			await AddEventAsync(denied, TrainingSubmissionEventKinds.Denied, reviewerUserId, text, 0, cancellationToken);
			await _notices.NotifyMemberAsync(denied, TrainingRecordNoticeKinds.SubmissionDenied, text, cancellationToken);
			Raise(denied, TrainingSubmissionWorkflowChanges.Denied, reviewerUserId, await FileCountAsync(denied), true);
			return new TrainingSubmissionOutcome { Record = denied, ReviewRequired = true };
		}

		public async Task<TrainingSubmissionOutcome> RequestInfoAsync(int departmentId, int learningRecordId, string reviewerUserId, string note,
			CancellationToken cancellationToken = default)
		{
			var text = TrainingSubmissionRules.Note(note);
			if (text == null)
				return TrainingSubmissionOutcome.Fail("learning_record_note_required");

			var (record, refusal) = await ReviewableAsync(departmentId, learningRecordId, reviewerUserId);
			if (refusal != null)
				return TrainingSubmissionOutcome.Fail(refusal);

			LearningRecord asked;
			try
			{
				asked = await _records.RequestInfoAsync(departmentId, record.LearningRecordId, reviewerUserId, text, cancellationToken);
			}
			catch (InvalidOperationException ex) when (IsCode(ex))
			{
				return TrainingSubmissionOutcome.Fail(ex.Message);
			}

			if (asked == null)
				return TrainingSubmissionOutcome.Fail("learning_record_not_found");

			await AddEventAsync(asked, TrainingSubmissionEventKinds.InfoRequested, reviewerUserId, text, 0, cancellationToken);
			await _notices.NotifyMemberAsync(asked, TrainingRecordNoticeKinds.SubmissionInfoRequested, text, cancellationToken);
			Raise(asked, TrainingSubmissionWorkflowChanges.InfoRequested, reviewerUserId, await FileCountAsync(asked), true);
			return new TrainingSubmissionOutcome { Record = asked, ReviewRequired = true };
		}

		public async Task<TrainingBulkReviewResult> BulkApproveAsync(int departmentId, IEnumerable<int> learningRecordIds, string reviewerUserId, string note,
			CancellationToken cancellationToken = default)
		{
			var result = new TrainingBulkReviewResult();
			var ids = (learningRecordIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().ToList();
			if (ids.Count == 0)
				return result;

			var scope = await GetReviewerScopeAsync(departmentId, reviewerUserId);
			foreach (var id in ids)
			{
				if (result.Approved.Count + result.Refused.Count >= TrainingSubmissionRules.MaxBulk)
				{
					result.Refused[id] = "learning_record_bulk_limit";
					continue;
				}

				cancellationToken.ThrowIfCancellationRequested();
				var outcome = await ApproveCoreAsync(departmentId, id, reviewerUserId, note, null, scope, cancellationToken);
				if (outcome.Refusal == null)
					result.Approved.Add(id);
				else
					result.Refused[id] = outcome.Refusal;
			}

			return result;
		}

		#endregion Reviewer actions

		#region Reads

		private static readonly List<int> SubmissionKinds = new List<int> { (int)LearningRecordSourceKinds.ExternalSubmission };

		/// <summary>The department's pending submissions (awaiting review or waiting for their member), oldest first.</summary>
		private async Task<List<LearningRecord>> PendingAsync(int departmentId) =>
			(await _records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				SourceKinds = SubmissionKinds,
				Statuses = new List<int> { (int)LearningRecordStatuses.Pending },
				ReviewStates = new List<int> { (int)TrainingSubmissionReviewStates.AwaitingReview, (int)TrainingSubmissionReviewStates.InfoRequested },
				OldestSubmittedFirst = true,
				Take = 5000
			}) ?? new List<LearningRecord>()).Where(r => r != null && r.DepartmentId == departmentId).ToList();

		public async Task<TrainingReviewInbox> GetInboxAsync(TrainingReviewInboxQuery query)
		{
			var inbox = new TrainingReviewInbox();
			if (query == null || query.DepartmentId <= 0 || string.IsNullOrWhiteSpace(query.ReviewerUserId))
				return inbox;

			var reviewer = await GetReviewerScopeAsync(query.DepartmentId, query.ReviewerUserId);
			inbox.IsReviewer = reviewer.IsReviewer;
			if (!reviewer.IsReviewer)
				return inbox;

			var mine = (await PendingAsync(query.DepartmentId)).Where(r => reviewer.Covers(r.UserId)).ToList();
			var awaiting = mine.Where(TrainingSubmissionRules.IsAwaitingReview).ToList();
			var waitingForMember = mine.Where(TrainingSubmissionRules.IsInfoRequested).ToList();
			inbox.AwaitingCount = awaiting.Count;
			inbox.InfoRequestedCount = waitingForMember.Count;
			inbox.OldestWaitingSince = awaiting.Count == 0 ? (DateTime?)null : awaiting.Min(TrainingSubmissionRules.WaitingSince);

			IEnumerable<LearningRecord> items = query.InfoRequested ? waitingForMember : awaiting;
			if (query.CategoryId.HasValue)
				items = query.CategoryId.Value > 0 ? items.Where(r => r.CategoryId == query.CategoryId.Value) : items.Where(r => !r.CategoryId.HasValue);
			if (!string.IsNullOrWhiteSpace(query.MemberUserId))
				items = items.Where(r => Same(r.UserId, query.MemberUserId));
			if (!string.IsNullOrWhiteSpace(query.Search))
			{
				var search = query.Search.Trim();
				items = items.Where(r => (r.Title ?? string.Empty).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
					(r.CourseCode ?? string.Empty).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
					(r.CeCourseNumber ?? string.Empty).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
					(r.CeProvider ?? string.Empty).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
			}

			inbox.Items = items.OrderBy(TrainingSubmissionRules.WaitingSince).ThenBy(r => r.LearningRecordId).Take(Math.Clamp(query.Take, 1, 500)).ToList();
			inbox.FileCounts = await FileCountsAsync(query.DepartmentId, inbox.Items);
			return inbox;
		}

		public async Task<int> CountAwaitingAsync(int departmentId, string reviewerUserId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(reviewerUserId))
				return 0;
			var reviewer = await GetReviewerScopeAsync(departmentId, reviewerUserId);
			if (!reviewer.IsReviewer)
				return 0;
			return (await PendingAsync(departmentId)).Count(r => TrainingSubmissionRules.IsAwaitingReview(r) && reviewer.Covers(r.UserId));
		}

		private async Task<Dictionary<int, int>> FileCountsAsync(int departmentId, IReadOnlyCollection<LearningRecord> records)
		{
			var counts = new Dictionary<int, int>();
			if (records == null || records.Count == 0)
				return counts;

			var files = await _records.GetEvidenceFilesAsync(departmentId, records.Select(r => r.LearningRecordId)) ?? new List<LearningRecordFile>();
			foreach (var record in records)
				counts[record.LearningRecordId] = files.Count(f => f.LearningRecordId == record.LearningRecordId ||
					(!f.LearningRecordId.HasValue && record.EvidenceFileId == f.LearningRecordFileId));
			return counts;
		}

		private async Task<int> FileCountAsync(LearningRecord record) =>
			record == null ? 0 : (await FileCountsAsync(record.DepartmentId, new[] { record })).TryGetValue(record.LearningRecordId, out var count) ? count : 0;

		public async Task<TrainingSubmissionDetail> GetSubmissionAsync(int departmentId, int learningRecordId, string viewerUserId)
		{
			var record = departmentId <= 0 || string.IsNullOrWhiteSpace(viewerUserId) ? null : await _records.GetRecordAsync(departmentId, learningRecordId);
			if (record == null || record.DepartmentId != departmentId || !TrainingSubmissionRules.IsSubmission(record))
				return null;

			var detail = new TrainingSubmissionDetail { Record = record, IsOwner = Same(record.UserId, viewerUserId) };
			if (!detail.IsOwner)
			{
				var reviewer = await GetReviewerScopeAsync(departmentId, viewerUserId);
				detail.CanReview = reviewer.Covers(record.UserId) && record.Status == (int)LearningRecordStatuses.Pending;
				// T1's record reviewers (who read the member's transcript) may read it too.
				if (!reviewer.Covers(record.UserId))
				{
					var scope = await _authorization.GetTrainingManagementScopeAsync(viewerUserId, departmentId);
					if (scope == null || !scope.CanReviewRecordsOf(record.UserId))
						return null;
				}
			}

			detail.Files = await _records.GetEvidenceFilesAsync(departmentId, new[] { record.LearningRecordId }) ?? new List<LearningRecordFile>();
			detail.Events = (await _reviewEvents.GetForRecordAsync(departmentId, record.LearningRecordId) ?? new List<LearningRecordReviewEvent>())
				.Where(e => e != null && e.DepartmentId == departmentId).OrderBy(e => e.CreatedOn).ThenBy(e => e.LearningRecordReviewEventId).ToList();
			return detail;
		}

		public async Task<LearningRecordFile> GetEvidenceFileAsync(int departmentId, int learningRecordFileId, string viewerUserId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(viewerUserId))
				return null;

			var file = await _records.GetEvidenceFileAsync(departmentId, learningRecordFileId);
			if (file == null || file.DepartmentId != departmentId || file.Data == null)
				return null;
			if (Same(file.UserId, viewerUserId))
				return file;
			if ((await GetReviewerScopeAsync(departmentId, viewerUserId)).Covers(file.UserId))
				return file;
			var scope = await _authorization.GetTrainingManagementScopeAsync(viewerUserId, departmentId);
			return scope != null && scope.CanReviewRecordsOf(file.UserId) ? file : null;
		}

		#endregion Reads

		#region Notices, history and workflow

		private async Task NotifyReviewersAsync(LearningRecord record, bool resubmitted, CancellationToken cancellationToken)
		{
			try
			{
				var settings = await _records.GetSettingsAsync(record.DepartmentId) ?? new TrainingRecordSettings();
				// Digest departments hear once a day (worker 85); "none" relies on the inbox and the dashboard item.
				if ((TrainingReviewerNoticeModes)settings.ReviewerNotices != TrainingReviewerNoticeModes.Immediate)
					return;

				var reviewers = await GetReviewerUserIdsAsync(record.DepartmentId, record.UserId);
				if (reviewers.Count > 0)
					await _notices.NotifyReviewersAsync(record, reviewers, resubmitted, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Reviewers of training submission {record?.LearningRecordId} could not be told.");
			}
		}

		public async Task<int> SendReviewerDigestsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var sent = 0;
			// Only departments with submissions waiting are read (no sweep of every department, no billing call).
			foreach (var departmentId in await _ledger.GetDepartmentIdsAwaitingReviewAsync(DigestDepartmentsPerRun) ?? new List<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var settings = await _records.GetSettingsAsync(departmentId) ?? new TrainingRecordSettings();
					if ((TrainingReviewerNoticeModes)settings.ReviewerNotices != TrainingReviewerNoticeModes.DailyDigest)
						continue;

					var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
					if (department == null)
						continue;

					DateTime local;
					try { local = utcNow.TimeConverter(department); }
					catch (Exception) { local = utcNow; }
					if (local.Hour < DigestHour)
						continue;

					// Claimed before anything is sent: a second worker, or the next run the same day, sends nothing.
					if (!await _digests.TryClaimAsync(departmentId, local.Date, utcNow, cancellationToken))
						continue;

					var awaiting = (await PendingAsync(departmentId)).Where(TrainingSubmissionRules.IsAwaitingReview).ToList();
					var perReviewer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
					var scopes = new Dictionary<string, TrainingSubmissionReviewerScope>(StringComparer.OrdinalIgnoreCase);
					foreach (var member in awaiting.GroupBy(r => r.UserId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
						foreach (var reviewer in await ReviewerUserIdsAsync(departmentId, member.Key, scopes))
							perReviewer[reviewer] = (perReviewer.TryGetValue(reviewer, out var n) ? n : 0) + member.Count();

					foreach (var pair in perReviewer.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
						if (await _notices.NotifyReviewerDigestAsync(departmentId, pair.Key, pair.Value, cancellationToken))
							sent++;

					await _digests.SaveCountsAsync(departmentId, local.Date, perReviewer.Count, awaiting.Count, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Training review digest for department {departmentId} failed.");
				}
			}

			return sent;
		}

		private async Task AddEventAsync(LearningRecord record, TrainingSubmissionEventKinds kind, string actorUserId, string note, int fileCount, CancellationToken cancellationToken)
		{
			try
			{
				await _reviewEvents.InsertAsync(new LearningRecordReviewEvent
				{
					DepartmentId = record.DepartmentId,
					LearningRecordId = record.LearningRecordId,
					UserId = record.UserId,
					Kind = (int)kind,
					ActorUserId = actorUserId,
					Note = TrainingSubmissionRules.Note(note),
					Hours = record.Hours,
					FileCount = Math.Max(0, fileCount),
					CreatedOn = DateTime.UtcNow
				}, cancellationToken, true);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Review history of training submission {record.LearningRecordId} could not be written.");
			}
		}

		/// <summary>The workflow payload of a submission change: ids, title, category, hours, states and counts; never notes or file names.</summary>
		public static TrainingSubmissionWorkflowEvent WorkflowEvent(LearningRecord record, TrainingSubmissionWorkflowChanges change, string reviewerUserId, int evidenceFiles,
			bool reviewRequired, DateTime utcNow) =>
			new TrainingSubmissionWorkflowEvent
			{
				DepartmentId = record.DepartmentId,
				Change = change,
				LearningRecordId = record.LearningRecordId,
				UserId = record.UserId,
				RecordTitle = record.Title,
				CourseCode = record.CourseCode ?? record.CeCourseNumber,
				CategoryId = record.CategoryId,
				Hours = record.Hours,
				CeHours = record.CeHours,
				ReviewState = TrainingSubmissionRules.StateOf(record)?.ToString(),
				ReviewRequired = reviewRequired,
				EvidenceFiles = evidenceFiles,
				ReviewerUserId = reviewerUserId,
				CompletedOn = record.CompletedOn,
				SubmittedOn = record.SubmittedOn,
				OccurredOn = utcNow
			};

		private void Raise(LearningRecord record, TrainingSubmissionWorkflowChanges change, string reviewerUserId, int evidenceFiles, bool reviewRequired)
		{
			try
			{
				_events?.SendMessage(WorkflowEvent(record, change, reviewerUserId, evidenceFiles, reviewRequired, DateTime.UtcNow));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Workflow event for training submission {record?.LearningRecordId} could not be raised.");
			}
		}

		#endregion Notices, history and workflow
	}
}
