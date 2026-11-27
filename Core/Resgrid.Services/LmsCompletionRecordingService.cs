using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 7B (permission 135): recorded completions of LMS courses and from class rosters. The baseline checks (permission, group scope,
	/// active member, never yourself, attestation, note) come from <see cref="ITrainingCompletionRecordingService"/>; this adds the LMS seat
	/// (§17.4), the add-on, the class roster and external learners on it, and completes the course through the learner runtime's normal
	/// path. A class linked to a baseline training records through the baseline service (no seat needed).
	/// </summary>
	public class LmsCompletionRecordingService : ILmsCompletionRecordingService
	{
		private readonly ITrainingCompletionRecordingService _baseline;
		private readonly ITrainingCompletionRecordingsRepository _recordings;
		private readonly ILmsLearnerService _runtime;
		private readonly ILmsCoursesRepository _courses;
		private readonly ITrainingService _trainings;
		private readonly ILmsAccessService _access;
		private readonly ILmsClassService _classes;
		private readonly ILmsExternalLearnersRepository _learners;
		private readonly IDepartmentsService _departments;
		private readonly IAuthorizationService _authorization;
		private readonly ILmsLearnerClassMailer _mailer;
		private readonly ILmsEnrollmentsRepository _enrollments;

		public LmsCompletionRecordingService(ITrainingCompletionRecordingService baseline, ITrainingCompletionRecordingsRepository recordings, ILmsLearnerService runtime,
			ILmsCoursesRepository courses, ITrainingService trainings, ILmsAccessService access, ILmsClassService classes, ILmsExternalLearnersRepository learners,
			IDepartmentsService departments, IAuthorizationService authorization, ILmsLearnerClassMailer mailer, ILmsEnrollmentsRepository enrollments)
		{
			_baseline = baseline;
			_recordings = recordings;
			_runtime = runtime;
			_courses = courses;
			_trainings = trainings;
			_access = access;
			_classes = classes;
			_learners = learners;
			_departments = departments;
			_authorization = authorization;
			_mailer = mailer;
			_enrollments = enrollments;
		}

		public Task<TrainingCompletionRecordingOutcome> RecordCourseAsync(int departmentId, int lmsCourseId, TrainingCompletionRecordingRequest request,
			CancellationToken cancellationToken = default) => RecordCourseAsync(departmentId, lmsCourseId, request, null, null, cancellationToken);

		private async Task<TrainingCompletionRecordingOutcome> RecordCourseAsync(int departmentId, int lmsCourseId, TrainingCompletionRecordingRequest request,
			ISet<string> rosterLearners, LmsClassSession session, CancellationToken cancellationToken)
		{
			var (scope, refusal) = await _baseline.AuthorizeAsync(departmentId, request);
			if (refusal != null)
				return TrainingCompletionRecordingOutcome.Fail(refusal);

			var course = await _courses.GetVisibleByIdAsync(departmentId, lmsCourseId);
			if (course == null || course.IsDeleted || course.IsSystem || course.DepartmentId != departmentId || course.Status != (int)ProtocolStatuses.Published ||
				!course.CurrentVersionId.HasValue)
				return TrainingCompletionRecordingOutcome.Fail(TrainingCompletionRecordingRules.NotFound);

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var members = TrainingCompletionRecordingRules.Members(request);
			var learners = (await _learners.GetByLearnerUserIdsAsync(departmentId, members.Where(LmsExternalLearnerIds.IsExternal)) ?? new List<LmsExternalLearner>())
				.Where(l => l.DepartmentId == departmentId).GroupBy(l => l.LearnerUserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			var completedOn = request.CompletedOnUtc.HasValue && request.CompletedOnUtc.Value < now ? request.CompletedOnUtc.Value : now;
			var batch = string.IsNullOrWhiteSpace(request.BatchKey) ? Guid.NewGuid().ToString("N") : request.BatchKey.Trim();
			var outcome = new TrainingCompletionRecordingOutcome();

			foreach (var requested in members)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var result = new TrainingCompletionRecordingResult { UserId = requested };
				outcome.Results.Add(result);

				var external = LmsExternalLearnerIds.IsExternal(requested);
				string userId;
				if (external)
				{
					// External learners only from a class roster that has them, and only an active learner (never a removed or erased one).
					if (rosterLearners == null || !rosterLearners.Contains(requested))
					{
						result.Refusal = TrainingCompletionRecordingRules.ExternalNotAllowed;
						continue;
					}
					if (!learners.TryGetValue(requested, out var learner) || learner.Status != (int)LmsExternalLearnerStatuses.Active)
					{
						result.Refusal = TrainingCompletionRecordingRules.NotActiveMember;
						continue;
					}
					if (scope == null || !scope.CanRecordFor(requested))
					{
						result.Refusal = TrainingCompletionRecordingRules.OutsideScope;
						continue;
					}
					userId = learner.LearnerUserId;
				}
				else
				{
					result.Refusal = TrainingCompletionRecordingService.MemberRefusal(scope, active, requested, request.RecorderUserId);
					if (result.Refusal != null)
						continue;
					userId = active.TryGetValue(requested, out var canonical) ? canonical : requested;
					// §17.4: being trained through the LMS uses a seat; no seat, no LMS course completion (the baseline path needs none).
					bool seated;
					try
					{
						seated = await _access.CanBeTrainedAsync(departmentId, userId);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, "The LMS seat could not be read for a recorded completion.");
						seated = false;
					}
					if (!seated)
					{
						result.Refusal = TrainingCompletionRecordingRules.NoSeat;
						continue;
					}
				}
				result.UserId = userId;

				TrainingCompletionRecording recording = null;
				try
				{
					recording = await _recordings.InsertAsync(TrainingCompletionRecordingRules.NewRecording(departmentId, userId, TrainingCompletionRecordingTargets.Course,
						course.LmsCourseId, course.CurrentVersionId, course.Title, request, batch, completedOn, false, now), cancellationToken, true);
					var completion = await _runtime.RecordCompletionForMemberAsync(departmentId, course.LmsCourseId, userId, recording, cancellationToken);
					if (!completion.Succeeded)
					{
						await _recordings.DeleteAsync(recording, cancellationToken);
						result.Refusal = completion.Error switch
						{
							LmsErrors.AlreadySubmitted => TrainingCompletionRecordingRules.AlreadyComplete,
							LmsErrors.AddonRequired or LmsErrors.LapseWindowEnded => TrainingCompletionRecordingRules.AddonRequired,
							LmsErrors.NotFound => TrainingCompletionRecordingRules.NotFound,
							_ => TrainingCompletionRecordingRules.Failed
						};
						continue;
					}

					recording.EnrollmentId = completion.Enrollment?.LmsEnrollmentId;
					recording.TargetVersionId = completion.Version?.LmsCourseVersionId ?? recording.TargetVersionId;
					recording.TargetName = TrainingCompletionRecordingRules.Trim(completion.Version?.Title ?? course.Title, TrainingCompletionRecording.NameMaxLength);
					recording.LearningRecordId = completion.Enrollment?.LearningRecordId;
					recording.PersonnelCertificationId = completion.Enrollment?.PersonnelCertificationId;
					recording = await _recordings.SaveOrUpdateAsync(recording, cancellationToken, true);
					result.Recording = recording;
					_baseline.Announce(recording, external);
					// Round 9B: the member hears that the completion was recorded for them (external learners get the class e-mail below).
					if (!external)
						await _baseline.NotifyMemberAsync(recording, cancellationToken);
					if (external && session != null)
						await _mailer.SendAsync(departmentId, session, new[] { userId }, LmsLearnerNoticeKinds.CompletionRecorded, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"A recorded completion of course {lmsCourseId} for {userId} failed.");
					result.Refusal = TrainingCompletionRecordingRules.Failed;
					if (recording != null && result.Recording == null)
					{
						try { await _recordings.DeleteAsync(recording, CancellationToken.None); }
						catch (Exception cleanup) { Logging.LogException(cleanup); }
					}
				}
			}

			return outcome;
		}

		public async Task<TrainingCompletionRecordingOutcome> RecordFromClassAsync(int departmentId, int lmsClassSessionId, TrainingCompletionRecordingRequest request,
			CancellationToken cancellationToken = default)
		{
			if (request == null)
				return TrainingCompletionRecordingOutcome.Fail(TrainingCompletionRecordingRules.NotAuthorized);
			var session = await _classes.GetSessionAsync(departmentId, lmsClassSessionId);
			var refusal = ClassRefusal(session, DateTime.UtcNow);
			if (refusal != null)
				return TrainingCompletionRecordingOutcome.Fail(refusal);

			request.Source = TrainingCompletionRecordingSources.ClassSession;
			request.SourceRefId = session.LmsClassSessionId.ToString(CultureInfo.InvariantCulture);
			request.CompletedOnUtc ??= session.EndsOn;
			request.BatchKey ??= Guid.NewGuid().ToString("N");
			var whole = TrainingCompletionRecordingRules.RequestRefusal(request);
			if (whole != null)
				return TrainingCompletionRecordingOutcome.Fail(whole);

			// Only people on this class's roster (holding a seat or marked attended); the rest are refused one by one.
			var roster = (await _classes.GetEnrollmentsAsync(departmentId, new[] { session.LmsClassSessionId }) ?? new List<LmsClassEnrollment>())
				.Where(e => e.LmsClassSessionId == session.LmsClassSessionId && !string.IsNullOrWhiteSpace(e.UserId) &&
					(e.Status == (int)LmsClassEnrollmentStatuses.Enrolled || e.Attendance == (int)LmsClassAttendanceStates.Attended)).ToList();
			var onRoster = new HashSet<string>(roster.Select(e => e.UserId.Trim()), StringComparer.OrdinalIgnoreCase);
			var requested = TrainingCompletionRecordingRules.Members(request);
			var notOnRoster = requested.Where(u => !onRoster.Contains(u)).ToList();
			request.MemberUserIds = requested.Where(onRoster.Contains).ToList();

			TrainingCompletionRecordingOutcome outcome;
			if (request.MemberUserIds.Count == 0)
				outcome = new TrainingCompletionRecordingOutcome();
			else if (session.CourseKind == (int)LmsClassCourseKinds.Course && session.LmsCourseId.HasValue)
				outcome = await RecordCourseAsync(departmentId, session.LmsCourseId.Value, request,
					new HashSet<string>(onRoster.Where(LmsExternalLearnerIds.IsExternal), StringComparer.OrdinalIgnoreCase), session, cancellationToken);
			else
				outcome = await _baseline.RecordTrainingAsync(departmentId, session.TrainingId.Value, request, cancellationToken);

			if (outcome.Succeeded)
				foreach (var userId in notOnRoster)
					outcome.Results.Add(new TrainingCompletionRecordingResult { UserId = userId, Refusal = TrainingCompletionRecordingRules.NotOnRoster });
			return outcome;
		}

		/// <summary>Why nothing can be recorded from this class (none linked, cancelled, not started yet); null when it can.</summary>
		public static string ClassRefusal(LmsClassSession session, DateTime utcNow)
		{
			if (session == null || session.IsDeleted)
				return TrainingCompletionRecordingRules.NotFound;
			if (session.Status == (int)LmsClassSessionStatuses.Cancelled)
				return TrainingCompletionRecordingRules.ClassCancelled;
			if (session.StartsOn > utcNow)
				return TrainingCompletionRecordingRules.ClassNotStarted;
			var linked = (session.CourseKind == (int)LmsClassCourseKinds.Course && session.LmsCourseId.HasValue) ||
				(session.CourseKind == (int)LmsClassCourseKinds.Training && session.TrainingId.HasValue);
			return linked ? null : TrainingCompletionRecordingRules.NoLinkedTraining;
		}

		public async Task<LmsClassRecordingView> GetClassRecordingViewAsync(int departmentId, int lmsClassSessionId, string viewerUserId)
		{
			var session = await _classes.GetSessionAsync(departmentId, lmsClassSessionId);
			if (session == null)
				return null;
			var view = new LmsClassRecordingView { Session = session, Refusal = ClassRefusal(session, DateTime.UtcNow) };
			var scope = string.IsNullOrWhiteSpace(viewerUserId) ? null : await _authorization.GetTrainingManagementScopeAsync(viewerUserId, departmentId);
			view.CanRecord = scope?.CanRecordCompletions == true;

			if (session.CourseKind == (int)LmsClassCourseKinds.Course && session.LmsCourseId.HasValue)
			{
				view.Target = TrainingCompletionRecordingTargets.Course;
				view.TargetId = session.LmsCourseId;
				view.TargetName = (await _courses.GetVisibleByIdAsync(departmentId, session.LmsCourseId.Value))?.Title;
			}
			else if (session.CourseKind == (int)LmsClassCourseKinds.Training && session.TrainingId.HasValue)
			{
				view.Target = TrainingCompletionRecordingTargets.Training;
				view.TargetId = session.TrainingId;
				var training = await _trainings.GetTrainingRowByIdAsync(session.TrainingId.Value);
				view.TargetName = training != null && training.DepartmentId == departmentId ? training.Name : null;
			}

			view.Recordings = (await _baseline.GetForSourceAsync(departmentId, TrainingCompletionRecordingSources.ClassSession,
				session.LmsClassSessionId.ToString(CultureInfo.InvariantCulture)) ?? new List<TrainingCompletionRecording>()).ToList();
			var live = view.Recordings.Where(r => !r.IsVoided && view.Target.HasValue && r.TargetKind == (int)view.Target.Value && r.TargetId == view.TargetId)
				.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var detail = await _classes.GetDetailAsync(departmentId, lmsClassSessionId, DateTime.UtcNow);
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var learnerIds = (detail?.Roster ?? new List<LmsClassRosterRow>()).Where(r => r.IsExternal).Select(r => r.Enrollment.UserId).ToList();
			var learners = learnerIds.Count == 0 ? new Dictionary<string, LmsExternalLearner>(StringComparer.OrdinalIgnoreCase)
				: (await _learners.GetByLearnerUserIdsAsync(departmentId, learnerIds) ?? new List<LmsExternalLearner>())
					.GroupBy(l => l.LearnerUserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			foreach (var row in (detail?.Roster ?? new List<LmsClassRosterRow>()).Where(r => r.Enrollment != null &&
				(r.Enrollment.Status == (int)LmsClassEnrollmentStatuses.Enrolled || r.Enrollment.Attendance == (int)LmsClassAttendanceStates.Attended)))
			{
				var userId = row.Enrollment.UserId?.Trim();
				var item = new LmsClassRecordingRow
				{
					Enrollment = row.Enrollment,
					UserId = userId,
					Name = row.Name,
					IsExternal = row.IsExternal,
					Attended = row.Enrollment.Attendance == (int)LmsClassAttendanceStates.Attended,
					Recording = userId != null && live.TryGetValue(userId, out var recording) ? recording : null
				};
				if (view.Refusal != null)
					item.Refusal = view.Refusal;
				else if (scope == null)
					item.Refusal = TrainingCompletionRecordingRules.NotAuthorized;
				else if (item.IsExternal)
					item.Refusal = view.Target != TrainingCompletionRecordingTargets.Course ? TrainingCompletionRecordingRules.ExternalNotAllowed
						: !learners.TryGetValue(userId ?? string.Empty, out var learner) || learner.Status != (int)LmsExternalLearnerStatuses.Active ? TrainingCompletionRecordingRules.NotActiveMember
						: !scope.CanRecordFor(userId) ? TrainingCompletionRecordingRules.OutsideScope : null;
				else
					item.Refusal = TrainingCompletionRecordingService.MemberRefusal(scope, active, userId, viewerUserId);
				view.Rows.Add(item);
			}

			view.Rows = view.Rows.OrderByDescending(r => r.Attended).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			return view;
		}

		public async Task<TrainingCompletionRecordingView> GetCourseRecordingViewAsync(int departmentId, int lmsCourseId, string viewerUserId)
		{
			var course = departmentId <= 0 ? null : await _courses.GetVisibleByIdAsync(departmentId, lmsCourseId);
			if (course == null || course.IsSystem || course.IsDeleted || course.DepartmentId != departmentId)
				return null;
			var view = new TrainingCompletionRecordingView { Target = TrainingCompletionRecordingTargets.Course, TargetId = lmsCourseId, TargetName = course.Title };
			if (course.Status != (int)ProtocolStatuses.Published || !course.CurrentVersionId.HasValue)
				view.Refusal = TrainingCompletionRecordingRules.NotFound;
			var scope = string.IsNullOrWhiteSpace(viewerUserId) ? null : await _authorization.GetTrainingManagementScopeAsync(viewerUserId, departmentId);
			view.CanRecord = scope?.CanRecordCompletions == true;
			view.Recordings = await _baseline.GetForTargetAsync(departmentId, TrainingCompletionRecordingTargets.Course, lmsCourseId);
			var live = view.Recordings.Where(r => !r.IsVoided && r.TargetVersionId == course.CurrentVersionId).GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var enrollments = (await _enrollments.GetForCourseAsync(departmentId, lmsCourseId) ?? new List<LmsEnrollment>()).Where(e => !string.IsNullOrWhiteSpace(e.UserId)).ToList();
			var open = new HashSet<string>(enrollments.Where(e => e.IsOpen).Select(e => e.UserId), StringComparer.OrdinalIgnoreCase);
			var completed = new HashSet<string>(enrollments.Where(e => e.Status == (int)LmsEnrollmentStatuses.Completed && e.LmsCourseVersionId == course.CurrentVersionId)
				.Select(e => e.UserId), StringComparer.OrdinalIgnoreCase);
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var userId in active)
			{
				var refusal = TrainingCompletionRecordingService.MemberRefusal(scope, active, userId, viewerUserId);
				if (refusal == TrainingCompletionRecordingRules.Self || refusal == TrainingCompletionRecordingRules.OutsideScope)
					continue;
				view.Candidates.Add(new TrainingCompletionCandidate
				{
					UserId = userId,
					Assigned = open.Contains(userId),
					Refusal = refusal ?? view.Refusal ?? (completed.Contains(userId) ? TrainingCompletionRecordingRules.AlreadyComplete : null),
					Recording = live.TryGetValue(userId, out var recording) ? recording : null
				});
			}
			view.Candidates = view.Candidates.OrderByDescending(c => c.Assigned).ThenBy(c => c.Refusal != null).ToList();
			return view;
		}
	}

	/// <summary>
	/// Round 7B: voiding a recorded LMS course completion (registered as the Course target of the baseline void): the enrolment's learning
	/// record and its credits are voided through the T1 path, the lessons the recording completed are open again, and the enrolment reopens
	/// (or is withdrawn when the member has opened another one meanwhile). A certification the completion issued or renewed is left for the
	/// certification officer to review (a renewal cannot be told apart from the member's own record).
	/// </summary>
	public class LmsCourseCompletionRecordingTarget : ITrainingCompletionRecordingTarget
	{
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsLessonProgressRepository _progress;
		private readonly ILearningRecordsRepository _records;
		private readonly ILearningRecordWriter _writer;

		public LmsCourseCompletionRecordingTarget(ILmsEnrollmentsRepository enrollments, ILmsLessonProgressRepository progress, ILearningRecordsRepository records,
			ILearningRecordWriter writer)
		{
			_enrollments = enrollments;
			_progress = progress;
			_records = records;
			_writer = writer;
		}

		public TrainingCompletionRecordingTargets Kind => TrainingCompletionRecordingTargets.Course;

		public async Task<bool> VoidAsync(TrainingCompletionRecording recording, string userId, string reason, CancellationToken cancellationToken = default)
		{
			if (recording?.EnrollmentId == null)
				return false;
			var enrollment = await _enrollments.GetByIdForDepartmentAsync(recording.DepartmentId, recording.EnrollmentId.Value);
			if (enrollment == null)
				return false;

			await TrainingCompletionRecordingService.VoidMemberRecordsAsync(_records, _writer, recording.DepartmentId, LearningRecordSourceKinds.LmsCourseCompletion,
				LmsLearnerService.SourceRefFor(enrollment), enrollment.UserId, reason, userId, cancellationToken);
			if (enrollment.RecordedCompletionId != recording.TrainingCompletionRecordingId)
				return true;

			var now = DateTime.UtcNow;
			foreach (var row in (await _progress.GetForEnrollmentAsync(recording.DepartmentId, enrollment.LmsEnrollmentId) ?? new List<LmsLessonProgress>())
				.Where(p => p.RecordedCompletionId == recording.TrainingCompletionRecordingId))
			{
				row.Status = (int)LmsLessonProgressStatuses.InProgress;
				row.CompletedOn = null;
				row.RecordedCompletionId = null;
				row.LastSeenOn = now;
				await _progress.SaveOrUpdateAsync(row, cancellationToken, true);
			}

			var other = await _enrollments.GetOpenAsync(recording.DepartmentId, enrollment.UserId, enrollment.LmsCourseId);
			for (var attempt = 0; attempt < 3 && enrollment != null; attempt++)
			{
				var expected = enrollment.RowVersion;
				if (other != null && other.LmsEnrollmentId != enrollment.LmsEnrollmentId)
				{
					enrollment.Status = (int)LmsEnrollmentStatuses.Withdrawn;
					enrollment.IsOpen = false;
					enrollment.WithdrawnOn = now;
					enrollment.WithdrawnByUserId = userId;
				}
				else
				{
					enrollment.Status = (int)LmsEnrollmentStatuses.InProgress;
					enrollment.IsOpen = true;
					enrollment.CompletedOn = null;
					enrollment.ProgressPercent = 0;
				}
				enrollment.LearningRecordId = null;
				enrollment.CompletionMethod = (int)LearningCompletionMethods.Self;
				enrollment.RecordedCompletionId = null;
				if (await _enrollments.TryUpdateAsync(enrollment, expected, cancellationToken))
					return true;
				enrollment = await _enrollments.GetByIdForDepartmentAsync(recording.DepartmentId, recording.EnrollmentId.Value);
			}
			return false;
		}
	}
}
