using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The learner side of the LMS (T2 items 2, 2a, 5, 6; A1): the catalog, enrollments pinned to the version taken (R-L12), the
	/// player's progress (resume, seat time capped by wall-clock, video position and watched percent, R-C8), lesson completion
	/// rules, anonymous surveys, crew-viewing presence (R-C4) and course completion, which writes one learning record through the
	/// T1 writer and may issue or renew a certification. Department content runs on the LMS gate and its lapse rules; system
	/// (Academy) content runs for every department (R-S2).
	/// </summary>
	public class LmsLearnerService : ILmsLearnerService
	{
		private const int ReminderBatch = 200;
		private const int MaxResponseLength = 8000;

		private static readonly HashSet<string> AllowedFileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"application/pdf", "image/jpeg", "image/png", "image/gif", "image/webp", "text/plain",
			"application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
			"application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
		};

		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCourseModulesRepository _modules;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsCourseAudiencesRepository _audiences;
		private readonly ILmsCourseOwnersRepository _owners;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsLessonProgressRepository _progress;
		private readonly ILmsLessonFilesRepository _files;
		private readonly ILmsAssessmentAttemptsRepository _attempts;
		private readonly ILmsSurveyResponsesRepository _surveys;
		private readonly ILmsMediaAssetsRepository _media;
		private readonly IProtocolVersionsRepository _protocolVersions;
		private readonly ILmsGateService _gate;
		private readonly ILmsEntitlementService _entitlements;
		private readonly IContentAudienceService _audienceService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _settingsService;
		private readonly IUserProfileService _profiles;
		private readonly ILearningRecordWriter _learningRecords;
		private readonly ILearningRecordService _learningRecordService;
		private readonly ICertificationService _certificationService;
		private readonly IEventAggregator _eventAggregator;
		private readonly Lazy<ICommunicationService> _communicationService;

		public LmsLearnerService(ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions, ILmsCourseModulesRepository modules, ILmsLessonsRepository lessons,
			ILmsCourseAudiencesRepository audiences, ILmsCourseOwnersRepository owners, ILmsEnrollmentsRepository enrollments, ILmsLessonProgressRepository progress,
			ILmsLessonFilesRepository files, ILmsAssessmentAttemptsRepository attempts, ILmsSurveyResponsesRepository surveys, ILmsMediaAssetsRepository media,
			IProtocolVersionsRepository protocolVersions, ILmsGateService gate, ILmsEntitlementService entitlements, IContentAudienceService audienceService,
			IDepartmentsService departmentsService, IDepartmentSettingsService settingsService, IUserProfileService profiles, ILearningRecordWriter learningRecords,
			ILearningRecordService learningRecordService, ICertificationService certificationService, IEventAggregator eventAggregator,
			Lazy<ICommunicationService> communicationService)
		{
			_courses = courses;
			_versions = versions;
			_modules = modules;
			_lessons = lessons;
			_audiences = audiences;
			_owners = owners;
			_enrollments = enrollments;
			_progress = progress;
			_files = files;
			_attempts = attempts;
			_surveys = surveys;
			_media = media;
			_protocolVersions = protocolVersions;
			_gate = gate;
			_entitlements = entitlements;
			_audienceService = audienceService;
			_departmentsService = departmentsService;
			_settingsService = settingsService;
			_profiles = profiles;
			_learningRecords = learningRecords;
			_learningRecordService = learningRecordService;
			_certificationService = certificationService;
			_eventAggregator = eventAggregator;
			_communicationService = communicationService;
		}

		#region Results (P2 hook)

		public Task<bool> IsCourseVersionPassedAsync(string userId, int courseVersionId) => _enrollments.HasCompletedVersionAsync(userId, courseVersionId);

		#endregion Results

		#region Catalog and enrollments

		public async Task<List<LmsMyCourse>> GetMyCoursesAsync(int departmentId, string userId)
		{
			var result = new List<LmsMyCourse>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return result;

			var enrollments = (await _enrollments.GetForUserAsync(departmentId, userId) ?? new List<LmsEnrollment>())
				.Where(e => e.Status != (int)LmsEnrollmentStatuses.Withdrawn).ToList();
			if (enrollments.Count == 0)
				return result;

			var gate = await _gate.GetGateAsync(departmentId);
			var courses = (await _courses.GetVisibleByIdsAsync(departmentId, enrollments.Select(e => e.LmsCourseId)) ?? new List<LmsCourse>()).ToDictionary(c => c.LmsCourseId);
			var versions = (await _versions.GetVisibleByIdsAsync(departmentId, enrollments.Select(e => e.LmsCourseVersionId)) ?? new List<LmsCourseVersion>()).ToDictionary(v => v.LmsCourseVersionId);
			var now = DateTime.UtcNow;

			foreach (var enrollment in enrollments)
			{
				if (!courses.TryGetValue(enrollment.LmsCourseId, out var course) || !versions.TryGetValue(enrollment.LmsCourseVersionId, out var version))
					continue;
				// Department-made LMS content is dark while the flag is off; the Academy is not.
				if (!course.IsSystem && !gate.FlagOn)
					continue;
				// Completed history stays visible (lapse rule 2); only open work needs the runtime.
				var canRun = LmsLapseRules.CanRun(gate, course.IsSystem, enrollment, now);
				result.Add(new LmsMyCourse
				{
					Enrollment = enrollment,
					Course = course,
					Version = version,
					CanRun = canRun,
					IsOverdue = enrollment.IsOpen && enrollment.DueOn.HasValue && enrollment.DueOn.Value < now,
					BlockedBy = canRun ? LmsErrors.None : (course.IsSystem ? LmsErrors.NotAuthorized : gate.IsLapsed ? LmsErrors.LapseWindowEnded : LmsErrors.AddonRequired)
				});
			}

			return result
				.OrderByDescending(r => r.Enrollment.IsOpen)
				.ThenBy(r => r.Enrollment.DueOn ?? DateTime.MaxValue)
				.ThenByDescending(r => r.Enrollment.LastActivityOn ?? r.Enrollment.AssignedOn)
				.ToList();
		}

		public async Task<List<LmsCatalogEntry>> GetCatalogAsync(LmsActor actor)
		{
			var entries = new List<LmsCatalogEntry>();
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.FlagOn)
				return entries;

			var audiences = (await _audiences.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsCourseAudience>()).ToLookup(a => a.LmsCourseId);
			var mine = (await _enrollments.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsEnrollment>()).Where(e => e.IsOpen).ToList();
			var canEnroll = LmsLapseRules.CanAssignNew(actor.Gate, false);
			foreach (var course in await _courses.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsCourse>())
			{
				if (course.Status != (int)ProtocolStatuses.Published || !course.CurrentVersionId.HasValue || !course.IsSelfEnrollable)
					continue;
				var rules = audiences[course.LmsCourseId].ToList();
				if (rules.Count > 0 && !actor.MayManage && !ContentAudienceMatcher.Matches(actor.Facts, rules))
					continue;
				var version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, course.CurrentVersionId.Value);
				var open = mine.FirstOrDefault(e => e.LmsCourseId == course.LmsCourseId);
				entries.Add(new LmsCatalogEntry { Course = course, Version = version, OpenEnrollment = open, CanEnroll = canEnroll && open == null });
			}

			return entries;
		}

		public async Task<LmsResult> EnrollSelfAsync(LmsActor actor, int lmsCourseId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.IsCurrentMember)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, lmsCourseId);
			if (course == null || course.IsDeleted || course.Status != (int)ProtocolStatuses.Published || !course.CurrentVersionId.HasValue)
				return LmsResult.Fail(LmsErrors.NotFound);

			if (course.IsSystem)
			{
				if (!actor.Gate.ModuleOn)
					return LmsResult.Fail(LmsErrors.NotAuthorized);
				if (!await _entitlements.IsVisibleAsync(actor.DepartmentId, course))
					return LmsResult.Fail(LmsErrors.NotEntitled);
			}
			else
			{
				if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
					return LmsResult.Fail(LmsErrors.AddonRequired);
				var rules = await _audiences.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId) ?? new List<LmsCourseAudience>();
				if (!course.IsSelfEnrollable || (rules.Count > 0 && !actor.MayManage && !ContentAudienceMatcher.Matches(actor.Facts, rules)))
					return LmsResult.Fail(LmsErrors.NotAuthorized);
			}

			var (enrollment, _) = await _enrollments.InsertOpenAsync(NewEnrollment(actor.DepartmentId, course, course.CurrentVersionId.Value, actor.UserId, actor.UserId,
				LmsEnrollmentSources.SelfEnrolled, null, null), cancellationToken);
			return new LmsResult { Course = course, Enrollment = enrollment };
		}

		public async Task<LmsAssignOutcome> AssignAsync(LmsActor actor, int lmsCourseId, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc,
			LmsEnrollmentSources source = LmsEnrollmentSources.Assigned, string rolePathKey = null, CancellationToken cancellationToken = default)
		{
			var outcome = new LmsAssignOutcome();
			if (actor == null || !actor.IsCurrentMember)
				return Fail(outcome, LmsErrors.NotAuthorized);

			var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, lmsCourseId);
			if (course == null || course.IsDeleted || course.Status != (int)ProtocolStatuses.Published || !course.CurrentVersionId.HasValue)
				return Fail(outcome, LmsErrors.NotFound);

			if (course.IsSystem)
			{
				// The Academy is free: Assign Training (126) assigns it, with the module on and the lesson visible to the department.
				if (!actor.MayAssignAcademy)
					return Fail(outcome, LmsErrors.NotAuthorized);
				if (!LmsLapseRules.CanAssignNew(actor.Gate, true))
					return Fail(outcome, LmsErrors.NotAuthorized);
				if (!await _entitlements.IsVisibleAsync(actor.DepartmentId, course))
					return Fail(outcome, LmsErrors.NotEntitled);
			}
			else
			{
				var rights = actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId));
				if (!rights.Assign)
					return Fail(outcome, LmsErrors.NotAuthorized);
				if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
					return Fail(outcome, LmsErrors.AddonRequired);
			}

			if (dueOnUtc.HasValue && dueOnUtc.Value < DateTime.UtcNow.AddMinutes(-5))
				return Fail(outcome, LmsErrors.InvalidInput);

			var rules = ContentAudienceMatcher.Normalize(who);
			if (rules.Count == 0)
				return Fail(outcome, LmsErrors.InvalidInput);

			var resolution = await _audienceService.ResolveAsync(actor.DepartmentId, rules);
			outcome.Skipped = resolution?.Skipped ?? new List<ContentAudienceSkip>();
			var created = new List<string>();
			foreach (var userId in resolution?.MemberUserIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
			{
				var (_, isNew) = await _enrollments.InsertOpenAsync(NewEnrollment(actor.DepartmentId, course, course.CurrentVersionId.Value, userId, actor.UserId, source,
					rolePathKey, dueOnUtc), cancellationToken);
				if (isNew)
				{
					outcome.Created++;
					created.Add(userId);
				}
				else
				{
					outcome.AlreadyEnrolled++;
				}
			}

			if (created.Count > 0)
			{
				Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsCourseAssigned, JsonConvert.SerializeObject(new { course.LmsCourseId, course.Title, assigned = created.Count, dueOnUtc, rolePathKey }));
				await NotifyAssignedAsync(actor.DepartmentId, course, created);
			}

			return outcome;
		}

		private static LmsAssignOutcome Fail(LmsAssignOutcome outcome, LmsErrors error)
		{
			outcome.Error = error;
			return outcome;
		}

		internal static LmsEnrollment NewEnrollment(int departmentId, LmsCourse course, int versionId, string userId, string assignedBy, LmsEnrollmentSources source,
			string rolePathKey, DateTime? dueOnUtc) => new LmsEnrollment
			{
				DepartmentId = departmentId,
				LmsCourseId = course.LmsCourseId,
				LmsCourseVersionId = versionId,
				UserId = userId,
				Source = (int)source,
				RolePathKey = rolePathKey,
				AssignedByUserId = assignedBy,
				AssignedOn = DateTime.UtcNow,
				DueOn = dueOnUtc,
				Status = (int)LmsEnrollmentStatuses.NotStarted,
				IsOpen = true
			};

		public async Task<LmsResult> WithdrawEnrollmentAsync(LmsActor actor, int lmsEnrollmentId, CancellationToken cancellationToken = default)
		{
			var enrollment = await _enrollments.GetByIdForDepartmentAsync(actor?.DepartmentId ?? 0, lmsEnrollmentId);
			if (enrollment == null || !enrollment.IsOpen)
				return LmsResult.Fail(LmsErrors.NotFound);
			var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, enrollment.LmsCourseId);
			if (course == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			var allowed = course.IsSystem ? actor.MayAssignAcademy : actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId)).Assign;
			if (!allowed)
				return LmsResult.Fail(LmsErrors.NotAuthorized);

			var expected = enrollment.RowVersion;
			enrollment.Status = (int)LmsEnrollmentStatuses.Withdrawn;
			enrollment.IsOpen = false;
			enrollment.WithdrawnOn = DateTime.UtcNow;
			enrollment.WithdrawnByUserId = actor.UserId;
			if (!await _enrollments.TryUpdateAsync(enrollment, expected, cancellationToken))
				return LmsResult.Fail(LmsErrors.StaleCopy);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsEnrollmentWithdrawn, JsonConvert.SerializeObject(new { enrollment.LmsEnrollmentId, enrollment.UserId, course.Title }));
			return new LmsResult { Course = course, Enrollment = enrollment };
		}

		public async Task<List<LmsEnrollmentRow>> GetCourseEnrollmentsAsync(LmsActor actor, int lmsCourseId)
		{
			var rows = new List<LmsEnrollmentRow>();
			var course = await _courses.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsCourseId);
			if (course == null || actor == null)
				return rows;
			var rights = course.IsSystem ? null : actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId));
			if (course.IsSystem ? !actor.MayAssignAcademy : !(rights.Report || rights.Assign))
				return rows;

			var enrollments = await _enrollments.GetForCourseAsync(actor.DepartmentId, lmsCourseId) ?? new List<LmsEnrollment>();
			var versions = (await _versions.GetVisibleByIdsAsync(actor.DepartmentId, enrollments.Select(e => e.LmsCourseVersionId)) ?? new List<LmsCourseVersion>()).ToDictionary(v => v.LmsCourseVersionId);
			var now = DateTime.UtcNow;
			foreach (var enrollment in enrollments.Where(e => e.Status != (int)LmsEnrollmentStatuses.Withdrawn))
				rows.Add(new LmsEnrollmentRow
				{
					Enrollment = enrollment,
					VersionLabel = versions.TryGetValue(enrollment.LmsCourseVersionId, out var v) ? v.Label : null,
					IsOverdue = enrollment.IsOpen && enrollment.DueOn.HasValue && enrollment.DueOn.Value < now
				});
			return rows;
		}

		#endregion Catalog and enrollments

		#region Player

		/// <summary>The enrollment, its course and version outline, and the gate: or why it may not run.</summary>
		private async Task<(LmsEnrollment Enrollment, LmsCourseOutline Outline, LmsDepartmentGate Gate, LmsErrors Error)> LoadAsync(int departmentId, string userId, int lmsEnrollmentId,
			bool requireRun = true)
		{
			var enrollment = await _enrollments.GetByIdForDepartmentAsync(departmentId, lmsEnrollmentId);
			if (enrollment == null || !string.Equals(enrollment.UserId, userId, StringComparison.OrdinalIgnoreCase) || enrollment.Status == (int)LmsEnrollmentStatuses.Withdrawn)
				return (null, null, null, LmsErrors.NotFound);

			var version = await _versions.GetVisibleByIdAsync(departmentId, enrollment.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(departmentId, enrollment.LmsCourseId);
			if (version == null || course == null)
				return (enrollment, null, null, LmsErrors.NotFound);

			var outline = LmsCourseService.BuildOutline(course, version,
				await _modules.GetForVersionAsync(departmentId, version.LmsCourseVersionId), await _lessons.GetForVersionAsync(departmentId, version.LmsCourseVersionId));
			var gate = await _gate.GetGateAsync(departmentId);
			if (requireRun && !LmsLapseRules.CanRun(gate, course.IsSystem, enrollment, DateTime.UtcNow))
				return (enrollment, outline, gate, course.IsSystem ? LmsErrors.NotAuthorized : gate.IsLapsed ? LmsErrors.LapseWindowEnded : LmsErrors.AddonRequired);
			if (course.IsSystem && !await _entitlements.IsVisibleAsync(departmentId, course))
				return (enrollment, outline, gate, LmsErrors.NotEntitled);
			return (enrollment, outline, gate, LmsErrors.None);
		}

		/// <summary>Lessons that cannot be opened yet: those after a required lesson that is not complete (pre-tests never lock).</summary>
		internal static HashSet<int> LockedLessons(LmsCourseOutline outline, IDictionary<int, LmsLessonProgress> progress)
		{
			var locked = new HashSet<int>();
			var blocked = false;
			foreach (var lesson in outline.Lessons)
			{
				if (blocked && !lesson.IsPreTest)
					locked.Add(lesson.LmsLessonId);
				var done = progress.TryGetValue(lesson.LmsLessonId, out var p) && p.Status == (int)LmsLessonProgressStatuses.Completed;
				if (lesson.IsRequired && !lesson.IsPreTest && !done)
					blocked = true;
			}
			return locked;
		}

		public async Task<LmsPlayerView> GetPlayerAsync(int departmentId, string userId, int lmsEnrollmentId, int? lmsLessonId)
		{
			var (enrollment, outline, gate, error) = await LoadAsync(departmentId, userId, lmsEnrollmentId);
			var view = new LmsPlayerView { Error = error, Enrollment = enrollment, Outline = outline };
			if (error != LmsErrors.None)
				return view;

			view.Progress = (await _progress.GetForEnrollmentAsync(departmentId, enrollment.LmsEnrollmentId) ?? new List<LmsLessonProgress>())
				.GroupBy(p => p.LmsLessonId).ToDictionary(g => g.Key, g => g.First());
			view.LockedLessonIds = LockedLessons(outline, view.Progress);
			var lessons = outline.Lessons.ToList();
			if (lessons.Count == 0)
				return view;

			LmsLesson Pick(int? id) => id.HasValue ? lessons.FirstOrDefault(l => l.LmsLessonId == id.Value && !view.LockedLessonIds.Contains(l.LmsLessonId)) : null;
			view.Lesson = Pick(lmsLessonId)
				?? Pick(enrollment.CurrentLessonId)
				?? lessons.FirstOrDefault(l => !view.LockedLessonIds.Contains(l.LmsLessonId) && !(view.Progress.TryGetValue(l.LmsLessonId, out var p) && p.Status == (int)LmsLessonProgressStatuses.Completed))
				?? lessons.First();

			var index = lessons.IndexOf(view.Lesson);
			view.PreviousLessonId = index > 0 ? lessons[index - 1].LmsLessonId : (int?)null;
			view.NextLessonId = index < lessons.Count - 1 ? lessons[index + 1].LmsLessonId : (int?)null;
			view.LessonProgress = view.Progress.TryGetValue(view.Lesson.LmsLessonId, out var current) ? current : null;
			var now = DateTime.UtcNow;

			switch ((LmsLessonKinds)view.Lesson.Kind)
			{
				case LmsLessonKinds.Assessment:
					view.Attempts = await _attempts.GetForEnrollmentLessonAsync(departmentId, enrollment.LmsEnrollmentId, view.Lesson.LmsLessonId) ?? new List<LmsAssessmentAttempt>();
					var (blocked, nextOn) = LmsAttemptRules.CanStart(view.Lesson, view.Attempts, now);
					view.AttemptBlockedBy = LmsAttemptRules.OpenAttempt(view.Attempts, now) != null ? LmsErrors.None : blocked;
					view.NextAttemptOn = nextOn;
					break;
				case LmsLessonKinds.ProtocolVersion:
					if (view.Lesson.ProtocolVersionId.HasValue && !outline.Course.IsSystem)
						view.Protocol = await _protocolVersions.GetByIdForDepartmentAsync(departmentId, view.Lesson.ProtocolVersionId.Value);
					break;
				case LmsLessonKinds.Video:
					view.Captions = LmsCaptionTrack.Parse(view.Lesson.CaptionTracksJson);
					if (view.Lesson.VideoSource == (int)LmsVideoSources.Hosted && view.Lesson.LmsMediaAssetId.HasValue)
					{
						view.Media = await _media.GetByIdForDepartmentAsync(departmentId, view.Lesson.LmsMediaAssetId.Value);
						view.HostedVideoPlayable = view.Media != null && view.Media.Status == (int)LmsMediaStatuses.Ready && LmsLapseRules.CanStreamHostedVideo(gate, now);
					}
					break;
				case LmsLessonKinds.Survey:
					view.SurveyAnswered = view.LessonProgress?.Status == (int)LmsLessonProgressStatuses.Completed;
					break;
			}

			return view;
		}

		public async Task<LmsResult> SaveProgressAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, LmsProgressInput input,
			CancellationToken cancellationToken = default)
		{
			var (enrollment, outline, _, error) = await LoadAsync(departmentId, userId, lmsEnrollmentId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);
			var lesson = outline.Lessons.FirstOrDefault(l => l.LmsLessonId == lmsLessonId);
			if (lesson == null)
				return LmsResult.Fail(LmsErrors.NotFound);

			var now = DateTime.UtcNow;
			var progress = await _progress.GetAsync(departmentId, enrollment.LmsEnrollmentId, lmsLessonId) ?? new LmsLessonProgress
			{
				DepartmentId = departmentId,
				LmsEnrollmentId = enrollment.LmsEnrollmentId,
				LmsLessonId = lmsLessonId,
				UserId = enrollment.UserId,
				Status = (int)LmsLessonProgressStatuses.InProgress,
				StartedOn = now
			};

			var seconds = LmsSeatTime.Accrue(progress.LastSeenOn, now, input?.Seconds ?? 0);
			progress.SeatSeconds += seconds;
			progress.LastSeenOn = now;
			if (input?.VideoPositionSeconds.HasValue == true)
				progress.VideoPositionSeconds = Math.Max(0, input.VideoPositionSeconds.Value);
			if (input?.WatchedPercent.HasValue == true)
			{
				var claimed = Math.Max(0, Math.Min(100, input.WatchedPercent.Value));
				// A hosted video's length is known: the watched share cannot outrun the seat time at double speed (R-C15).
				if (lesson.VideoSource == (int)LmsVideoSources.Hosted && lesson.LmsMediaAssetId.HasValue)
				{
					var media = await _media.GetByIdForDepartmentAsync(departmentId, lesson.LmsMediaAssetId.Value);
					if (media != null && media.DurationSeconds > 0)
						claimed = Math.Min(claimed, (int)Math.Floor((progress.SeatSeconds * 2.0 + 10) * 100.0 / media.DurationSeconds));
				}
				progress.WatchedPercent = Math.Max(progress.WatchedPercent, Math.Min(100, claimed));
			}
			await _progress.SaveOrUpdateAsync(progress, cancellationToken, true);

			await TouchEnrollmentAsync(departmentId, enrollment, lmsLessonId, seconds, now, cancellationToken);
			return new LmsResult { Enrollment = enrollment, Value = progress };
		}

		private async Task TouchEnrollmentAsync(int departmentId, LmsEnrollment enrollment, int lessonId, int seconds, DateTime now, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < 3 && enrollment != null; attempt++)
			{
				var expected = enrollment.RowVersion;
				enrollment.StartedOn ??= now;
				if (enrollment.Status == (int)LmsEnrollmentStatuses.NotStarted)
					enrollment.Status = (int)LmsEnrollmentStatuses.InProgress;
				enrollment.CurrentLessonId = lessonId;
				enrollment.SeatSeconds += seconds;
				enrollment.LastActivityOn = now;
				if (await _enrollments.TryUpdateAsync(enrollment, expected, cancellationToken))
					return;
				enrollment = await _enrollments.GetByIdForDepartmentAsync(departmentId, enrollment.LmsEnrollmentId);
			}
		}

		public async Task<LmsResult> CompleteLessonAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, LmsLessonCompletionInput input,
			CancellationToken cancellationToken = default)
		{
			var (enrollment, outline, _, error) = await LoadAsync(departmentId, userId, lmsEnrollmentId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);
			var lesson = outline.Lessons.FirstOrDefault(l => l.LmsLessonId == lmsLessonId);
			if (lesson == null)
				return LmsResult.Fail(LmsErrors.NotFound);

			var progressMap = (await _progress.GetForEnrollmentAsync(departmentId, enrollment.LmsEnrollmentId) ?? new List<LmsLessonProgress>())
				.GroupBy(p => p.LmsLessonId).ToDictionary(g => g.Key, g => g.First());
			if (LockedLessons(outline, progressMap).Contains(lmsLessonId))
				return LmsResult.Fail(LmsErrors.LessonLocked);

			progressMap.TryGetValue(lmsLessonId, out var progress);
			if (progress?.Status == (int)LmsLessonProgressStatuses.Completed)
				return new LmsResult { Enrollment = enrollment, Value = progress };

			var now = DateTime.UtcNow;
			progress ??= new LmsLessonProgress
			{
				DepartmentId = departmentId, LmsEnrollmentId = enrollment.LmsEnrollmentId, LmsLessonId = lmsLessonId, UserId = enrollment.UserId, StartedOn = now, LastSeenOn = now
			};

			var kind = (LmsLessonKinds)lesson.Kind;
			if (kind == LmsLessonKinds.Survey)
				return LmsResult.Fail(LmsErrors.RequirementNotMet);
			if (kind == LmsLessonKinds.Assessment)
			{
				var attempts = await _attempts.GetForEnrollmentLessonAsync(departmentId, enrollment.LmsEnrollmentId, lmsLessonId) ?? new List<LmsAssessmentAttempt>();
				if (!attempts.Any(a => a.Passed == true))
					return LmsResult.Fail(LmsErrors.RequirementNotMet);
			}
			else
			{
				if (progress.SeatSeconds < lesson.MinimumSeatSeconds)
					return new LmsResult { Error = LmsErrors.SeatTimeNotMet, Value = progress };
				if (kind == LmsLessonKinds.Video && progress.WatchedPercent < lesson.RequiredWatchPercent)
					return new LmsResult { Error = LmsErrors.SeatTimeNotMet, Value = progress };
			}

			if (kind == LmsLessonKinds.Blocks && !string.IsNullOrWhiteSpace(lesson.FreeResponsePrompt))
			{
				var response = input?.ResponseText?.Trim();
				if (string.IsNullOrWhiteSpace(response))
					return LmsResult.Fail(LmsErrors.ResponseRequired);
				progress.ResponseText = response.Length > MaxResponseLength ? response.Substring(0, MaxResponseLength) : response;
			}

			if (kind == LmsLessonKinds.Blocks && !string.IsNullOrWhiteSpace(lesson.FileRequestPrompt))
			{
				if (input?.FileData == null || input.FileData.Length == 0)
					return LmsResult.Fail(LmsErrors.ResponseRequired);
				if (input.FileData.LongLength > LmsLessonFile.MaxBytes)
					return LmsResult.Fail(LmsErrors.FileTooLarge);
				if (string.IsNullOrWhiteSpace(input.ContentType) || !AllowedFileTypes.Contains(input.ContentType.Trim()))
					return LmsResult.Fail(LmsErrors.UnsupportedFile);

				var file = new LmsLessonFile
				{
					DepartmentId = departmentId,
					LmsLessonId = lmsLessonId,
					UserId = enrollment.UserId,
					FileName = LmsCourseService.Truncate(System.IO.Path.GetFileName(input.FileName ?? "upload"), 260),
					ContentType = input.ContentType.Trim(),
					SizeBytes = input.FileData.LongLength,
					Data = input.FileData,
					UploadedOn = now
				};
				await _files.InsertAsync(file, cancellationToken, true);
				progress.LmsLessonFileId = file.LmsLessonFileId;
			}

			progress.Status = (int)LmsLessonProgressStatuses.Completed;
			progress.CompletedOn = now;
			await _progress.SaveOrUpdateAsync(progress, cancellationToken, true);
			progressMap[lmsLessonId] = progress;

			var completed = await EvaluateCompletionAsync(enrollment, outline, progressMap, false, cancellationToken);
			return new LmsResult { Enrollment = enrollment, Value = progress, Count = completed ? 1 : 0 };
		}

		public async Task<LmsResult> SubmitSurveyAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, IDictionary<string, string> answers,
			CancellationToken cancellationToken = default)
		{
			var (enrollment, outline, _, error) = await LoadAsync(departmentId, userId, lmsEnrollmentId);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);
			var lesson = outline.Lessons.FirstOrDefault(l => l.LmsLessonId == lmsLessonId && l.Kind == (int)LmsLessonKinds.Survey);
			if (lesson == null)
				return LmsResult.Fail(LmsErrors.NotFound);

			var progressMap = (await _progress.GetForEnrollmentAsync(departmentId, enrollment.LmsEnrollmentId) ?? new List<LmsLessonProgress>())
				.GroupBy(p => p.LmsLessonId).ToDictionary(g => g.Key, g => g.First());
			if (LockedLessons(outline, progressMap).Contains(lmsLessonId))
				return LmsResult.Fail(LmsErrors.LessonLocked);
			if (progressMap.TryGetValue(lmsLessonId, out var existing) && existing.Status == (int)LmsLessonProgressStatuses.Completed)
				return LmsResult.Fail(LmsErrors.AlreadySubmitted);

			var clean = CleanSurveyAnswers(LmsSurveyQuestion.Parse(lesson.SurveyJson), answers);
			if (clean == null)
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var now = DateTime.UtcNow;
			// Anonymous: no user id, and only the day (a completion time could otherwise identify the respondent).
			await _surveys.InsertAsync(new LmsSurveyResponse
			{
				DepartmentId = departmentId,
				LmsCourseVersionId = enrollment.LmsCourseVersionId,
				LmsLessonId = lmsLessonId,
				AnswersJson = JsonConvert.SerializeObject(clean),
				SubmittedOn = now.Date
			}, cancellationToken, true);

			var progress = existing ?? new LmsLessonProgress
			{
				DepartmentId = departmentId, LmsEnrollmentId = enrollment.LmsEnrollmentId, LmsLessonId = lmsLessonId, UserId = enrollment.UserId, StartedOn = now
			};
			progress.Status = (int)LmsLessonProgressStatuses.Completed;
			progress.CompletedOn = now;
			progress.LastSeenOn = now;
			await _progress.SaveOrUpdateAsync(progress, cancellationToken, true);
			progressMap[lmsLessonId] = progress;

			var completed = await EvaluateCompletionAsync(enrollment, outline, progressMap, false, cancellationToken);
			return new LmsResult { Enrollment = enrollment, Count = completed ? 1 : 0 };
		}

		/// <summary>Answers by question key, validated per kind (rating 1–5, a listed option, text up to 2000); null when a question is missing.</summary>
		internal static Dictionary<string, string> CleanSurveyAnswers(IEnumerable<LmsSurveyQuestion> questions, IDictionary<string, string> answers)
		{
			var clean = new Dictionary<string, string>(StringComparer.Ordinal);
			answers ??= new Dictionary<string, string>();
			foreach (var question in questions ?? Enumerable.Empty<LmsSurveyQuestion>())
			{
				answers.TryGetValue(question.Key ?? string.Empty, out var value);
				value = value?.Trim();
				switch ((LmsSurveyQuestionKinds)question.Kind)
				{
					case LmsSurveyQuestionKinds.Rating:
						if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rating) || rating < 1 || rating > 5)
							return null;
						clean[question.Key] = rating.ToString(CultureInfo.InvariantCulture);
						break;
					case LmsSurveyQuestionKinds.Choice:
						if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var option) || option < 0 || option >= (question.Options?.Count ?? 0))
							return null;
						clean[question.Key] = option.ToString(CultureInfo.InvariantCulture);
						break;
					default:
						// Free text is optional.
						if (!string.IsNullOrWhiteSpace(value))
							clean[question.Key] = value.Length > 2000 ? value.Substring(0, 2000) : value;
						break;
				}
			}
			return clean;
		}

		public async Task<LmsSurveyReport> GetSurveyReportAsync(LmsActor actor, int lmsLessonId)
		{
			var lesson = await _lessons.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsLessonId);
			if (lesson == null || lesson.Kind != (int)LmsLessonKinds.Survey || lesson.DepartmentId != actor.DepartmentId)
				return null;
			var version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, lesson.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
			if (course == null || !actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId)).Report)
				return null;

			// Responses of every version's copy of this survey would mix different questions: the report is per lesson (one version).
			var responses = await _surveys.GetForLessonAsync(actor.DepartmentId, lmsLessonId) ?? new List<LmsSurveyResponse>();
			return BuildSurveyReport(course, lesson, responses);
		}

		internal static LmsSurveyReport BuildSurveyReport(LmsCourse course, LmsLesson lesson, IReadOnlyCollection<LmsSurveyResponse> responses)
		{
			var report = new LmsSurveyReport { Course = course, Lesson = lesson, Responses = responses.Count };
			var parsed = responses.Select(r =>
			{
				try { return JsonConvert.DeserializeObject<Dictionary<string, string>>(r.AnswersJson ?? "{}") ?? new Dictionary<string, string>(); }
				catch (JsonException) { return new Dictionary<string, string>(); }
			}).ToList();

			foreach (var question in LmsSurveyQuestion.Parse(lesson.SurveyJson))
			{
				var item = new LmsSurveyQuestionReport { Question = question };
				var values = parsed.Select(p => p.TryGetValue(question.Key ?? string.Empty, out var v) ? v : null).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
				if (!report.Hidden)
				{
					switch ((LmsSurveyQuestionKinds)question.Kind)
					{
						case LmsSurveyQuestionKinds.Rating:
							var ratings = values.Select(v => int.TryParse(v, out var n) ? n : 0).Where(n => n >= 1 && n <= 5).ToList();
							item.Average = ratings.Count == 0 ? (decimal?)null : Math.Round((decimal)ratings.Average(), 2);
							foreach (var r in ratings)
								item.Counts[r.ToString(CultureInfo.InvariantCulture)] = item.Counts.TryGetValue(r.ToString(CultureInfo.InvariantCulture), out var c) ? c + 1 : 1;
							break;
						case LmsSurveyQuestionKinds.Choice:
							foreach (var v in values)
								if (int.TryParse(v, out var index) && index >= 0 && index < question.Options.Count)
									item.Counts[question.Options[index]] = item.Counts.TryGetValue(question.Options[index], out var c) ? c + 1 : 1;
							break;
						default:
							// Shuffled, so the order of comments does not follow the order learners finished in.
							item.Comments = values.OrderBy(v => v, StringComparer.Ordinal).ToList();
							break;
					}
				}
				report.Questions.Add(item);
			}

			return report;
		}

		public async Task<LmsLessonFile> GetLessonFileAsync(LmsActor actor, int lmsLessonFileId)
		{
			var file = await _files.GetByIdForDepartmentAsync(actor?.DepartmentId ?? 0, lmsLessonFileId);
			if (file == null)
				return null;
			if (string.Equals(file.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase) || actor.MayGrade)
				return file;
			var lesson = await _lessons.GetVisibleByIdAsync(actor.DepartmentId, file.LmsLessonId);
			var version = lesson == null ? null : await _versions.GetVisibleByIdAsync(actor.DepartmentId, lesson.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
			return course != null && actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId)).Report ? file : null;
		}

		#endregion Player

		#region Completion

		public async Task<bool> OnAttemptFinishedAsync(LmsAssessmentAttempt attempt, CancellationToken cancellationToken = default)
		{
			if (attempt == null || attempt.Passed != true)
				return false;

			var (enrollment, outline, _, _) = await LoadAsync(attempt.DepartmentId, attempt.UserId, attempt.LmsEnrollmentId, requireRun: false);
			if (enrollment == null || outline == null || !enrollment.IsOpen)
				return false;
			var lesson = outline.Lessons.FirstOrDefault(l => l.LmsLessonId == attempt.LmsLessonId);
			if (lesson == null)
				return false;

			var now = DateTime.UtcNow;
			var progressMap = (await _progress.GetForEnrollmentAsync(attempt.DepartmentId, enrollment.LmsEnrollmentId) ?? new List<LmsLessonProgress>())
				.GroupBy(p => p.LmsLessonId).ToDictionary(g => g.Key, g => g.First());
			progressMap.TryGetValue(lesson.LmsLessonId, out var progress);
			progress ??= new LmsLessonProgress
			{
				DepartmentId = attempt.DepartmentId, LmsEnrollmentId = enrollment.LmsEnrollmentId, LmsLessonId = lesson.LmsLessonId, UserId = enrollment.UserId, StartedOn = attempt.StartedOn
			};
			if (progress.Status != (int)LmsLessonProgressStatuses.Completed)
			{
				progress.Status = (int)LmsLessonProgressStatuses.Completed;
				progress.CompletedOn = attempt.SubmittedOn ?? now;
				progress.LastSeenOn = now;
				await _progress.SaveOrUpdateAsync(progress, cancellationToken, true);
			}
			progressMap[lesson.LmsLessonId] = progress;

			return await EvaluateCompletionAsync(enrollment, outline, progressMap, lesson.IsPreTest, cancellationToken);
		}

		public async Task<LmsResult> RecordCrewPresenceAsync(LmsCrewViewing viewing, string userId, CancellationToken cancellationToken = default)
		{
			if (viewing == null || string.IsNullOrWhiteSpace(userId))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var course = await _courses.GetVisibleByIdAsync(viewing.DepartmentId, viewing.LmsCourseId);
			var version = await _versions.GetVisibleByIdAsync(viewing.DepartmentId, viewing.LmsCourseVersionId);
			if (course == null || version == null)
				return LmsResult.Fail(LmsErrors.NotFound);

			var gate = await _gate.GetGateAsync(viewing.DepartmentId);
			var enrollment = await _enrollments.GetOpenAsync(viewing.DepartmentId, userId, course.LmsCourseId);
			var now = DateTime.UtcNow;
			if (enrollment == null)
			{
				if (!LmsLapseRules.CanAssignNew(gate, course.IsSystem))
					return LmsResult.Fail(course.IsSystem ? LmsErrors.NotAuthorized : LmsErrors.AddonRequired);
				(enrollment, _) = await _enrollments.InsertOpenAsync(NewEnrollment(viewing.DepartmentId, course, version.LmsCourseVersionId, userId, viewing.StartedByUserId,
					LmsEnrollmentSources.CrewViewing, null, null), cancellationToken);
			}
			else if (enrollment.LmsCourseVersionId != version.LmsCourseVersionId)
			{
				if (enrollment.StartedOn.HasValue)
					// The member is mid-way through another version (R-L12): present, but the lesson of this version is not theirs to complete.
					return new LmsResult { Error = LmsErrors.RequirementNotMet, Enrollment = enrollment };
				var expected = enrollment.RowVersion;
				enrollment.LmsCourseVersionId = version.LmsCourseVersionId;
				await _enrollments.TryUpdateAsync(enrollment, expected, cancellationToken);
			}

			if (!LmsLapseRules.CanRun(gate, course.IsSystem, enrollment, now))
				return LmsResult.Fail(gate.IsLapsed ? LmsErrors.LapseWindowEnded : LmsErrors.AddonRequired);

			var outline = LmsCourseService.BuildOutline(course, version,
				await _modules.GetForVersionAsync(viewing.DepartmentId, version.LmsCourseVersionId), await _lessons.GetForVersionAsync(viewing.DepartmentId, version.LmsCourseVersionId));
			var lesson = outline.Lessons.FirstOrDefault(l => l.LmsLessonId == viewing.LmsLessonId);
			if (lesson == null)
				return LmsResult.Fail(LmsErrors.NotFound);

			var progressMap = (await _progress.GetForEnrollmentAsync(viewing.DepartmentId, enrollment.LmsEnrollmentId) ?? new List<LmsLessonProgress>())
				.GroupBy(p => p.LmsLessonId).ToDictionary(g => g.Key, g => g.First());
			progressMap.TryGetValue(lesson.LmsLessonId, out var progress);
			progress ??= new LmsLessonProgress
			{
				DepartmentId = viewing.DepartmentId, LmsEnrollmentId = enrollment.LmsEnrollmentId, LmsLessonId = lesson.LmsLessonId, UserId = enrollment.UserId, StartedOn = viewing.StartedOn
			};

			// Watching together counts as watching (the quiz each member then takes on their own phone is what proves learning).
			if (lesson.Kind != (int)LmsLessonKinds.Assessment && lesson.Kind != (int)LmsLessonKinds.Survey && progress.Status != (int)LmsLessonProgressStatuses.Completed)
			{
				progress.Status = (int)LmsLessonProgressStatuses.Completed;
				progress.CompletedOn = now;
				progress.LmsCrewViewingId = viewing.LmsCrewViewingId;
				progress.WatchedPercent = 100;
				progress.SeatSeconds = Math.Max(progress.SeatSeconds, Math.Max(lesson.MinimumSeatSeconds, (int)Math.Min(int.MaxValue, (now - viewing.StartedOn).TotalSeconds)));
				progress.LastSeenOn = now;
				await _progress.SaveOrUpdateAsync(progress, cancellationToken, true);
				progressMap[lesson.LmsLessonId] = progress;
			}

			await TouchEnrollmentAsync(viewing.DepartmentId, enrollment, outline.Lessons.SkipWhile(l => l.LmsLessonId != lesson.LmsLessonId).Skip(1).FirstOrDefault()?.LmsLessonId ?? lesson.LmsLessonId, 0, now, cancellationToken);
			await EvaluateCompletionAsync(enrollment, outline, progressMap, false, cancellationToken);
			return new LmsResult { Course = course, Version = version, Enrollment = enrollment, Value = progress };
		}

		/// <summary>
		/// Completes the course when every required lesson is complete (or a pre-test was passed): the enrollment closes, one learning
		/// record is written (AcademyLesson for system content, LmsCourseCompletion otherwise; idempotent by the enrollment) and the
		/// version's certification is issued or renewed. True when the course completed now.
		/// </summary>
		internal async Task<bool> EvaluateCompletionAsync(LmsEnrollment enrollment, LmsCourseOutline outline, IDictionary<int, LmsLessonProgress> progress, bool passedPreTest,
			CancellationToken cancellationToken)
		{
			if (enrollment == null || !enrollment.IsOpen)
				return false;

			var required = outline.Lessons.Where(l => l.IsRequired && !l.IsPreTest).ToList();
			var done = required.Count(l => progress.TryGetValue(l.LmsLessonId, out var p) && p.Status == (int)LmsLessonProgressStatuses.Completed);
			var percent = required.Count == 0 ? 100 : (int)Math.Floor(done * 100.0 / required.Count);
			var complete = passedPreTest || (required.Count > 0 && done == required.Count);

			var now = DateTime.UtcNow;
			var fresh = enrollment;
			for (var attempt = 0; attempt < 3 && fresh != null; attempt++)
			{
				if (!fresh.IsOpen)
					return false;
				var expected = fresh.RowVersion;
				fresh.ProgressPercent = complete ? 100 : percent;
				fresh.StartedOn ??= now;
				fresh.LastActivityOn = now;
				if (complete)
				{
					fresh.Status = (int)LmsEnrollmentStatuses.Completed;
					fresh.IsOpen = false;
					fresh.CompletedOn = now;
					fresh.TestedOut = passedPreTest && done < required.Count;
				}
				else if (fresh.Status == (int)LmsEnrollmentStatuses.NotStarted)
				{
					fresh.Status = (int)LmsEnrollmentStatuses.InProgress;
				}

				if (await _enrollments.TryUpdateAsync(fresh, expected, cancellationToken))
					break;
				fresh = await _enrollments.GetByIdForDepartmentAsync(enrollment.DepartmentId, enrollment.LmsEnrollmentId);
				if (attempt == 2)
					return false;
			}

			if (!complete || fresh == null)
				return false;

			await RecordCompletionAsync(fresh, outline, cancellationToken);
			return true;
		}

		/// <summary>The transcript entry and the certification of a completed enrollment. Non-fatal: a failure is logged and the completion stands.</summary>
		internal async Task RecordCompletionAsync(LmsEnrollment enrollment, LmsCourseOutline outline, CancellationToken cancellationToken)
		{
			var course = outline.Course;
			var version = outline.Version;
			var changed = false;
			try
			{
				var categoryId = course.IsSystem ? await EnsureAcademyCategoryAsync(enrollment.DepartmentId, cancellationToken) : version.CategoryId;
				var lessonId = AcademyCourseManifest.LessonIdFromSystemKey(course.SystemKey);
				var draft = new LearningRecordDraft
				{
					UserId = enrollment.UserId,
					Title = LmsCourseService.Truncate(version.Title ?? course.Title, LearningRecord.MaxTitleLength),
					CourseCode = course.IsSystem ? (lessonId == null ? null : "RG-ACADEMY-" + lessonId) : course.Code,
					CompletedOn = enrollment.CompletedOn ?? DateTime.UtcNow,
					StartedOn = enrollment.StartedOn,
					EndedOn = enrollment.CompletedOn,
					Hours = version.Hours,
					CategoryId = categoryId,
					MethodOfTraining = (int)MethodsOfTraining.Online,
					CeProvider = course.IsSystem ? null : version.CeProvider,
					CeCourseNumber = course.IsSystem ? null : version.CeCourseNumber,
					Tags = course.Tags,
					Notes = version.Label == null ? null : "v" + version.Label + (enrollment.TestedOut ? " (test-out)" : string.Empty),
					Status = LearningRecordStatuses.Verified
				};
				var kind = course.IsSystem ? LearningRecordSourceKinds.AcademyLesson : LearningRecordSourceKinds.LmsCourseCompletion;
				var written = await _learningRecords.WriteSourceAsync(enrollment.DepartmentId, kind, SourceRefFor(enrollment), new[] { draft }, enrollment.UserId, cancellationToken);
				var record = written?.Records?.FirstOrDefault(r => r.Status != (int)LearningRecordStatuses.Voided);
				if (record != null && enrollment.LearningRecordId != record.LearningRecordId)
				{
					enrollment.LearningRecordId = record.LearningRecordId;
					changed = true;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The learning record of LMS enrollment {enrollment.LmsEnrollmentId} could not be written.");
			}

			if (!course.IsSystem && version.IssuesCertificationTypeId.HasValue)
			{
				try
				{
					var certificationId = await IssueOrRenewCertificationAsync(enrollment.DepartmentId, enrollment.UserId, version.IssuesCertificationTypeId.Value,
						enrollment.CompletedOn ?? DateTime.UtcNow, cancellationToken);
					if (certificationId.HasValue && enrollment.PersonnelCertificationId != certificationId)
					{
						enrollment.PersonnelCertificationId = certificationId;
						changed = true;
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"The certification of LMS enrollment {enrollment.LmsEnrollmentId} could not be issued.");
				}
			}

			if (changed)
				await _enrollments.TryUpdateAsync(enrollment, enrollment.RowVersion, cancellationToken);
		}

		/// <summary>The learning-record source key of an enrollment (one record per enrollment, R-T1).</summary>
		public static string SourceRefFor(LmsEnrollment enrollment) => "lms-enrollment:" + enrollment.LmsEnrollmentId.ToString(CultureInfo.InvariantCulture);

		/// <summary>
		/// Issues the certification a completion grants, or renews the member's record of that type (T2 item 6): the new expiry runs
		/// from the completion by the type's validity; a type that requires verification leaves the record PendingVerification.
		/// </summary>
		public async Task<int?> IssueOrRenewCertificationAsync(int departmentId, string userId, int certificationTypeId, DateTime completedOnUtc, CancellationToken cancellationToken = default)
		{
			var type = await _certificationService.GetCertificationTypeByIdAsync(certificationTypeId);
			if (type == null || type.DepartmentId != departmentId || type.IsDeleted || !type.IsActive || type.AppliesTo != (int)CertificationAppliesTo.Person)
				return null;

			DateTime? expires = type.NeverExpires || !type.DefaultValidityMonths.HasValue ? (DateTime?)null : completedOnUtc.Date.AddMonths(type.DefaultValidityMonths.Value);
			var existing = (await _certificationService.GetCertificationsForDepartmentAsync(departmentId, new[] { userId }) ?? new List<PersonnelCertification>())
				.Where(c => c != null && !c.IsDeleted && c.DepartmentCertificationTypeId == certificationTypeId && string.Equals(c.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(c => c.ExpiresOn ?? DateTime.MaxValue).FirstOrDefault();

			if (existing != null)
			{
				// Already valid beyond the new expiry: nothing to renew.
				if (expires.HasValue && existing.ExpiresOn.HasValue && existing.ExpiresOn.Value.Date >= expires.Value.Date &&
					existing.Status != (int)PersonnelCertificationStatuses.Expired)
					return existing.PersonnelCertificationId;
				if (!expires.HasValue && !type.NeverExpires)
					return existing.PersonnelCertificationId;

				// Renewed as the holder: a type that requires verification goes back to PendingVerification (CertificationService rule).
				var renewed = await _certificationService.RenewCertificationAsync(existing.PersonnelCertificationId, departmentId, expires, null, userId, cancellationToken);
				return renewed?.PersonnelCertificationId;
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var created = await _certificationService.SaveCertificationAsync(new PersonnelCertification
			{
				DepartmentId = departmentId,
				UserId = userId,
				Name = type.Type,
				Type = type.Type,
				DepartmentCertificationTypeId = certificationTypeId,
				IssuedBy = department?.Name,
				RecievedOn = completedOnUtc.Date,
				ExpiresOn = expires,
				Status = type.RequiresVerification ? (int)PersonnelCertificationStatuses.PendingVerification : (int)PersonnelCertificationStatuses.Active,
				StatusChangedOn = DateTime.UtcNow,
				StatusChangedByUserId = userId
			}, cancellationToken);
			return created?.PersonnelCertificationId;
		}

		/// <summary>The department's "Systems training" category (no CE) for Academy completions, created on first use (§15 A1).</summary>
		private async Task<int?> EnsureAcademyCategoryAsync(int departmentId, CancellationToken cancellationToken)
		{
			var categories = await _learningRecordService.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>();
			var category = categories.FirstOrDefault(c => !c.IsDeleted && string.Equals(c.ExternalCode, AcademyCourseManifest.CategoryCode, StringComparison.OrdinalIgnoreCase));
			if (category != null)
				return category.TrainingCategoryId;

			// The category name is department data: written in the managing member's language.
			var managing = (await _departmentsService.GetDepartmentByIdAsync(departmentId, false))?.ManagingUserId;
			var language = string.IsNullOrWhiteSpace(managing) ? null : (await _profiles.GetProfileByUserIdAsync(managing))?.Language;
			var saved = await _learningRecordService.SaveCategoryAsync(new TrainingCategory
			{
				DepartmentId = departmentId,
				Name = LmsText.Get("Academy.CategoryName", language),
				Code = AcademyCourseManifest.CategoryCode,
				ExternalCode = AcademyCourseManifest.CategoryCode,
				Kind = (int)TrainingCategoryKinds.Custom,
				Description = LmsText.Get("Academy.CategoryDescription", language),
				CreatedOn = DateTime.UtcNow
			}, null, cancellationToken);
			return saved?.TrainingCategoryId;
		}

		#endregion Completion

		#region Worker 88

		public async Task<int> SendInProgressRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var sent = 0;
			var idle = utcNow.AddDays(-Math.Max(1, Config.LmsConfig.InProgressReminderIdleDays));
			var due = await _enrollments.GetReminderDueAsync(idle, ReminderBatch) ?? new List<LmsEnrollment>();
			foreach (var department in due.GroupBy(e => e.DepartmentId))
			{
				try
				{
					// R-L9: due work first, then one gate answer per department per run.
					var gate = await _gate.GetGateAsync(department.Key);
					var dept = await _departmentsService.GetDepartmentByIdAsync(department.Key, false);
					if (dept == null)
						continue;
					var active = await _departmentsService.GetActiveMemberUserIdsAsync(department.Key) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					var courses = (await _courses.GetVisibleByIdsAsync(department.Key, department.Select(e => e.LmsCourseId)) ?? new List<LmsCourse>()).ToDictionary(c => c.LmsCourseId);
					var canTransmit = ConfigHelper.CanTransmit(department.Key);
					string number = null;

					foreach (var enrollment in department)
					{
						if (!courses.TryGetValue(enrollment.LmsCourseId, out var course) || !active.Contains(enrollment.UserId) ||
							!LmsLapseRules.CanRun(gate, course.IsSystem, enrollment, utcNow))
							continue;
						// Claim first: a second worker run never sends the same reminder twice.
						if (!await _enrollments.ClaimReminderAsync(department.Key, enrollment.LmsEnrollmentId, enrollment.ReminderSentOn, utcNow, cancellationToken))
							continue;
						if (!canTransmit)
							continue;

						number ??= await _settingsService.GetTextToCallNumberForDepartmentAsync(department.Key);
						var language = (await _profiles.GetProfileByUserIdAsync(enrollment.UserId))?.Language;
						var link = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Courses/Play/" + enrollment.LmsEnrollmentId.ToString(CultureInfo.InvariantCulture);
						var message = string.Format(LmsText.Culture(language), LmsText.Get("Notify.InProgressReminder", language), course.Title, link);
						await _communicationService.Value.SendNotificationAsync(enrollment.UserId, department.Key, message, number, dept, LmsText.Get("Notify.InProgressReminder.Title", language));
						sent++;
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"LMS in-progress reminders failed for department {department.Key}.");
				}
			}

			return sent;
		}

		#endregion Worker 88

		#region Helpers

		private async Task NotifyAssignedAsync(int departmentId, LmsCourse course, IEnumerable<string> userIds)
		{
			if (!ConfigHelper.CanTransmit(departmentId))
				return;
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				if (department == null)
					return;
				var number = await _settingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
				var link = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Courses/My";
				foreach (var userId in userIds)
				{
					var language = (await _profiles.GetProfileByUserIdAsync(userId))?.Language;
					var message = string.Format(LmsText.Culture(language), LmsText.Get("Notify.CourseAssigned", language), course.Title, link);
					await _communicationService.Value.SendNotificationAsync(userId, departmentId, message, number, department, LmsText.Get("Notify.CourseAssigned.Title", language));
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "LMS assignment notifications failed.");
			}
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}

		#endregion Helpers
	}
}
