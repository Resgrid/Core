using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.LmsClasses;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Classes (Training T4 part 1, LMS add-on, dark behind Training.Lms): class sessions on the Calendar with enrolment rules,
	/// waitlist and roster; the rotating-QR kiosk and the member's sign-in page (GPS when the session asks); attendance, walk-ins, meeting
	/// report import and the instructor's sign-off; instructors, costs and facility / prop booking; lesson plans (R-C6) and resources. Class
	/// managers are department admins and holders of Manage Courses (132) or Assign Training (126); a session's instructors run its roster.
	/// NotFound unless the Trainings module and the operator flag are on; while the add-on is lapsed everything stays readable, existing
	/// sessions can be run and signed off, and nothing new is made (lapse rule 3).
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsClassesController : SecureBaseController
	{
		private const long MaxImportBytes = 2 * 1024 * 1024;
		private static readonly Regex ServiceCode = new Regex("^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsClassService _classes;
		private readonly ILmsLessonPlanService _lessonPlans;
		private readonly ILmsCompetencyService _competencies;
		private readonly ILmsCourseService _courses;
		private readonly ITrainingService _trainings;
		private readonly ILearningRecordService _learningRecords;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly ITrainingTimecardOffers _timecards;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.LmsClasses.LmsClasses> _localizer;
		private readonly ILmsCompletionRecordingService _recordings;
		private readonly ITrainingCompletionRecordingService _recorded;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _trainingLocalizer;

		private LmsActor _actor;
		private Department _department;
		private Dictionary<string, string> _names;

		public LmsClassesController(ILmsGateService gate, ILmsAccessService access, ILmsClassService classes, ILmsLessonPlanService lessonPlans, ILmsCompetencyService competencies,
			ILmsCourseService courses, ITrainingService trainings, ILearningRecordService learningRecords, IDepartmentsService departments, IDepartmentGroupsService groups,
			ITrainingTimecardOffers timecards, IStringLocalizer<Resgrid.Localization.Areas.User.LmsClasses.LmsClasses> localizer, ILmsCompletionRecordingService recordings,
			ITrainingCompletionRecordingService recorded, IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> trainingLocalizer)
		{
			_gate = gate;
			_access = access;
			_classes = classes;
			_lessonPlans = lessonPlans;
			_competencies = competencies;
			_courses = courses;
			_trainings = trainings;
			_learningRecords = learningRecords;
			_departments = departments;
			_groups = groups;
			_timecards = timecards;
			_localizer = localizer;
			_recordings = recordings;
			_recorded = recorded;
			_trainingLocalizer = trainingLocalizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// Dark until the operator flag is on (and closed with the Trainings module): one gate read per request (R-L9).
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId) || !(await ActorAsync()).Gate.FlagOn)
			{
				context.Result = NotFound();
				return;
			}

			_department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			ViewData["LmsPrograms"] = true;
			await next();
		}

		private async Task<LmsActor> ActorAsync() => _actor ??= await _gate.GetActorAsync(DepartmentId, UserId) ?? LmsActor.Nobody(DepartmentId);

		/// <summary>Department admin, Manage Courses (132) or Assign Training (126).</summary>
		public static bool MayManageClasses(LmsActor actor) =>
			actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || actor.CanManageCourses || actor.CanAssignTraining);

		private async Task<bool> CanManageAsync() => MayManageClasses(await ActorAsync());

		private async Task<bool> CanRunAsync(int sessionId) => await CanManageAsync() || await _classes.IsInstructorAsync(DepartmentId, sessionId, UserId);

		private async Task<bool> CanAuthorAsync() => (await ActorAsync()).Gate.CanAuthor;

		private string L(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		private string ErrorText(Exception ex)
		{
			if (ex is InvalidOperationException && ex.Message != null && ServiceCode.IsMatch(ex.Message))
			{
				var text = _localizer?[ex.Message];
				if (text != null && !text.ResourceNotFound && !string.IsNullOrEmpty(text.Value))
					return text.Value;
			}
			return L("ActionFailed");
		}

		private void Flash(string messageKey, string error = null)
		{
			if (TempData == null)
				return;
			if (messageKey != null)
				TempData["LmsClassesMessage"] = L(messageKey);
			if (error != null)
				TempData["LmsClassesError"] = error;
		}

		private async Task<bool> TryAsync(Func<Task> work, string successKey)
		{
			try
			{
				await work();
				Flash(successKey);
				return true;
			}
			catch (InvalidOperationException ex)
			{
				Flash(null, ErrorText(ex));
				return false;
			}
		}

		private async Task<Dictionary<string, string>> NamesAsync() =>
			_names ??= (await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private async Task<List<KeyValuePair<string, string>>> SelectableMembersAsync() =>
			(await _departments.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(n => new KeyValuePair<string, string>(n.UserId, n.Name)).ToList();

		private DateTime ToLocal(DateTime utc) => _department == null ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(_department);

		private async Task<T> PageAsync<T>(T view, string tab) where T : LmsClassPageView
		{
			var actor = await ActorAsync();
			view.CanAuthor = actor.Gate.CanAuthor;
			view.IsLapsed = actor.Gate.IsLapsed;
			view.CanManage = MayManageClasses(actor);
			view.CanApprove = actor.MayManage;
			view.Department = _department;
			view.Names = await NamesAsync();
			view.Categories = await _learningRecords.GetCategoriesAsync(DepartmentId, true) ?? new List<TrainingCategory>();
			view.Message ??= TempData?["LmsClassesMessage"] as string;
			view.Error ??= TempData?["LmsClassesError"] as string;
			view.Local = ToLocal;

			ViewData["Section"] = "Classes";
			ViewData["SubTabs"] = Tabs(tab, view.CanManage);
			return view;
		}

		private ModuleTabsView Tabs(string active, bool manager)
		{
			var tabs = new ModuleTabsView { Label = L("StripLabel") };
			void Add(string key, string icon, string action) => tabs.Tabs.Add(new ModuleTab(L(key), icon, "LmsClasses", action, action == active));
			Add("Tab.Classes", "fa-calendar", "Index");
			Add("Tab.My", "fa-user", "My");
			if (manager)
			{
				Add("Tab.LessonPlans", "fa-book", "LessonPlans");
				Add("Tab.Resources", "fa-building-o", "Resources");
				Add("Tab.Costs", "fa-money", "Costs");
			}
			return tabs;
		}

		private static LmsClassSessionInput Input(LmsClassFormModel form, DateTime localStart, DateTime localEnd) => new LmsClassSessionInput
		{
			Title = form.Title,
			Description = form.Description,
			ContentSummary = form.ContentSummary,
			CourseKind = form.LmsCourseId.HasValue ? LmsClassCourseKinds.Course : form.TrainingId.HasValue ? LmsClassCourseKinds.Training : LmsClassCourseKinds.None,
			LmsCourseId = form.LmsCourseId,
			TrainingId = form.LmsCourseId.HasValue ? null : form.TrainingId,
			LmsLessonPlanId = form.LmsLessonPlanId,
			CategoryId = form.CategoryId,
			CourseCode = form.CourseCode,
			LocalStart = localStart,
			LocalEnd = localEnd,
			Location = form.Location,
			MeetingUrl = form.MeetingUrl,
			Capacity = Math.Max(0, form.Capacity),
			WaitlistEnabled = form.WaitlistEnabled,
			EnrollmentOpen = form.EnrollmentOpen,
			RequiresApproval = form.RequiresApproval,
			CutoffDays = Math.Max(0, form.CutoffDays),
			AllowSelfUnenroll = form.AllowSelfUnenroll,
			RestrictGroupIds = form.RestrictGroupIds ?? new List<int>(),
			VisibleGroupIds = form.VisibleGroupIds ?? new List<int>(),
			Tags = form.Tags,
			ReminderHours = Math.Max(0, form.ReminderHours),
			QrEnabled = form.QrEnabled,
			GpsRequired = form.GpsRequired,
			GpsLatitude = form.GpsLatitude,
			GpsLongitude = form.GpsLongitude,
			GpsRadiusMeters = form.GpsRadiusMeters,
			PostClassEvaluationKind = Enum.IsDefined(typeof(LmsClassPostEvaluationKinds), form.PostClassEvaluationKind) ? (LmsClassPostEvaluationKinds)form.PostClassEvaluationKind : 0,
			RetentionYears = form.RetentionYears,
			Currency = form.Currency,
			Repeat = Enum.IsDefined(typeof(LmsClassRepeatKinds), form.Repeat) ? (LmsClassRepeatKinds)form.Repeat : LmsClassRepeatKinds.None,
			Occurrences = Math.Max(1, form.Occurrences),
			InstructorUserIds = form.InstructorUserIds ?? new List<string>()
		};

		#endregion Gates and helpers

		#region Sessions

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Index(string tag)
		{
			var view = await PageAsync(new LmsClassesIndexView { Tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim() }, "Index");
			var now = DateTime.UtcNow;
			var group = await _groups.GetGroupForUserAsync(UserId, DepartmentId);
			var groupId = group != null && group.DepartmentId == DepartmentId ? group.DepartmentGroupId : (int?)null;
			var sessions = (await _classes.GetSessionsAsync(DepartmentId, now.AddDays(-60), now.AddDays(180)))
				.Where(s => LmsClassRules.IsVisibleTo(s, groupId, view.CanManage)).ToList();
			view.Tags = sessions.SelectMany(s => s.TagList()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
			if (view.Tag != null)
				sessions = sessions.Where(s => s.TagList().Contains(view.Tag, StringComparer.OrdinalIgnoreCase)).ToList();
			view.Upcoming = sessions.Where(s => s.EndsOn >= now).OrderBy(s => s.StartsOn).ToList();
			view.Recent = sessions.Where(s => s.EndsOn < now).OrderByDescending(s => s.StartsOn).ToList();
			view.Rosters = (await _classes.GetEnrollmentsAsync(DepartmentId, sessions.Select(s => s.LmsClassSessionId))).GroupBy(e => e.LmsClassSessionId)
				.ToDictionary(g => g.Key, g => g.ToList());
			view.PendingApprovals = view.Upcoming.Sum(s => view.Count(s.LmsClassSessionId, LmsClassEnrollmentStatuses.PendingApproval));
			view.Waitlisted = view.Upcoming.Sum(s => view.Count(s.LmsClassSessionId, LmsClassEnrollmentStatuses.Waitlisted));
			return View(view);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> My()
		{
			var now = DateTime.UtcNow;
			var view = await PageAsync(new LmsMyClassesView { UtcNow = now }, "My");
			view.Classes = await _classes.GetMyClassesAsync(DepartmentId, UserId, now);
			view.Taught = await _classes.GetTaughtAsync(DepartmentId, UserId);
			foreach (var id in view.Taught.Select(t => t.LmsClassSessionId).Distinct().Take(100))
			{
				var session = await _classes.GetSessionAsync(DepartmentId, id);
				if (session != null)
					view.TaughtSessions[id] = session;
			}
			return View(view);
		}

		private async Task<LmsClassEditView> EditPageAsync(LmsClassEditView view)
		{
			view = await PageAsync(view, "Index");
			var actor = await ActorAsync();
			view.Courses = (await _courses.GetCourseRowsAsync(actor) ?? new List<LmsCourseRow>())
				.Where(r => r?.Course != null && !r.Course.IsSystem && !r.Course.IsDeleted && r.Course.CurrentVersionId.HasValue)
				.OrderBy(r => r.Course.Title, StringComparer.CurrentCultureIgnoreCase).Select(r => new KeyValuePair<int, string>(r.Course.LmsCourseId, r.Course.Title)).ToList();
			view.Trainings = (await _trainings.GetAllTrainingsForDepartmentAsync(DepartmentId) ?? new List<Training>())
				.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).Select(t => new KeyValuePair<int, string>(t.TrainingId, t.Name)).ToList();
			view.LessonPlans = (await _lessonPlans.GetPlansAsync(DepartmentId)).Where(p => p.CurrentVersionId.HasValue).ToList();
			view.Groups = await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			view.Members = await SelectableMembersAsync();
			view.DefaultCurrency = await _classes.GetTrainingCurrencyAsync(DepartmentId);
			return view;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> New()
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var start = ToLocal(DateTime.UtcNow).Date.AddDays(7).AddHours(9);
			return View("Edit", await EditPageAsync(new LmsClassEditView
			{
				Input = new LmsClassSessionInput
				{
					LocalStart = start,
					LocalEnd = start.AddHours(2),
					EnrollmentOpen = true,
					AllowSelfUnenroll = true,
					QrEnabled = true,
					ReminderHours = 24,
					GpsRadiusMeters = LmsClassRules.DefaultGpsRadiusMeters,
					Occurrences = 1
				}
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> New(LmsClassFormModel form, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			if (!await CanAuthorAsync())
			{
				Flash(null, L("AddonRequired"));
				return RedirectToAction("Index");
			}

			List<LmsClassSession> created = null;
			if (await TryAsync(async () => created = await _classes.CreateAsync(DepartmentId, Input(form, form.Start, form.End), UserId, cancellationToken), "Created"))
				return RedirectToAction("Session", new { id = created[0].LmsClassSessionId });
			return RedirectToAction("New");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Edit(int id)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var session = await _classes.GetSessionAsync(DepartmentId, id);
			if (session == null)
				return NotFound();
			var instructors = await _classes.GetInstructorsAsync(DepartmentId, new[] { id });
			return View(await EditPageAsync(new LmsClassEditView
			{
				Session = session,
				Input = new LmsClassSessionInput
				{
					Title = session.Title,
					Description = session.Description,
					ContentSummary = session.ContentSummary,
					CourseKind = (LmsClassCourseKinds)session.CourseKind,
					LmsCourseId = session.LmsCourseId,
					TrainingId = session.TrainingId,
					LmsLessonPlanId = session.LmsLessonPlanId,
					CategoryId = session.CategoryId,
					CourseCode = session.CourseCode,
					LocalStart = ToLocal(session.StartsOn),
					LocalEnd = ToLocal(session.EndsOn),
					Location = session.Location,
					MeetingUrl = session.MeetingUrl,
					Capacity = session.Capacity,
					WaitlistEnabled = session.WaitlistEnabled,
					EnrollmentOpen = session.EnrollmentOpen,
					RequiresApproval = session.RequiresApproval,
					CutoffDays = session.CutoffDays,
					AllowSelfUnenroll = session.AllowSelfUnenroll,
					RestrictGroupIds = session.RestrictGroups(),
					VisibleGroupIds = session.VisibleGroups(),
					Tags = session.Tags,
					ReminderHours = session.ReminderHours,
					QrEnabled = session.QrEnabled,
					GpsRequired = session.GpsRequired,
					GpsLatitude = session.GpsLatitude,
					GpsLongitude = session.GpsLongitude,
					GpsRadiusMeters = session.GpsRadiusMeters,
					PostClassEvaluationKind = (LmsClassPostEvaluationKinds)session.PostClassEvaluationKind,
					RetentionYears = session.RetentionYears,
					Currency = session.Currency,
					InstructorUserIds = instructors.Where(i => i.IsMember).Select(i => i.UserId).ToList()
				}
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Edit(int id, LmsClassFormModel form, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			if (await TryAsync(() => _classes.UpdateAsync(DepartmentId, id, Input(form, form.Start, form.End), UserId, cancellationToken), "Saved"))
				return RedirectToAction("Session", new { id });
			return RedirectToAction("Edit", new { id });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Session(int id)
		{
			var now = DateTime.UtcNow;
			var detail = await _classes.GetDetailAsync(DepartmentId, id, now);
			if (detail == null)
				return NotFound();
			var canRun = await CanRunAsync(id);
			var group = await _groups.GetGroupForUserAsync(UserId, DepartmentId);
			var groupId = group != null && group.DepartmentId == DepartmentId ? group.DepartmentGroupId : (int?)null;
			var manager = await CanManageAsync();
			var mine = detail.Roster.FirstOrDefault(r => string.Equals(r.Enrollment.UserId, UserId, StringComparison.OrdinalIgnoreCase))?.Enrollment;
			if (!canRun && mine == null && !LmsClassRules.IsVisibleTo(detail.Session, groupId, manager))
				return NotFound();

			var view = await PageAsync(new LmsClassSessionView
			{
				Detail = detail,
				CanRun = canRun,
				Mine = mine,
				EnrollRefusal = LmsClassRules.SelfEnrollRefusal(detail.Session, now, groupId),
				UtcNow = now
			}, "Index");
			if (TempData?["LmsClassesImport"] is string import)
				view.Import = Newtonsoft.Json.JsonConvert.DeserializeObject<LmsAttendanceImportResult>(import);
			if (canRun)
			{
				view.Members = await SelectableMembersAsync();
				view.Resources = await _classes.GetResourcesAsync(DepartmentId);
				view.TimecardAvailable = await _timecards.IsAvailableAsync(DepartmentId);
			}
			if (!string.IsNullOrWhiteSpace(detail.Session.SeriesKey))
				view.Series = await _classes.GetSeriesAsync(DepartmentId, detail.Session.SeriesKey);
			// Round 7B: the "Record completion" panel for holders of Record Training Completions (135).
			if (ClaimsAuthorizationHelper.CanApplyTrainingCompletions())
			{
				var recording = await _recordings.GetClassRecordingViewAsync(DepartmentId, id, UserId);
				view.Recording = recording?.CanRecord == true ? recording : null;
			}
			return View(view);
		}

		/// <summary>
		/// Round 7B (permission 135): record that the selected roster members completed the class's LMS course or baseline training. The
		/// service checks the permission and group scope, the roster, active members, seats and the attestation, member by member.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> RecordCompletions(int id, List<string> userIds, string note, bool attested, string attestationVersion, CancellationToken cancellationToken)
		{
			var outcome = await _recordings.RecordFromClassAsync(DepartmentId, id, new TrainingCompletionRecordingRequest
			{
				RecorderUserId = UserId,
				MemberUserIds = userIds ?? new List<string>(),
				Note = note,
				Attested = attested,
				AttestationVersion = string.IsNullOrWhiteSpace(attestationVersion) ? TrainingCompletionAttestation.CurrentVersion : attestationVersion
			}, cancellationToken);
			FlashRecording(outcome);
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> VoidRecordedCompletion(int id, int recordingId, string reason, CancellationToken cancellationToken)
		{
			var recording = await _recorded.GetAsync(DepartmentId, recordingId);
			if (recording == null || recording.SourceKind != (int)TrainingCompletionRecordingSources.ClassSession ||
				recording.SourceRefId != id.ToString(CultureInfo.InvariantCulture))
				return NotFound();
			var result = await _recorded.VoidAsync(DepartmentId, recordingId, UserId, reason, cancellationToken);
			if (TempData != null)
			{
				if (result.Refusal == null)
					TempData["LmsClassesMessage"] = T("RecordCompletions.VoidDone");
				else
					TempData["LmsClassesError"] = T(result.Refusal);
			}
			return RedirectToAction("Session", new { id });
		}

		private string T(string key)
		{
			var value = _trainingLocalizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		private void FlashRecording(TrainingCompletionRecordingOutcome outcome)
		{
			if (TempData == null)
				return;
			if (!outcome.Succeeded)
			{
				TempData["LmsClassesError"] = T(outcome.Error);
				return;
			}
			TempData["LmsClassesMessage"] = string.Format(CultureInfo.CurrentCulture, T("RecordCompletions.ResultFormat"), outcome.Recorded, outcome.Refused);
			var refusals = outcome.Results.Where(r => !r.Recorded).GroupBy(r => r.Refusal).Select(g => T(g.Key) + " (" + g.Count().ToString(CultureInfo.CurrentCulture) + ")").ToList();
			if (refusals.Count > 0)
				TempData["LmsClassesError"] = string.Join(" ", refusals);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Series(string key)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var sessions = await _classes.GetSeriesAsync(DepartmentId, key);
			if (sessions.Count == 0)
				return NotFound();
			var view = await PageAsync(new LmsClassSeriesView { Sessions = sessions }, "Index");
			view.Roster = await _classes.GetEnrollmentsAsync(DepartmentId, sessions.Select(s => s.LmsClassSessionId));
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Cancel(int id, string reason, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _classes.CancelAsync(DepartmentId, id, reason, UserId, cancellationToken), "Cancelled");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _classes.DeleteAsync(DepartmentId, id, UserId, cancellationToken), "Deleted");
			return RedirectToAction("Index");
		}

		/// <summary>The round-4C seam: "make it a class session" from an annual plan line.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> FromPlanItem(int id, int planId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			if (!await CanAuthorAsync())
			{
				Flash(null, L("AddonRequired"));
				return RedirectToAction("Plan", "LmsPrograms", new { id = planId });
			}

			LmsClassSession session = null;
			if (await TryAsync(async () => session = await _classes.CreateFromPlanItemAsync(DepartmentId, id, UserId, cancellationToken), "Created"))
				return RedirectToAction("Session", new { id = session.LmsClassSessionId });
			return RedirectToAction("Plan", "LmsPrograms", new { id = planId });
		}

		#endregion Sessions

		#region Enrolment

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Enroll(int id, CancellationToken cancellationToken)
		{
			if (!await CanAuthorAsync())
				Flash(null, L("AddonRequired"));
			else
				await TryAsync(() => _classes.EnrollSelfAsync(DepartmentId, id, UserId, DateTime.UtcNow, cancellationToken), "EnrolledMessage");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Unenroll(int id, CancellationToken cancellationToken)
		{
			await TryAsync(() => _classes.UnenrollSelfAsync(DepartmentId, id, UserId, DateTime.UtcNow, cancellationToken), "WithdrawnMessage");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> AddMember(int id, string userId, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			if (!await CanAuthorAsync())
				Flash(null, L("AddonRequired"));
			else
				await TryAsync(() => _classes.EnrollMemberAsync(DepartmentId, id, userId, UserId, DateTime.UtcNow, cancellationToken), "EnrolledMessage");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RemoveMember(int id, int enrollmentId, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			await TryAsync(() => _classes.RemoveMemberAsync(DepartmentId, enrollmentId, UserId, DateTime.UtcNow, cancellationToken), "RemovedMessage");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Decide(int id, int enrollmentId, bool approve, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			await TryAsync(() => _classes.DecideAsync(DepartmentId, enrollmentId, approve, UserId, DateTime.UtcNow, cancellationToken), approve ? "ApprovedMessage" : "DeclinedMessage");
			return RedirectToAction("Session", new { id });
		}

		#endregion Enrolment

		#region Attendance, sign-in and sign-off

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Attendance(int id, int enrollmentId, int state, string hours, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			decimal? hoursOverride = decimal.TryParse(hours, NumberStyles.Number, CultureInfo.CurrentCulture, out var h) || decimal.TryParse(hours, NumberStyles.Number, CultureInfo.InvariantCulture, out h)
				? h : null;
			await TryAsync(() => _classes.MarkAttendanceAsync(DepartmentId, enrollmentId, (LmsClassAttendanceStates)state, hoursOverride, UserId, DateTime.UtcNow, cancellationToken), "Saved");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> WalkIn(int id, string userId, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			await TryAsync(() => _classes.AddWalkInAsync(DepartmentId, id, userId, UserId, DateTime.UtcNow, cancellationToken), "WalkInMessage");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxImportBytes + 64 * 1024)]
		public async Task<IActionResult> ImportAttendance(int id, IFormFile file, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			if (file == null || file.Length <= 0 || file.Length > MaxImportBytes)
			{
				Flash(null, L("lms_class_import_empty"));
				return RedirectToAction("Session", new { id });
			}

			string text;
			using (var stream = file.OpenReadStream())
			using (var reader = new StreamReader(stream, Encoding.UTF8, true))
				text = await reader.ReadToEndAsync(cancellationToken);
			LmsAttendanceImportResult result = null;
			if (await TryAsync(async () => result = await _classes.ImportAttendanceAsync(DepartmentId, id, text, UserId, DateTime.UtcNow, cancellationToken), "ImportedMessage") && TempData != null)
				TempData["LmsClassesImport"] = Newtonsoft.Json.JsonConvert.SerializeObject(new LmsAttendanceImportResult
				{
					Rows = result.Rows, Matched = result.Matched, WalkIns = result.WalkIns, Unmatched = result.Unmatched.Take(50).ToList()
				});
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SignOff(int id, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			LmsClassSignOffResult result = null;
			if (await TryAsync(async () => result = await _classes.SignOffAsync(DepartmentId, id, UserId, DateTime.UtcNow, cancellationToken), null) && TempData != null)
				TempData["LmsClassesMessage"] = string.Format(CultureInfo.CurrentCulture, L("SignedOffFormat"), result.Attended, result.Absent, result.Written, result.Voided);
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RotateQr(int id, CancellationToken cancellationToken)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			await TryAsync(() => _classes.RotateQrAsync(DepartmentId, id, UserId, cancellationToken), "QrRotated");
			return RedirectToAction("Kiosk", new { id });
		}

		/// <summary>The kiosk screen: the rotating QR (every 30 seconds) and who has signed in.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Kiosk(int id)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			var detail = await _classes.GetDetailAsync(DepartmentId, id, DateTime.UtcNow);
			if (detail == null)
				return NotFound();
			var code = _classes.CurrentCode(detail.Session, DateTime.UtcNow);
			var url = code == null ? null : GetBaseUrl() + Url.Action("SignIn", new { s = id, c = code });
			return View(await PageAsync(new LmsClassKioskView
			{
				Session = detail.Session,
				Open = code != null,
				SignInUrl = url,
				QrDataUrl = url == null ? null : WebQrCode.DataUrl(url),
				Present = detail.Roster.Where(r => r.Enrollment.CheckInOn.HasValue).Select(r => r.Name).ToList()
			}, "Index"));
		}

		/// <summary>The kiosk polls this for the rotating code (JSON).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Code(int id)
		{
			if (!await CanRunAsync(id))
				return Unauthorized();
			var detail = await _classes.GetDetailAsync(DepartmentId, id, DateTime.UtcNow);
			if (detail == null)
				return NotFound();
			var code = _classes.CurrentCode(detail.Session, DateTime.UtcNow);
			var url = code == null ? null : GetBaseUrl() + Url.Action("SignIn", new { s = id, c = code });
			Response.Headers["Cache-Control"] = "no-store";
			return Json(new
			{
				open = code != null,
				qr = url == null ? null : WebQrCode.DataUrl(url),
				url,
				present = detail.Roster.Where(r => r.Enrollment.CheckInOn.HasValue).Select(r => r.Name).ToList()
			});
		}

		/// <summary>What a member's phone opens from the QR: one button signs them in (the GET itself changes nothing).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SignIn(int s, string c)
		{
			var session = await _classes.GetSessionAsync(DepartmentId, s);
			if (session == null)
				return NotFound();
			var detail = await _classes.GetDetailAsync(DepartmentId, s, DateTime.UtcNow);
			return View(await PageAsync(new LmsClassSignInView
			{
				Session = session,
				Code = c,
				Open = LmsClassRules.IsSignInOpen(session, DateTime.UtcNow),
				Mine = detail?.Roster.FirstOrDefault(r => string.Equals(r.Enrollment.UserId, UserId, StringComparison.OrdinalIgnoreCase))?.Enrollment
			}, "My"));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ActionName("SignIn")]
		public async Task<IActionResult> SignInPost(int s, string c, string latitude, string longitude, CancellationToken cancellationToken)
		{
			decimal? lat = decimal.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var la) ? la : null;
			decimal? lon = decimal.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) ? lo : null;
			if (await TryAsync(() => _classes.SignInAsync(DepartmentId, s, UserId, c, lat, lon, DateTime.UtcNow, cancellationToken), "SignedInMessage"))
				return RedirectToAction("Session", new { id = s });
			return RedirectToAction("SignIn", new { s, c });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SignOutOfClass(int id, CancellationToken cancellationToken)
		{
			await TryAsync(() => _classes.SignOutAsync(DepartmentId, id, UserId, DateTime.UtcNow, cancellationToken), "SignedOutMessage");
			return RedirectToAction("Session", new { id });
		}

		#endregion Attendance, sign-in and sign-off

		#region Instructors, costs and bookings

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> AddInstructor(int id, string userId, string name, string qualifications, int role, string hoursTaught, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			decimal? hours = decimal.TryParse(hoursTaught, NumberStyles.Number, CultureInfo.CurrentCulture, out var h) || decimal.TryParse(hoursTaught, NumberStyles.Number, CultureInfo.InvariantCulture, out h) ? h : null;
			await TryAsync(() => _classes.AddInstructorAsync(DepartmentId, id, new LmsClassInstructor
			{
				UserId = string.IsNullOrWhiteSpace(userId) ? null : userId,
				Name = name,
				Qualifications = qualifications,
				Role = role,
				HoursTaught = hours
			}, UserId, cancellationToken), "Saved");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RemoveInstructor(int id, int instructorId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _classes.RemoveInstructorAsync(DepartmentId, instructorId, UserId, cancellationToken), "Saved");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> AddCost(int id, int kind, string description, string amount, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var value = decimal.TryParse(amount, NumberStyles.Number, CultureInfo.CurrentCulture, out var a) || decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out a) ? a : -1m;
			await TryAsync(() => _classes.AddCostAsync(DepartmentId, id, new LmsClassCostLine { Kind = kind, Description = description, Amount = value }, UserId, cancellationToken), "Saved");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> RemoveCost(int id, int costId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _classes.RemoveCostAsync(DepartmentId, costId, UserId, cancellationToken), "Saved");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Book(int id, int resourceId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _classes.BookResourceAsync(DepartmentId, id, resourceId, UserId, cancellationToken), "BookedMessage");
			return RedirectToAction("Session", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Unbook(int id, int bookingId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _classes.CancelBookingAsync(DepartmentId, bookingId, UserId, cancellationToken), "Saved");
			return RedirectToAction("Session", new { id });
		}

		#endregion Instructors, costs and bookings

		#region Resources and costs

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Resources(int? edit)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var now = DateTime.UtcNow;
			var view = await PageAsync(new LmsTrainingResourcesView(), "Resources");
			view.Resources = await _classes.GetResourcesAsync(DepartmentId);
			view.Editing = edit.HasValue ? view.Resources.FirstOrDefault(r => r.LmsTrainingResourceId == edit.Value) : null;
			foreach (var resource in view.Resources)
				view.Bookings[resource.LmsTrainingResourceId] = await _classes.GetBookingsAsync(DepartmentId, resource.LmsTrainingResourceId, now.AddDays(-1), now.AddDays(90));
			foreach (var id in view.Bookings.Values.SelectMany(b => b).Select(b => b.LmsClassSessionId).Distinct())
			{
				var session = await _classes.GetSessionAsync(DepartmentId, id);
				if (session != null)
					view.Sessions[id] = session;
			}
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveResource(int id, string name, int kind, int? capacity, string notes, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			if (!await CanAuthorAsync())
				Flash(null, L("AddonRequired"));
			else
				await TryAsync(() => _classes.SaveResourceAsync(new LmsTrainingResource
				{
					LmsTrainingResourceId = id,
					DepartmentId = DepartmentId,
					Name = name,
					Kind = kind,
					Capacity = capacity,
					Notes = notes
				}, UserId, cancellationToken), "Saved");
			return RedirectToAction("Resources");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteResource(int id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _classes.DeleteResourceAsync(DepartmentId, id, UserId, cancellationToken), "Deleted");
			return RedirectToAction("Resources");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> Costs(int? year, int groupBy = 0)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsClassCostsView(), "Costs");
			view.Year = year ?? ToLocal(DateTime.UtcNow).Year;
			view.GroupBy = Enum.IsDefined(typeof(LmsClassCostGroupings), groupBy) ? (LmsClassCostGroupings)groupBy : LmsClassCostGroupings.Course;
			var from = new DateTime(view.Year, 1, 1).DepartmentLocalToUtc(_department);
			var to = new DateTime(view.Year + 1, 1, 1).DepartmentLocalToUtc(_department);
			view.Rollup = await _classes.GetCostRollupAsync(DepartmentId, from, to, view.GroupBy);
			return View(view);
		}

		#endregion Resources and costs

		#region Lesson plans

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> LessonPlans()
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var view = await PageAsync(new LmsLessonPlansView(), "LessonPlans");
			view.Plans = await _lessonPlans.GetPlansAsync(DepartmentId);
			view.Starters = _lessonPlans.GetStarters(CultureInfo.CurrentUICulture.Name);
			return View(view);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> CreateLessonPlan(string title, string starter, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			if (!await CanAuthorAsync())
			{
				Flash(null, L("AddonRequired"));
				return RedirectToAction("LessonPlans");
			}

			LmsLessonPlan plan = null;
			var ok = await TryAsync(async () => plan = string.IsNullOrWhiteSpace(starter)
				? await _lessonPlans.CreateAsync(DepartmentId, title, new LmsLessonPlanContent(), UserId, cancellationToken)
				: await _lessonPlans.ApplyStarterAsync(DepartmentId, starter, UserId, CultureInfo.CurrentUICulture.Name, cancellationToken), "Created");
			return ok ? RedirectToAction("LessonPlan", new { id = plan.LmsLessonPlanId }) : RedirectToAction("LessonPlans");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> LessonPlan(int id, int? version)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var plan = await _lessonPlans.GetPlanAsync(DepartmentId, id);
			if (plan == null)
				return NotFound();
			var view = await PageAsync(new LmsLessonPlanView { Plan = plan }, "LessonPlans");
			view.Versions = await _lessonPlans.GetVersionsAsync(DepartmentId, id);
			view.Version = (version.HasValue ? view.Versions.FirstOrDefault(v => v.LmsLessonPlanVersionId == version.Value) : null)
				?? view.Versions.FirstOrDefault(v => v.Status == (int)ProtocolVersionStatuses.Draft || v.Status == (int)ProtocolVersionStatuses.InReview || v.Status == (int)ProtocolVersionStatuses.Approved)
				?? view.Versions.FirstOrDefault(v => v.LmsLessonPlanVersionId == plan.CurrentVersionId)
				?? view.Versions.FirstOrDefault();
			view.Content = view.Version?.Content() ?? new LmsLessonPlanContent();
			view.Frameworks = await _competencies.GetFrameworksAsync(DepartmentId) ?? new List<LmsCompetencyFramework>();
			foreach (var framework in view.Frameworks.Take(20))
				foreach (var item in await _competencies.GetItemsAsync(DepartmentId, framework.LmsCompetencyFrameworkId) ?? new List<LmsCompetencyItem>())
					view.CompetencyItems[item.LmsCompetencyItemId] = item;
			return View(view);
		}

		private static List<string> Lines(string text) =>
			(text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SaveLessonPlan(int id, int versionId, string title, string objectives, List<int> competencyItemIds, string materials, string props,
			int studentsPerInstructor, int durationMinutes, string safetyPlan, string preBrief, string roles, string notes, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _lessonPlans.SaveDraftAsync(DepartmentId, versionId, title, new LmsLessonPlanContent
			{
				Objectives = Lines(objectives),
				CompetencyItemIds = competencyItemIds ?? new List<int>(),
				Materials = Lines(materials),
				Props = Lines(props),
				StudentsPerInstructor = studentsPerInstructor,
				DurationMinutes = durationMinutes,
				SafetyPlan = safetyPlan,
				PreBriefChecklist = Lines(preBrief),
				Roles = Lines(roles),
				Notes = notes
			}, UserId, cancellationToken), "Saved");
			return RedirectToAction("LessonPlan", new { id, version = versionId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> SubmitLessonPlan(int id, int versionId, string changeSummary, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _lessonPlans.SubmitAsync(DepartmentId, versionId, changeSummary, UserId, cancellationToken), "SubmittedMessage");
			return RedirectToAction("LessonPlan", new { id, version = versionId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DecideLessonPlan(int id, int versionId, bool approve, string note, CancellationToken cancellationToken)
		{
			if (!(await ActorAsync()).MayManage)
				return Unauthorized();
			await TryAsync(() => _lessonPlans.DecideAsync(DepartmentId, versionId, approve, note, UserId, cancellationToken), approve ? "ApprovedMessage" : "DeclinedMessage");
			return RedirectToAction("LessonPlan", new { id, version = versionId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> PublishLessonPlan(int id, int versionId, bool major, CancellationToken cancellationToken)
		{
			if (!(await ActorAsync()).MayManage)
				return Unauthorized();
			if (!await CanAuthorAsync())
				Flash(null, L("AddonRequired"));
			else
				await TryAsync(() => _lessonPlans.PublishAsync(DepartmentId, versionId, major, UserId, cancellationToken), "PublishedMessage");
			return RedirectToAction("LessonPlan", new { id, version = versionId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> NewLessonPlanDraft(int id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			LmsLessonPlanVersion draft = null;
			await TryAsync(async () => draft = await _lessonPlans.NewDraftAsync(DepartmentId, id, UserId, cancellationToken), "Saved");
			return RedirectToAction("LessonPlan", new { id, version = draft?.LmsLessonPlanVersionId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_View)]
		public async Task<IActionResult> DeleteLessonPlan(int id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await TryAsync(() => _lessonPlans.DeleteAsync(DepartmentId, id, UserId, cancellationToken), "Deleted");
			return RedirectToAction("LessonPlans");
		}

		#endregion Lesson plans
	}

	/// <summary>The session form (department-local times).</summary>
	public sealed class LmsClassFormModel
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public string ContentSummary { get; set; }
		public int? LmsCourseId { get; set; }
		public int? TrainingId { get; set; }
		public int? LmsLessonPlanId { get; set; }
		public int? CategoryId { get; set; }
		public string CourseCode { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Location { get; set; }
		public string MeetingUrl { get; set; }
		public int Capacity { get; set; }
		public bool WaitlistEnabled { get; set; }
		public bool EnrollmentOpen { get; set; }
		public bool RequiresApproval { get; set; }
		public int CutoffDays { get; set; }
		public bool AllowSelfUnenroll { get; set; }
		public List<int> RestrictGroupIds { get; set; }
		public List<int> VisibleGroupIds { get; set; }
		public string Tags { get; set; }
		public int ReminderHours { get; set; }
		public bool QrEnabled { get; set; }
		public bool GpsRequired { get; set; }
		public decimal? GpsLatitude { get; set; }
		public decimal? GpsLongitude { get; set; }
		public int GpsRadiusMeters { get; set; }
		public int PostClassEvaluationKind { get; set; }
		public int? RetentionYears { get; set; }
		public string Currency { get; set; }
		public int Repeat { get; set; }
		public int Occurrences { get; set; } = 1;
		public List<string> InstructorUserIds { get; set; }
	}
}
