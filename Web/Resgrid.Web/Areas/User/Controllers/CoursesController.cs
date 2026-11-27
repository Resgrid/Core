using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Courses;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Courses, Academy and Course library (Training T2 and A1, protocols-training-knowledge-plan §13 T2, §15, §18.3). Every
	/// member takes the courses assigned to them and the Resgrid Academy (free, no seat, R-S2) in a responsive player (resume, playback
	/// speed, captions and transcript, minimum seat time, R-C8). Officers with Assign Training assign Academy lessons and role paths.
	/// Department courses (the LMS add-on) are dark while the Training.Lms flag is off; authoring and new assignments need the add-on
	/// (lapse rule 3). The Trainings module switch closes every page.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class CoursesController : SecureBaseController
	{
		private const long MaxLessonFileRequestBytes = 12 * 1024 * 1024;

		private readonly ILmsGateService _gate;
		private readonly ILmsCourseService _courses;
		private readonly ILmsQuestionBankService _banks;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsAssessmentService _assessments;
		private readonly ILmsMediaService _media;
		private readonly ILmsCrewViewingService _crew;
		private readonly ILmsAcademyService _academy;
		private readonly ILmsAccessService _access;
		private readonly ILmsTrainingService _lmsTraining;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _groupsService;
		private readonly IPersonnelRolesService _rolesService;
		private readonly ILearningRecordService _learningRecords;
		private readonly ICertificationService _certificationService;
		private readonly IProtocolLibraryService _protocolLibrary;
		private readonly IProtocolsService _protocolsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Courses.Courses> _localizer;

		private LmsActor _actor;
		private Department _department;
		private readonly ILmsCompletionRecordingService _recordings;
		private readonly ITrainingCompletionRecordingService _recorded;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> _trainingLocalizer;

		public CoursesController(ILmsGateService gate, ILmsCourseService courses, ILmsQuestionBankService banks, ILmsLearnerService learner, ILmsAssessmentService assessments,
			ILmsMediaService media, ILmsCrewViewingService crew, ILmsAcademyService academy, ILmsAccessService access, ILmsTrainingService lmsTraining,
			IDepartmentsService departmentsService, IDepartmentGroupsService groupsService, IPersonnelRolesService rolesService, ILearningRecordService learningRecords,
			ICertificationService certificationService, IProtocolLibraryService protocolLibrary, IProtocolsService protocolsService, IAuthorizationService authorizationService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Courses.Courses> localizer, ILmsCompletionRecordingService recordings, ITrainingCompletionRecordingService recorded,
			IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings> trainingLocalizer)
		{
			_recordings = recordings;
			_recorded = recorded;
			_trainingLocalizer = trainingLocalizer;
			_gate = gate;
			_courses = courses;
			_banks = banks;
			_learner = learner;
			_assessments = assessments;
			_media = media;
			_crew = crew;
			_academy = academy;
			_access = access;
			_lmsTraining = lmsTraining;
			_departmentsService = departmentsService;
			_groupsService = groupsService;
			_rolesService = rolesService;
			_learningRecords = learningRecords;
			_certificationService = certificationService;
			_protocolLibrary = protocolLibrary;
			_protocolsService = protocolsService;
			_authorizationService = authorizationService;
			_localizer = localizer;
		}

		#region Recorded completions (round 7B, permission 135)

		private string RecordText(string key)
		{
			var value = _trainingLocalizer?[key];
			return value == null || value.ResourceNotFound || string.IsNullOrEmpty(value.Value) ? key : value.Value;
		}

		/// <summary>Record that members completed this course's current version together (the enrolment list first); voids of earlier ones.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> RecordCompletions(int targetId)
		{
			var actor = await ActorAsync();
			if (actor?.Gate == null || !actor.Gate.FlagOn)
				return NotFound();
			var view = await _recordings.GetCourseRecordingViewAsync(DepartmentId, targetId, UserId);
			if (view == null)
				return NotFound();
			if (!view.CanRecord)
				return Unauthorized();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			return View(new Resgrid.Web.Areas.User.Models.RecordCompletionsPageView
			{
				View = view,
				Controller = "Courses",
				Names = await NamesAsync(),
				Local = d => department == null ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc).TimeConverter(department),
				Message = TempData?["CourseMessage"] as string,
				Error = TempData?["CourseError"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> RecordCompletions(int targetId, List<string> userIds, string note, bool attested, string attestationVersion, CancellationToken cancellationToken)
		{
			var outcome = await _recordings.RecordCourseAsync(DepartmentId, targetId, new TrainingCompletionRecordingRequest
			{
				RecorderUserId = UserId,
				MemberUserIds = userIds ?? new List<string>(),
				Note = note,
				Attested = attested,
				AttestationVersion = string.IsNullOrWhiteSpace(attestationVersion) ? TrainingCompletionAttestation.CurrentVersion : attestationVersion,
				Source = TrainingCompletionRecordingSources.Manual
			}, cancellationToken);
			if (TempData != null)
			{
				if (!outcome.Succeeded)
					TempData["CourseError"] = RecordText(outcome.Error);
				else
				{
					TempData["CourseMessage"] = string.Format(CultureInfo.CurrentCulture, RecordText("RecordCompletions.ResultFormat"), outcome.Recorded, outcome.Refused);
					var refusals = outcome.Results.Where(r => !r.Recorded).GroupBy(r => r.Refusal).Select(g => RecordText(g.Key) + " (" + g.Count().ToString(CultureInfo.CurrentCulture) + ")").ToList();
					if (refusals.Count > 0)
						TempData["CourseError"] = string.Join(" ", refusals);
				}
			}
			return RedirectToAction("RecordCompletions", new { targetId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		public async Task<IActionResult> VoidRecordedCompletion(int targetId, int recordingId, string reason, CancellationToken cancellationToken)
		{
			var recording = await _recorded.GetAsync(DepartmentId, recordingId);
			if (recording == null || recording.TargetKind != (int)TrainingCompletionRecordingTargets.Course || recording.TargetId != targetId)
				return NotFound();
			var result = await _recorded.VoidAsync(DepartmentId, recordingId, UserId, reason, cancellationToken);
			if (TempData != null)
			{
				if (result.Refusal == null)
					TempData["CourseMessage"] = RecordText("RecordCompletions.VoidDone");
				else
					TempData["CourseError"] = RecordText(result.Refusal);
			}
			return RedirectToAction("RecordCompletions", new { targetId });
		}

		#endregion Recorded completions (round 7B, permission 135)

		#region K2 drafting aids (accept)

		/// <summary>
		/// K2: the questions the author kept from a draft, saved to the course's drafted-questions bank (created on first use, owned by the
		/// author). Only what was accepted is saved, through the bank service's own rules (author rights, the add-on, question validation).
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(200_000)]
		public async Task<IActionResult> AcceptAidQuestions(int id, [FromForm] string questions, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			var outline = await _courses.GetOutlineAsync(DepartmentId, id);
			if (outline?.Course == null || outline.Course.IsSystem || !(await _courses.GetRightsAsync(actor, outline.Course))?.Edit == true)
				return NotFound();

			List<AuthoringAidQuestionDraft> drafts;
			try
			{
				drafts = JsonConvert.DeserializeObject<List<AuthoringAidQuestionDraft>>(questions ?? "[]") ?? new List<AuthoringAidQuestionDraft>();
			}
			catch (JsonException)
			{
				return BadRequest();
			}
			drafts = drafts.Where(d => d != null && !string.IsNullOrWhiteSpace(d.Text) && d.Options != null && d.Options.Count >= 2 && d.Options.Count <= 6 &&
				d.CorrectIndex >= 0 && d.CorrectIndex < d.Options.Count && d.Options.All(o => !string.IsNullOrWhiteSpace(o))).Take(AuthoringAidRequest.MaxQuestions).ToList();
			if (drafts.Count == 0)
				return Json(new { saved = false, message = Text("AidNothingAccepted") });

			var bankName = AidTruncate(string.Format(Text("AidBankNameFormat"), outline.Course.Title), 250);
			var bank = (await _banks.GetBanksAsync(actor) ?? new List<LmsQuestionBank>())
				.FirstOrDefault(b => string.Equals(b.Name, bankName, StringComparison.Ordinal) && string.Equals(b.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase));
			if (bank == null)
			{
				var created = await _banks.SaveBankAsync(actor, new LmsQuestionBank { Name = bankName, Description = Text("AidBankDescription") }, cancellationToken);
				if (!created.Succeeded)
					return Json(new { saved = false, message = ErrorText(created) });
				bank = created.Value as LmsQuestionBank;
			}

			var saved = 0;
			foreach (var draft in drafts)
			{
				var keys = new[] { "a", "b", "c", "d", "e", "f" };
				var result = await _banks.SaveQuestionAsync(actor, new LmsQuestion
				{
					LmsQuestionBankId = bank.LmsQuestionBankId,
					Kind = (int)LmsQuestionKinds.SingleChoice,
					Text = AidTruncate(draft.Text.Trim(), 4000),
					Explanation = AidTruncate(draft.Explanation?.Trim(), 4000),
					Points = 1,
					Options = draft.Options.Select((o, i) => new LmsQuestionOption { Key = keys[i], Text = AidTruncate(o.Trim(), 1000), IsCorrect = i == draft.CorrectIndex }).ToList()
				}, cancellationToken);
				if (!result.Succeeded)
					return Json(new { saved = saved > 0, message = ErrorText(result) });
				saved++;
			}

			return Json(new
			{
				saved = true,
				message = string.Format(Text("AidQuestionsSaved"), saved, bank.Name),
				link = Url.Action("Bank", "Courses", new { area = "User", id = bank.LmsQuestionBankId }),
				linkText = Text("AidOpenBank")
			});
		}

		/// <summary>K2: the modules the author kept from a drafted outline, added to this draft as modules with (empty) block lessons.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(100_000)]
		public async Task<IActionResult> AcceptAidOutline(int id, [FromForm] string outline, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			List<AuthoringAidOutlineModule> modules;
			try
			{
				modules = JsonConvert.DeserializeObject<List<AuthoringAidOutlineModule>>(outline ?? "[]") ?? new List<AuthoringAidOutlineModule>();
			}
			catch (JsonException)
			{
				return BadRequest();
			}
			modules = modules.Where(m => m != null && !string.IsNullOrWhiteSpace(m.Title)).Take(12).ToList();
			if (modules.Count == 0)
				return Json(new { saved = false, message = Text("AidNothingAccepted") });

			var added = 0;
			foreach (var module in modules)
			{
				var result = await _courses.SaveModuleAsync(actor, id, 0, module.Title, cancellationToken);
				if (!result.Succeeded || result.Value is not LmsCourseModule saved)
					return Json(new { saved = added > 0, reload = added > 0, message = ErrorText(result) });
				foreach (var lesson in (module.Lessons ?? new List<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).Take(10))
				{
					var lessonResult = await _courses.SaveLessonAsync(actor, new LmsLessonInput
					{
						LmsCourseVersionId = id,
						LmsCourseModuleId = saved.LmsCourseModuleId,
						Kind = (int)LmsLessonKinds.Blocks,
						Title = AidTruncate(lesson.Trim(), LmsLesson.TitleMaxLength),
						ContentHtml = string.Empty,
						IsRequired = true
					}, cancellationToken);
					if (!lessonResult.Succeeded)
						return Json(new { saved = true, reload = true, message = ErrorText(lessonResult) });
				}
				added++;
			}

			return Json(new { saved = true, reload = true, message = string.Format(Text("AidOutlineSaved"), added) });
		}

		private static string AidTruncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		#endregion K2 drafting aids (accept)

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
			{
				context.Result = NotFound();
				return;
			}

			_department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			ViewData["Scope"] = await _authorizationService.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
			ViewData["LmsLibrary"] = (await ActorAsync()).Gate.FlagOn && ((await ActorAsync()).MayManage || (await ActorAsync()).MayGrade);
			ViewData["AcademyLesson"] = "09-training";
			await next();
		}

		private async Task<LmsActor> ActorAsync() => _actor ??= await _gate.GetActorAsync(DepartmentId, UserId);

		private string Text(string key) => _localizer[key].Value;

		private string ErrorText(LmsResult result) =>
			result.Error != LmsErrors.None ? Text(CourseLabels.Error(result.Error)) : Text(CourseLabels.LifecycleError(result.LifecycleError));

		private void Flash(LmsResult result, string successKey)
		{
			if (result.Succeeded)
				TempData["CourseMessage"] = Text(successKey);
			else
				TempData["CourseError"] = ErrorText(result);
		}

		private T Page<T>(T model) where T : CoursesPageModel
		{
			model.Message ??= TempData["CourseMessage"] as string;
			model.Error ??= TempData["CourseError"] as string;
			model.Actor = _actor;
			var department = _department;
			model.Date = d => !d.HasValue ? null : department == null ? d.Value.ToShortDateString() : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc).TimeConverter(department).ToShortDateString();
			return model;
		}

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			return (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		}

		private async Task<CourseWhoLists> WhoListsAsync()
		{
			var lists = new CourseWhoLists();
			foreach (var person in await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
				lists.Members[person.UserId] = person.Name;
			foreach (var group in await _groupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
				lists.Groups[group.DepartmentGroupId] = group.Name;
			foreach (var role in await _rolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
				lists.Roles[role.PersonnelRoleId] = role.Name;
			return lists;
		}

		private async Task<DateTime?> DueUtcAsync(string localDate)
		{
			if (string.IsNullOrWhiteSpace(localDate) || !DateTime.TryParseExact(localDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
				return null;
			return TrainingDueDates.EndOfLocalDayUtc(date, _department ?? await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false));
		}

		private string AcademyTitle(LmsCourse course) =>
			course != null && course.IsSystem ? _academy.Text(AcademyCourseManifest.LessonIdFromSystemKey(course.SystemKey) + ".Title", CultureInfo.CurrentUICulture.Name) : course?.Title;

		private string Locale => CultureInfo.CurrentUICulture.Name;

		/// <summary>The assignment summary; round 10 A7: how many were not enrolled for want of an LMS seat (enable their LMS access, then assign again).</summary>
		private string AssignedText(LmsAssignOutcome outcome)
		{
			var text = string.Format(CultureInfo.CurrentCulture, Text("AssignedMessage"), outcome.Created, outcome.AlreadyEnrolled, outcome.Skipped.Count);
			return outcome.NotLicensed > 0 ? text + " " + string.Format(CultureInfo.CurrentCulture, Text("AssignedNotLicensedMessage"), outcome.NotLicensed) : text;
		}

		#endregion Gates and helpers

		#region My courses and the Academy

		[HttpGet]
		public async Task<IActionResult> My()
		{
			var actor = await ActorAsync();
			var model = Page(new MyCoursesModel
			{
				Courses = await _learner.GetMyCoursesAsync(DepartmentId, UserId),
				Catalog = await _learner.GetCatalogAsync(actor),
				TitleOf = AcademyTitle
			});
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Enroll(int courseId, CancellationToken cancellationToken = default)
		{
			var result = await _learner.EnrollSelfAsync(await ActorAsync(), courseId, cancellationToken);
			if (result.Succeeded && result.Enrollment != null)
				return RedirectToAction("Play", new { id = result.Enrollment.LmsEnrollmentId });
			Flash(result, "EnrolledMessage");
			return RedirectToAction("My");
		}

		[HttpGet]
		public async Task<IActionResult> Academy(string assign = null, string path = null)
		{
			var actor = await ActorAsync();
			var catalog = await _academy.GetCatalogAsync(actor);
			if (catalog.Error != LmsErrors.None)
				return NotFound();
			var model = Page(new AcademyModel
			{
				Catalog = catalog,
				AssignLessonId = catalog.CanAssign ? AcademyCourseManifest.Find(assign)?.LessonId : null,
				AssignPathKey = catalog.CanAssign ? AcademyCourseManifest.FindPath(path)?.Key : null,
				Who = catalog.CanAssign ? await WhoListsAsync() : null,
				Text = key => _academy.Text(key, Locale),
				AcademyHome = Resgrid.Model.Academy.AcademyCatalog.HomeUrl(Config.SystemBehaviorConfig.AcademyUrl)
			});
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AcademyAssign(string lessonId, string pathKey, CourseWhoInput who, CancellationToken cancellationToken = default)
		{
			var actor = await ActorAsync();
			var due = await DueUtcAsync(who?.DueOn);
			var rules = who?.Rules() ?? new List<ContentAudienceRule>();
			var outcome = !string.IsNullOrWhiteSpace(pathKey)
				? await _academy.AssignPathAsync(actor, pathKey, rules, due, cancellationToken)
				: await _academy.AssignLessonAsync(actor, lessonId, rules, due, cancellationToken);
			if (outcome.Error == LmsErrors.None)
				TempData["CourseMessage"] = AssignedText(outcome);
			else
				TempData["CourseError"] = Text(CourseLabels.Error(outcome.Error));
			return RedirectToAction("Academy");
		}

		[HttpGet]
		public async Task<IActionResult> AcademyPath(string id)
		{
			var actor = await ActorAsync();
			var path = AcademyCourseManifest.FindPath(id);
			if (path == null || !actor.MayAssignAcademy)
				return NotFound();
			var model = Page(new AcademyPathModel
			{
				Path = path,
				Rows = await _academy.GetPathProgressAsync(actor, path.Key),
				Names = await NamesAsync(),
				Text = key => _academy.Text(key, Locale)
			});
			return View(model);
		}

		#endregion My courses and the Academy

		#region Player

		[HttpGet]
		public async Task<IActionResult> Play(int id, int? lesson = null)
		{
			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, id, lesson);
			if (player.Error == LmsErrors.NotFound)
				return NotFound();

			var model = Page(new PlayerModel
			{
				Player = player,
				CourseTitle = player.Outline == null ? null : (player.Outline.Course.IsSystem ? AcademyTitle(player.Outline.Course) : player.Outline.Version.Title ?? player.Outline.Course.Title),
				SurveyQuestions = player.Lesson?.Kind == (int)LmsLessonKinds.Survey ? LmsSurveyQuestion.Parse(player.Lesson.SurveyJson) : new List<LmsSurveyQuestion>()
			});
			if (player.Error != LmsErrors.None)
				model.Error ??= Text(CourseLabels.Error(player.Error));
			if (player.Outline?.Course.IsSystem == true)
				ViewData["AcademyLesson"] = AcademyCourseManifest.LessonIdFromSystemKey(player.Outline.Course.SystemKey);

			if (player.HostedVideoPlayable && player.Lesson != null)
			{
				model.HlsUrl = Url.Action("Hls", new { id = player.Enrollment.LmsEnrollmentId, lesson = player.Lesson.LmsLessonId });
				model.Mp4Url = Url.Action("Mp4", new { id = player.Enrollment.LmsEnrollmentId, lesson = player.Lesson.LmsLessonId });
			}

			var actor = await ActorAsync();
			if (player.Outline != null && player.Lesson != null && player.Lesson.Kind != (int)LmsLessonKinds.Assessment && player.Lesson.Kind != (int)LmsLessonKinds.Survey)
				model.CanStartCrewViewing = player.Outline.Course.IsSystem
					? actor.MayAssignAcademy
					: (await _courses.GetRightsAsync(actor, player.Outline.Course)).Assign && actor.Gate.CanAuthor;
			// K3 "Ask this course" (the tutor needs the LMS add-on and Enhanced AI; the panel asks Knowledge/Status for the latter). Never on an
			// assessment or survey lesson, so the tutor is not open beside a test.
			model.CanAskCourse = player.Outline != null && player.Lesson != null && !player.Outline.Course.IsSystem && player.Error == LmsErrors.None &&
				actor.Gate != null && actor.Gate.FlagOn && actor.Gate.AddonActive == true &&
				player.Lesson.Kind != (int)LmsLessonKinds.Assessment && player.Lesson.Kind != (int)LmsLessonKinds.Survey;
			return View(model);
		}

		/// <summary>
		/// K3: where a knowledge citation of a course lesson opens: the member's own enrollment of the course (open first, else the latest) at
		/// that lesson when it belongs to the version they take, else where they left off; the course catalog when they are not enrolled. The
		/// citation's block anchor rides on the redirect as the URL fragment.
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> Open(int id, int? lesson = null)
		{
			var mine = (await _learner.GetMyCoursesAsync(DepartmentId, UserId) ?? new List<LmsMyCourse>())
				.Where(c => c.Course != null && c.Enrollment != null && c.Course.LmsCourseId == id && c.Enrollment.Status != (int)LmsEnrollmentStatuses.Withdrawn)
				.OrderByDescending(c => c.Enrollment.IsOpen).ThenByDescending(c => c.Enrollment.LastActivityOn ?? c.Enrollment.CompletedOn ?? c.Enrollment.AssignedOn).FirstOrDefault();
			if (mine == null)
				return RedirectToAction("My");

			int? target = null;
			if (lesson.HasValue)
			{
				var outline = await _courses.GetOutlineAsync(DepartmentId, mine.Enrollment.LmsCourseVersionId);
				if (outline != null && outline.Lessons.Any(l => l.LmsLessonId == lesson.Value))
					target = lesson.Value;
			}
			return RedirectToAction("Play", new { id = mine.Enrollment.LmsEnrollmentId, lesson = target });
		}

		/// <summary>Progress on every lesson change and heartbeat (JSON). Seat time is capped by the server clock.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Progress(int id, int lesson, int seconds, int? position, int? watched, CancellationToken cancellationToken = default)
		{
			var result = await _learner.SaveProgressAsync(DepartmentId, UserId, id, lesson,
				new LmsProgressInput { Seconds = seconds, VideoPositionSeconds = position, WatchedPercent = watched }, cancellationToken);
			var progress = result.Value as LmsLessonProgress;
			return Json(new { success = result.Succeeded, error = result.Succeeded ? null : result.Error.ToString(), seat = progress?.SeatSeconds ?? 0, watched = progress?.WatchedPercent ?? 0 });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(MaxLessonFileRequestBytes)]
		public async Task<IActionResult> Complete(int id, int lesson, string responseText, IFormFile file, CancellationToken cancellationToken = default)
		{
			var input = new LmsLessonCompletionInput { ResponseText = responseText };
			if (file != null && file.Length > 0)
			{
				if (file.Length > LmsLessonFile.MaxBytes)
				{
					TempData["CourseError"] = Text(CourseLabels.Error(LmsErrors.FileTooLarge));
					return RedirectToAction("Play", new { id, lesson });
				}
				using var buffer = new MemoryStream();
				await file.CopyToAsync(buffer, cancellationToken);
				input.FileData = buffer.ToArray();
				input.FileName = file.FileName;
				input.ContentType = file.ContentType;
			}

			var result = await _learner.CompleteLessonAsync(DepartmentId, UserId, id, lesson, input, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["CourseError"] = ErrorText(result);
				return RedirectToAction("Play", new { id, lesson });
			}
			TempData["CourseMessage"] = Text(result.Count > 0 ? "CourseCompletedMessage" : "LessonCompletedMessage");
			return RedirectToAction("Play", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Survey(int id, int lesson, CancellationToken cancellationToken = default)
		{
			var answers = Request.Form.Keys.Where(k => k.StartsWith("q_", StringComparison.Ordinal))
				.ToDictionary(k => k.Substring(2), k => Request.Form[k].ToString(), StringComparer.Ordinal);
			var result = await _learner.SubmitSurveyAsync(DepartmentId, UserId, id, lesson, answers, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["CourseError"] = ErrorText(result);
				return RedirectToAction("Play", new { id, lesson });
			}
			TempData["CourseMessage"] = Text(result.Count > 0 ? "CourseCompletedMessage" : "SurveyThanksMessage");
			return RedirectToAction("Play", new { id });
		}

		/// <summary>The hosted video's playlist with signed segment URLs, for a member who may run this lesson now.</summary>
		[HttpGet]
		public async Task<IActionResult> Hls(int id, int lesson)
		{
			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, id, lesson);
			if (player.Error != LmsErrors.None || player.Lesson?.LmsLessonId != lesson || !player.HostedVideoPlayable || player.Media == null)
				return NotFound();
			var playlist = await _media.GetSignedPlaylistAsync((await _gate.GetGateAsync(DepartmentId)).For(player.Enrollment?.LmsCourseId ?? 0), player.Media, DateTime.UtcNow);
			if (playlist == null)
				return NotFound();
			Response.Headers["Cache-Control"] = "no-store";
			return Content(playlist, "application/vnd.apple.mpegurl");
		}

		[HttpGet]
		public async Task<IActionResult> Mp4(int id, int lesson)
		{
			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, id, lesson);
			if (player.Error != LmsErrors.None || player.Lesson?.LmsLessonId != lesson || !player.HostedVideoPlayable || player.Media == null)
				return NotFound();
			var url = await _media.GetSignedMp4UrlAsync((await _gate.GetGateAsync(DepartmentId)).For(player.Enrollment?.LmsCourseId ?? 0), player.Media, DateTime.UtcNow);
			return url == null ? NotFound() : Redirect(url);
		}

		/// <summary>An uploaded WebVTT track (same origin, so the video element's track loads without CORS).</summary>
		[HttpGet]
		public async Task<IActionResult> Captions(int id, int lesson, string locale)
		{
			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, id, lesson);
			if (player.Error != LmsErrors.None || player.Lesson?.LmsLessonId != lesson)
				return NotFound();
			var text = await _media.GetCaptionsAsync(DepartmentId, player.Lesson, locale);
			return text == null ? NotFound() : Content(text, "text/vtt");
		}

		[HttpGet]
		public async Task<IActionResult> LessonFile(int id)
		{
			var file = await _learner.GetLessonFileAsync(await ActorAsync(), id);
			if (file == null)
				return NotFound();
			return File(file.Data, string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, file.FileName ?? "file");
		}

		#endregion Player

		#region Assessments

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> StartAttempt(int id, int lesson, bool attested = false, CancellationToken cancellationToken = default)
		{
			var view = await _assessments.StartAttemptAsync(DepartmentId, UserId, id, lesson, attested, IpAddressHelper.GetRequestIP(Request, true),
				Request.Headers["User-Agent"].ToString(), Locale, cancellationToken);
			if (view.Attempt == null || (view.Error != LmsErrors.None && view.Error != LmsErrors.TimeExpired))
			{
				TempData["CourseError"] = Text(CourseLabels.Error(view.Error == LmsErrors.None ? LmsErrors.InvalidInput : view.Error));
				return RedirectToAction("Play", new { id, lesson });
			}
			return RedirectToAction("Attempt", new { id = view.Attempt.LmsAssessmentAttemptId });
		}

		[HttpGet]
		public async Task<IActionResult> Attempt(int id)
		{
			var view = await _assessments.GetAttemptAsync(DepartmentId, UserId, id);
			if (view.Error == LmsErrors.NotFound)
				return NotFound();
			if (view.Error == LmsErrors.AlreadySubmitted)
				return RedirectToAction("Result", new { id });
			// Round 10 A7: without an LMS seat an open attempt is not shown; the player says why (read-only enrollment).
			if (view.Error == LmsErrors.NotLicensed)
			{
				TempData["CourseError"] = Text(CourseLabels.Error(view.Error));
				return RedirectToAction("Play", new { id = view.Attempt.LmsEnrollmentId });
			}
			return View(Page(new AttemptModel { View = view, EnrollmentId = view.Attempt.LmsEnrollmentId }));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SubmitAttempt(int id, CancellationToken cancellationToken = default)
		{
			var answers = new List<LmsAttemptAnswer>();
			foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("a_", StringComparison.Ordinal)))
			{
				var parts = key.Split('_');
				if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var questionId))
					continue;
				var answer = answers.FirstOrDefault(a => a.QuestionId == questionId);
				if (answer == null)
					answers.Add(answer = new LmsAttemptAnswer { QuestionId = questionId });
				if (parts.Length == 3 && parts[2] == "text")
					answer.Text = Request.Form[key].ToString();
				else if (parts.Length == 3 && parts[2] == "order")
					answer.Keys = Request.Form[key].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList();
				else
					answer.Keys.AddRange(Request.Form[key].Where(v => !string.IsNullOrWhiteSpace(v)));
			}

			var result = await _assessments.SubmitAttemptAsync(DepartmentId, UserId, id, answers, cancellationToken);
			if (result.Error == LmsErrors.NotFound)
				return NotFound();
			// Round 10 A7: nothing was scored without an LMS seat; back to the (read-only) player with the reason.
			if (result.Error == LmsErrors.NotLicensed && result.Attempt != null)
			{
				TempData["CourseError"] = Text(CourseLabels.Error(result.Error));
				return RedirectToAction("Play", new { id = result.Attempt.LmsEnrollmentId });
			}
			if (result.Error != LmsErrors.None && result.Error != LmsErrors.AlreadySubmitted)
				TempData["CourseError"] = Text(CourseLabels.Error(result.Error));
			else if (result.CourseCompleted)
				TempData["CourseMessage"] = Text("CourseCompletedMessage");
			return RedirectToAction("Result", new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Result(int id)
		{
			var result = await _assessments.GetResultAsync(await ActorAsync(), id);
			if (result.Error == LmsErrors.NotFound || result.Error == LmsErrors.NotAuthorized)
				return NotFound();
			var names = string.Equals(result.Attempt.UserId, UserId, StringComparison.OrdinalIgnoreCase) ? null : await NamesAsync();
			return View(Page(new AttemptResultModel
			{
				Result = result,
				MemberName = names != null && names.TryGetValue(result.Attempt.UserId, out var name) ? name : null
			}));
		}

		[HttpGet]
		public async Task<IActionResult> Grading()
		{
			var actor = await ActorAsync();
			if (!actor.MayGrade || !actor.Gate.FlagOn)
				return NotFound();
			return View(Page(new GradingModel { Items = await _assessments.GetGradingQueueAsync(actor), Names = await NamesAsync() }));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Grade(int id, string notes, CancellationToken cancellationToken = default)
		{
			var grades = Request.Form.Keys.Where(k => k.StartsWith("g_", StringComparison.Ordinal))
				.Select(k => (Key: k, Ok: int.TryParse(k.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out var q), Question: q,
					Value: decimal.TryParse(Request.Form[k], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : -1))
				.Where(x => x.Ok && x.Value >= 0)
				.Select(x => new LmsManualGrade { QuestionId = x.Question, Awarded = x.Value }).ToList();
			var result = await _assessments.GradeAsync(await ActorAsync(), id, grades, notes, cancellationToken);
			if (result.Error == LmsErrors.None)
				TempData["CourseMessage"] = Text("GradedMessage");
			else
				TempData["CourseError"] = Text(CourseLabels.Error(result.Error));
			return RedirectToAction("Grading");
		}

		[HttpGet]
		public async Task<IActionResult> ItemAnalysis(int id)
		{
			var report = await _assessments.GetItemAnalysisAsync(await ActorAsync(), id);
			return report == null ? NotFound() : View(Page(new ItemAnalysisModel { Report = report }));
		}

		[HttpGet]
		public async Task<IActionResult> PrintTest(int id, bool key = false)
		{
			var test = await _assessments.GetPrintableTestAsync(await ActorAsync(), id, key);
			if (test == null)
				return NotFound();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			return View(new PrintTestModel { Test = test, DepartmentName = department?.Name });
		}

		[HttpGet]
		public async Task<IActionResult> SurveyResults(int id)
		{
			var report = await _learner.GetSurveyReportAsync(await ActorAsync(), id);
			return report == null ? NotFound() : View(Page(new SurveyResultsModel { Report = report }));
		}

		#endregion Assessments

		#region Crew viewing

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> StartCrewViewing(int courseId, int lesson, CancellationToken cancellationToken = default)
		{
			var result = await _crew.StartAsync(await ActorAsync(), courseId, lesson, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["CourseError"] = ErrorText(result);
				return RedirectToAction("My");
			}
			return RedirectToAction("CrewViewing", new { id = ((LmsCrewViewing)result.Value).LmsCrewViewingId });
		}

		[HttpGet]
		public async Task<IActionResult> CrewViewing(int id)
		{
			var viewing = await _crew.GetAsync(DepartmentId, id);
			var actor = await ActorAsync();
			if (viewing == null || (!string.Equals(viewing.StartedByUserId, UserId, StringComparison.OrdinalIgnoreCase) && !actor.MayManage && !actor.MayAssignAcademy))
				return NotFound();
			var outline = await _courses.GetOutlineAsync(DepartmentId, viewing.LmsCourseVersionId);
			var lesson = outline?.Lessons.FirstOrDefault(l => l.LmsLessonId == viewing.LmsLessonId);
			var code = _crew.CurrentCode(viewing, DateTime.UtcNow);
			var scanUrl = code == null ? null : GetBaseUrl() + Url.Action("CrewScan", new { id = viewing.LmsCrewViewingId, c = code });
			return View(Page(new CrewViewingModel
			{
				Viewing = viewing,
				CourseTitle = outline == null ? null : (outline.Course.IsSystem ? AcademyTitle(outline.Course) : outline.Version.Title),
				LessonTitle = lesson?.Title,
				Attendees = await _crew.GetAttendeesAsync(DepartmentId, id),
				Names = await NamesAsync(),
				Open = code != null,
				ScanUrl = scanUrl,
				QrSvg = scanUrl == null ? null : WebQrCode.DataUrl(scanUrl)
			}));
		}

		/// <summary>The shared screen polls this for the rotating code (JSON): the QR image, the attendee count.</summary>
		[HttpGet]
		public async Task<IActionResult> CrewCode(int id)
		{
			var viewing = await _crew.GetAsync(DepartmentId, id);
			var actor = await ActorAsync();
			if (viewing == null || (!string.Equals(viewing.StartedByUserId, UserId, StringComparison.OrdinalIgnoreCase) && !actor.MayManage && !actor.MayAssignAcademy))
				return NotFound();
			var code = _crew.CurrentCode(viewing, DateTime.UtcNow);
			var scanUrl = code == null ? null : GetBaseUrl() + Url.Action("CrewScan", new { id = viewing.LmsCrewViewingId, c = code });
			var attendees = await _crew.GetAttendeesAsync(DepartmentId, id);
			var names = await NamesAsync();
			Response.Headers["Cache-Control"] = "no-store";
			return Json(new
			{
				open = code != null,
				qr = scanUrl == null ? null : WebQrCode.DataUrl(scanUrl),
				url = scanUrl,
				attendees = attendees.Select(a => names.TryGetValue(a.UserId, out var n) ? n : a.UserId).ToList()
			});
		}

		/// <summary>The page a member's phone opens from the QR: one button marks them present (a GET never changes anything).</summary>
		[HttpGet]
		public async Task<IActionResult> CrewScan(int id, string c)
		{
			var viewing = await _crew.GetAsync(DepartmentId, id);
			if (viewing == null)
				return NotFound();
			var outline = await _courses.GetOutlineAsync(DepartmentId, viewing.LmsCourseVersionId);
			return View(Page(new CrewScanModel
			{
				ViewingId = id,
				Code = c,
				LessonTitle = outline?.Lessons.FirstOrDefault(l => l.LmsLessonId == viewing.LmsLessonId)?.Title,
				Open = _crew.CurrentCode(viewing, DateTime.UtcNow) != null
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[ActionName("CrewScan")]
		public async Task<IActionResult> CrewScanPost(int id, string c, CancellationToken cancellationToken = default)
		{
			var result = await _crew.ScanAsync(DepartmentId, UserId, id, c, IpAddressHelper.GetRequestIP(Request, true), cancellationToken);
			if (result.Enrollment != null && (result.Succeeded || result.Error == LmsErrors.RequirementNotMet))
			{
				TempData["CourseMessage"] = Text("PresentMessage");
				return RedirectToAction("Play", new { id = result.Enrollment.LmsEnrollmentId });
			}
			TempData["CourseError"] = ErrorText(result);
			return RedirectToAction("CrewScan", new { id, c });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EndCrewViewing(int id, CancellationToken cancellationToken = default)
		{
			var result = await _crew.EndAsync(await ActorAsync(), id, cancellationToken);
			Flash(result, "CrewEndedMessage");
			return RedirectToAction("CrewViewing", new { id });
		}

		#endregion Crew viewing

		#region Course library (authoring)

		private async Task<IActionResult> LibraryGateAsync()
		{
			var actor = await ActorAsync();
			// Dark while the flag is off; otherwise managers and course owners reach their courses even when the add-on lapsed
			// (rule 2: records stay readable; rule 3: authoring and new assignments are refused by the services).
			return actor.Gate.FlagOn && actor.IsCurrentMember ? null : NotFound();
		}

		[HttpGet]
		public async Task<IActionResult> Library()
		{
			var denied = await LibraryGateAsync();
			if (denied != null)
				return denied;
			var actor = await ActorAsync();
			var rows = await _courses.GetCourseRowsAsync(actor);
			if (rows.Count == 0 && !actor.MayManage && !actor.MayGrade)
				return NotFound();
			return View(Page(new CourseLibraryModel
			{
				Rows = rows,
				Reviews = await _courses.GetReviewQueueAsync(actor),
				PendingGrades = actor.MayGrade ? (await _assessments.GetGradingQueueAsync(actor)).Count : 0,
				CanAuthor = actor.Gate.CanAuthor && actor.MayManage,
				IsLapsed = actor.Gate.IsLapsed
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Create(string title, string code, string summary, CancellationToken cancellationToken = default)
		{
			var denied = await LibraryGateAsync();
			if (denied != null)
				return denied;
			var result = await _courses.CreateAsync(await ActorAsync(), new LmsCourseInput { Title = title, Code = code, Summary = summary }, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["CourseError"] = ErrorText(result);
				return RedirectToAction("Library");
			}
			return RedirectToAction("Builder", new { id = result.Version.LmsCourseVersionId });
		}

		[HttpGet]
		public async Task<IActionResult> Course(int id)
		{
			var denied = await LibraryGateAsync();
			if (denied != null)
				return denied;
			var actor = await ActorAsync();
			var course = await _courses.GetCourseAsync(DepartmentId, id);
			if (course == null || course.IsSystem || course.IsDeleted)
				return NotFound();
			var rights = await _courses.GetRightsAsync(actor, course);
			if (!rights.Any)
				return NotFound();
			var model = Page(new CourseDetailModel
			{
				Course = course,
				Rights = rights,
				Versions = await _courses.GetVersionsAsync(DepartmentId, id),
				Current = course.CurrentVersionId.HasValue ? await _courses.GetOutlineAsync(DepartmentId, course.CurrentVersionId.Value) : null,
				Enrollments = await _learner.GetCourseEnrollmentsAsync(actor, id),
				Owners = await _courses.GetOwnersAsync(DepartmentId, id),
				Audience = await _courses.GetAudienceAsync(DepartmentId, id),
				Names = await NamesAsync(),
				Who = rights.Assign ? await WhoListsAsync() : null,
				CanAssignNew = rights.Assign && LmsLapseRules.CanAssignNew(actor.Gate.For(course.LmsCourseId), false)
			});
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveCourse(int id, string title, string code, string summary, string tags, bool selfEnroll, CourseWhoInput audience,
			CancellationToken cancellationToken = default)
		{
			var result = await _courses.UpdateCourseAsync(await ActorAsync(), id, new LmsCourseInput
			{
				Title = title, Code = code, Summary = summary, Tags = tags, IsSelfEnrollable = selfEnroll,
				Audience = audience?.Everyone == true ? new List<ContentAudienceRule>() : audience?.Rules() ?? new List<ContentAudienceRule>()
			}, cancellationToken);
			Flash(result, "SavedMessage");
			return RedirectToAction("Course", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Assign(int id, CourseWhoInput who, CancellationToken cancellationToken = default)
		{
			var outcome = await _learner.AssignDirectAsync(await ActorAsync(), id, who?.Rules() ?? new List<ContentAudienceRule>(), await DueUtcAsync(who?.DueOn),
				cancellationToken);
			if (outcome.Error == LmsErrors.None)
				TempData["CourseMessage"] = AssignedText(outcome);
			else
				TempData["CourseError"] = Text(CourseLabels.Error(outcome.Error));
			return RedirectToAction("Course", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> WithdrawEnrollment(int id, int enrollmentId, CancellationToken cancellationToken = default)
		{
			Flash(await _learner.WithdrawEnrollmentAsync(await ActorAsync(), enrollmentId, cancellationToken), "WithdrawnMessage");
			return RedirectToAction("Course", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveOwner(int id, string userId, bool canEdit, bool canAssign, bool canReport, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.SaveOwnerAsync(await ActorAsync(), id, userId, canEdit, canAssign, canReport, cancellationToken), "SavedMessage");
			return RedirectToAction("Course", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveOwner(int id, int ownerId, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.RemoveOwnerAsync(await ActorAsync(), id, ownerId, cancellationToken), "SavedMessage");
			return RedirectToAction("Course", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> StartDraft(int id, CancellationToken cancellationToken = default)
		{
			var result = await _courses.StartDraftAsync(await ActorAsync(), id, cancellationToken);
			if (result.Version != null && (result.Succeeded || result.LifecycleError == ProtocolLifecycleErrors.DraftExists))
				return RedirectToAction("Builder", new { id = result.Version.LmsCourseVersionId });
			TempData["CourseError"] = ErrorText(result);
			return RedirectToAction("Course", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Retire(int id, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.RetireAsync(await ActorAsync(), id, cancellationToken), "RetiredMessage");
			return RedirectToAction("Course", new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Builder(int id, int? lesson = null, int? module = null, int kind = 0)
		{
			var denied = await LibraryGateAsync();
			if (denied != null)
				return denied;
			var actor = await ActorAsync();
			var outline = await _courses.GetOutlineAsync(DepartmentId, id);
			if (outline == null || outline.Course.IsSystem || outline.Version.DepartmentId != DepartmentId)
				return NotFound();
			var rights = await _courses.GetRightsAsync(actor, outline.Course);
			if (!rights.Any)
				return NotFound();

			var model = Page(new CourseBuilderModel
			{
				Outline = outline,
				Rights = rights,
				Lesson = lesson.HasValue ? outline.Lessons.FirstOrDefault(l => l.LmsLessonId == lesson.Value) : null,
				ModuleForNewLesson = !lesson.HasValue && module.HasValue && outline.Modules.Any(m => m.Module.LmsCourseModuleId == module.Value) ? module : null,
				NewLessonKind = Enum.IsDefined(typeof(LmsLessonKinds), kind) ? kind : 0,
				Editable = rights.Edit && actor.Gate.CanAuthor && ProtocolLifecycle.IsContentEditable(outline.Version.Status),
				Approvals = await _courses.GetApprovalsAsync(DepartmentId, id),
				Signatures = await _courses.GetApprovalSignaturesAsync(DepartmentId, id),
				MediaConfigured = _media.IsStoreConfigured,
				CanUploadMedia = _media.IsStoreConfigured && LmsLapseRules.CanUploadMedia(actor.Gate),
				Names = await NamesAsync()
			});

			if (model.Editable || model.Lesson != null)
			{
				foreach (var category in await _learningRecords.GetCategoriesAsync(DepartmentId) ?? new List<TrainingCategory>())
					model.Categories[category.TrainingCategoryId] = category.Name;
				foreach (var type in await _certificationService.GetActiveCertificationTypesAsync(DepartmentId, CertificationAppliesTo.Person) ?? new List<DepartmentCertificationType>())
					model.CertificationTypes[type.DepartmentCertificationTypeId] = type.Type;
				foreach (var template in await _lmsTraining.GetCertificateTemplatesAsync(DepartmentId) ?? new List<LmsCertificateTemplate>())
					model.CertificateTemplates[template.LmsCertificateTemplateId] = template.Name;
				foreach (var bank in await _banks.GetBanksAsync(actor) ?? new List<LmsQuestionBank>())
					model.Banks[bank.LmsQuestionBankId] = bank.Name;
				foreach (var asset in (await _media.GetAssetsAsync(DepartmentId)).Where(a => a.Kind == (int)LmsMediaKinds.Video))
					model.Media[asset.LmsMediaAssetId] = asset.FileName + " (" + Text(CourseLabels.MediaStatus(asset.Status)) + ")";
				await AddProtocolVersionsAsync(model);
			}
			return View(model);
		}

		private async Task AddProtocolVersionsAsync(CourseBuilderModel model)
		{
			try
			{
				var protocols = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId) ?? new List<DispatchProtocol>())
					.Where(p => !p.IsDeleted && p.CurrentVersionId.HasValue).ToDictionary(p => p.CurrentVersionId.Value);
				foreach (var version in (await _protocolLibrary.GetDepartmentVersionHeadersAsync(DepartmentId) ?? new List<ProtocolVersion>())
					.Where(v => v.Status == (int)ProtocolVersionStatuses.Published && protocols.ContainsKey(v.ProtocolVersionId)))
					model.ProtocolVersions[version.ProtocolVersionId] = protocols[version.ProtocolVersionId].Name + " v" + version.Label;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protocol versions could not be listed for the course builder.");
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveSettings(int id, string title, string overviewHtml, decimal hours, int? categoryId, string ceProvider, string ceCourseNumber,
			int? certificateTemplateId, int? issuesCertificationTypeId, long rowVersion, CancellationToken cancellationToken = default)
		{
			var result = await _courses.SaveVersionSettingsAsync(await ActorAsync(), id, new LmsVersionSettingsInput
			{
				Title = title, OverviewHtml = overviewHtml, Hours = hours, CategoryId = categoryId, CeProvider = ceProvider, CeCourseNumber = ceCourseNumber,
				CertificateTemplateId = certificateTemplateId, IssuesCertificationTypeId = issuesCertificationTypeId, ExpectedRowVersion = rowVersion
			}, cancellationToken);
			Flash(result, "SavedMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveModule(int id, int moduleId, string title, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.SaveModuleAsync(await ActorAsync(), id, moduleId, title, cancellationToken), "SavedMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteModule(int id, int moduleId, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.DeleteModuleAsync(await ActorAsync(), moduleId, cancellationToken), "DeletedMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveLesson(int id, LmsLessonInput input, string surveyText, long? offlineMaxMb, CancellationToken cancellationToken = default)
		{
			input ??= new LmsLessonInput();
			input.LmsCourseVersionId = id;
			input.OfflineMaxBytes = offlineMaxMb > 0 ? Math.Min(offlineMaxMb.Value, 4096) * 1024 * 1024 : (long?)null;
			if (input.Kind == (int)LmsLessonKinds.Survey)
				input.SurveyQuestions = ParseSurvey(surveyText);
			var result = await _courses.SaveLessonAsync(await ActorAsync(), input, cancellationToken);
			Flash(result, "SavedMessage");
			var lesson = result.Value as LmsLesson;
			return lesson == null
				? RedirectToAction("Builder", new { id, module = input.LmsCourseModuleId, kind = input.Kind })
				: RedirectToAction("Builder", new { id, lesson = lesson.LmsLessonId });
		}

		/// <summary>One survey question per line: "rating: text", "text: text" or "choice: text | option | option".</summary>
		internal static List<LmsSurveyQuestion> ParseSurvey(string text)
		{
			var questions = new List<LmsSurveyQuestion>();
			foreach (var raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
			{
				var line = raw.Trim();
				var colon = line.IndexOf(':');
				if (colon <= 0)
					continue;
				var kind = line.Substring(0, colon).Trim().ToLowerInvariant();
				var body = line.Substring(colon + 1).Trim();
				if (body.Length == 0)
					continue;
				if (kind == "choice")
				{
					var parts = body.Split('|').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
					if (parts.Count >= 3)
						questions.Add(new LmsSurveyQuestion { Kind = (int)LmsSurveyQuestionKinds.Choice, Text = parts[0], Options = parts.Skip(1).ToList() });
				}
				else if (kind == "rating")
					questions.Add(new LmsSurveyQuestion { Kind = (int)LmsSurveyQuestionKinds.Rating, Text = body });
				else if (kind == "text")
					questions.Add(new LmsSurveyQuestion { Kind = (int)LmsSurveyQuestionKinds.Text, Text = body });
			}
			return questions;
		}

		/// <summary>The survey questions as the builder's text area shows them (the format <see cref="ParseSurvey"/> reads).</summary>
		public static string SurveyText(string surveyJson) => string.Join("\n", LmsSurveyQuestion.Parse(surveyJson).Select(q =>
			q.Kind == (int)LmsSurveyQuestionKinds.Choice ? "choice: " + q.Text + " | " + string.Join(" | ", q.Options)
			: q.Kind == (int)LmsSurveyQuestionKinds.Rating ? "rating: " + q.Text : "text: " + q.Text));

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteLesson(int id, int lessonId, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.DeleteLessonAsync(await ActorAsync(), lessonId, cancellationToken), "DeletedMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Move(int id, bool isModule, int itemId, bool up, CancellationToken cancellationToken = default)
		{
			var result = await _courses.MoveAsync(await ActorAsync(), id, isModule, itemId, up, cancellationToken);
			if (!result.Succeeded)
				TempData["CourseError"] = ErrorText(result);
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Submit(int id, string changeSummary, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.SubmitAsync(await ActorAsync(), id, changeSummary, cancellationToken), "SubmittedMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.WithdrawAsync(await ActorAsync(), id, cancellationToken), "WithdrawnDraftMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Discard(int id, CancellationToken cancellationToken = default)
		{
			var result = await _courses.DiscardDraftAsync(await ActorAsync(), id, cancellationToken);
			Flash(result, "DiscardedMessage");
			return result.Succeeded && result.Course != null && !result.Course.IsDeleted
				? RedirectToAction("Course", new { id = result.Course.LmsCourseId })
				: RedirectToAction(result.Succeeded ? "Library" : "Builder", result.Succeeded ? null : new { id });
		}

		/// <summary>Approves with an L2 signature: this session's fresh step-up (RequiresRecentTwoFactor) bound to the version's hash.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Resgrid.Web.Attributes.RequiresRecentTwoFactor(RequireForOperation = true, StepUpArea = Resgrid.Model.Security.StepUpArea.Signatures)]
		public async Task<IActionResult> Approve(int id, string comment, CancellationToken cancellationToken = default)
		{
			var proof = SignatureProof.SessionOnly(UserId, IpAddressHelper.GetRequestIP(Request, true), Request?.Headers["User-Agent"].ToString());
			// Set by RequiresRecentTwoFactor(RequireForOperation = true) from this session's server-side evidence.
			Resgrid.Web.Helpers.WebStepUpProof.Apply(proof, HttpContext);
			Flash(await _courses.ApproveAsync(await ActorAsync(), id, comment, proof, cancellationToken), "ApprovedMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Reject(int id, string comment, CancellationToken cancellationToken = default)
		{
			Flash(await _courses.RejectAsync(await ActorAsync(), id, comment, cancellationToken), "RejectedMessage");
			return RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Publish(int id, bool major, string changeSummary, int policy, int? reassignDays, CancellationToken cancellationToken = default)
		{
			var result = await _courses.PublishAsync(await ActorAsync(), id, major, changeSummary, (LmsMajorChangePolicies)policy, reassignDays, cancellationToken);
			Flash(result, "PublishedMessage");
			return result.Succeeded ? RedirectToAction("Course", new { id = result.Course.LmsCourseId }) : RedirectToAction("Builder", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(2L * 1024 * 1024 * 1024 + 1024 * 1024)]
		[RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024 + 1024 * 1024)]
		public async Task<IActionResult> UploadVideo(int? id, IFormFile file, CancellationToken cancellationToken = default)
		{
			LmsResult result;
			if (file == null || file.Length == 0)
				result = LmsResult.Fail(LmsErrors.InvalidInput);
			else
			{
				using var stream = file.OpenReadStream();
				result = await _media.UploadVideoAsync(await ActorAsync(), file.FileName, file.ContentType, stream, file.Length, cancellationToken);
			}
			Flash(result, "UploadedMessage");
			return id.HasValue ? RedirectToAction("Builder", new { id = id.Value }) : RedirectToAction("Storage");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UploadCaptions(int id, int lessonId, string locale, string label, IFormFile file, CancellationToken cancellationToken = default)
		{
			LmsResult result;
			if (file == null || file.Length == 0 || file.Length > Config.LmsConfig.MaxCaptionBytes)
				result = LmsResult.Fail(file == null || file.Length == 0 ? LmsErrors.InvalidInput : LmsErrors.FileTooLarge);
			else
			{
				using var buffer = new MemoryStream();
				await file.CopyToAsync(buffer, cancellationToken);
				result = await _media.UploadCaptionsAsync(await ActorAsync(), lessonId, locale, label, buffer.ToArray(), cancellationToken);
			}
			Flash(result, "UploadedMessage");
			return RedirectToAction("Builder", new { id, lesson = lessonId });
		}

		[HttpGet]
		public async Task<IActionResult> Storage()
		{
			var denied = await LibraryGateAsync();
			if (denied != null)
				return denied;
			var actor = await ActorAsync();
			if (!actor.MayManage)
				return NotFound();
			return View(Page(new StorageModel
			{
				Report = await _media.GetStorageReportAsync(DepartmentId),
				Assets = await _media.GetAssetsAsync(DepartmentId),
				MediaConfigured = _media.IsStoreConfigured,
				CanUpload = _media.IsStoreConfigured && LmsLapseRules.CanUploadMedia(actor.Gate)
			}));
		}

		#endregion Course library (authoring)

		#region Question banks

		[HttpGet]
		public async Task<IActionResult> Banks()
		{
			var denied = await LibraryGateAsync();
			if (denied != null)
				return denied;
			var actor = await ActorAsync();
			return View(Page(new BanksModel { Banks = await _banks.GetBanksAsync(actor), CanAuthor = actor.Gate.CanAuthor }));
		}

		[HttpGet]
		public async Task<IActionResult> Bank(int id, int? question = null)
		{
			var denied = await LibraryGateAsync();
			if (denied != null)
				return denied;
			var actor = await ActorAsync();
			var bank = await _banks.GetBankAsync(actor, id);
			if (bank == null)
				return NotFound();
			var questions = await _banks.GetQuestionsAsync(actor, id);
			return View(Page(new BankModel
			{
				Bank = bank,
				Questions = questions,
				Editing = question.HasValue ? questions.FirstOrDefault(q => q.LmsQuestionId == question.Value) : null,
				CanAuthor = actor.Gate.CanAuthor
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveBank(int id, string name, string description, bool isShared, string tags, CancellationToken cancellationToken = default)
		{
			var result = await _banks.SaveBankAsync(await ActorAsync(), new LmsQuestionBank { LmsQuestionBankId = id, Name = name, Description = description, IsShared = isShared, Tags = tags },
				cancellationToken);
			Flash(result, "SavedMessage");
			return result.Value is LmsQuestionBank bank ? RedirectToAction("Bank", new { id = bank.LmsQuestionBankId }) : RedirectToAction("Banks");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteBank(int id, CancellationToken cancellationToken = default)
		{
			Flash(await _banks.DeleteBankAsync(await ActorAsync(), id, cancellationToken), "DeletedMessage");
			return RedirectToAction("Banks");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveQuestion(int id, int questionId, int kind, string text, string imageUrl, string explanation, string tags, decimal points,
			List<string> optionText, List<int> correct, CancellationToken cancellationToken = default)
		{
			var correctSet = new HashSet<int>(correct ?? new List<int>());
			var options = (optionText ?? new List<string>()).Select((t, i) => new LmsQuestionOption { Text = t, IsCorrect = correctSet.Contains(i) })
				.Where(o => !string.IsNullOrWhiteSpace(o.Text)).ToList();
			var result = await _banks.SaveQuestionAsync(await ActorAsync(), new LmsQuestion
			{
				LmsQuestionId = questionId,
				LmsQuestionBankId = id,
				Kind = kind,
				Text = text,
				ImageUrl = imageUrl,
				Explanation = explanation,
				Tags = tags,
				Points = points,
				Options = options
			}, cancellationToken);
			Flash(result, "SavedMessage");
			return RedirectToAction("Bank", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RetireQuestion(int id, int questionId, CancellationToken cancellationToken = default)
		{
			Flash(await _banks.RetireQuestionAsync(await ActorAsync(), questionId, cancellationToken), "RetiredQuestionMessage");
			return RedirectToAction("Bank", new { id });
		}

		#endregion Question banks
	}
}
