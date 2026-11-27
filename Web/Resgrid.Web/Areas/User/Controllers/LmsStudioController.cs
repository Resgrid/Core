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
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Courses;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Trainings → Course library → the course studio (Training T5 items 4 and 5, protocols-training-knowledge-plan §13 T5, §13a "Learning Studio",
	/// §16.3, §17.2): the Resgrid starter course library (clone into a draft with attribution; LMS), course drafts from a protocol version or an
	/// uploaded SOP (Enhanced AI and the LMS), and a course's flashcards (drafted, accepted, edited, reviewed when their source changes) with
	/// the learner's practice deck for the course player. Dark while the Training.Lms flag is off; the Trainings module switch closes every page.
	/// Every new course is a draft the author edits and submits through the course's normal approval.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class LmsStudioController : SecureBaseController
	{
		private const long MaxDraftRequestBytes = LmsCourseDraftRequest.MaxDocumentBytes + 512 * 1024;

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsCourseService _courses;
		private readonly ILmsCourseDraftService _drafts;
		private readonly ILmsFlashcardService _flashcards;
		private readonly ILmsStarterCourseService _starters;
		private readonly IAuthorizationService _authorizationService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Courses.Courses> _localizer;
		private readonly IDepartmentSettingsService _departmentSettings;

		private LmsActor _actor;

		public LmsStudioController(ILmsGateService gate, ILmsAccessService access, ILmsCourseService courses, ILmsCourseDraftService drafts, ILmsFlashcardService flashcards,
			ILmsStarterCourseService starters, IAuthorizationService authorizationService, IStringLocalizer<Resgrid.Localization.Areas.User.Courses.Courses> localizer,
			IDepartmentSettingsService departmentSettings)
		{
			_departmentSettings = departmentSettings;
			_gate = gate;
			_access = access;
			_courses = courses;
			_drafts = drafts;
			_flashcards = flashcards;
			_starters = starters;
			_authorizationService = authorizationService;
			_localizer = localizer;
		}

		#region Gates and helpers

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
			{
				context.Result = NotFound();
				return;
			}

			var actor = await ActorAsync();
			// Dark while the LMS flag is off (like the course library); each action checks its own right and add-on.
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.FlagOn)
			{
				context.Result = NotFound();
				return;
			}

			ViewData["Scope"] = await _authorizationService.GetTrainingManagementScopeAsync(UserId, DepartmentId) ?? TrainingManagementScope.None(UserId, DepartmentId);
			ViewData["LmsLibrary"] = actor.MayManage || actor.MayGrade;
			ViewData["AcademyLesson"] = "09-training";
			await next();
		}

		private async Task<LmsActor> ActorAsync() => _actor ??= await _gate.GetActorAsync(DepartmentId, UserId);

		private string Text(string key) => _localizer[key].Value;

		private string OutcomeText(string outcome) => Text("Studio.Outcome." + (string.IsNullOrWhiteSpace(outcome) ? LmsStudioOutcomes.Unavailable : outcome));

		private static string Locale => CultureInfo.CurrentUICulture.Name;

		private T Page<T>(T model) where T : CoursesPageModel
		{
			model.Message ??= TempData["CourseMessage"] as string;
			model.Error ??= TempData["CourseError"] as string;
			model.Actor = _actor;
			return model;
		}

		/// <summary>The studio's own strip under the Training sections: the course library, the starter library and drafting with AI.</summary>
		private void SubTabs(string active, bool aiTab)
		{
			var tabs = new ModuleTabsView { Label = Text("Studio.TabsLabel") };
			tabs.Tabs.Add(new ModuleTab(Text("LibraryTitle"), "fa-sitemap", "Courses", "Library", false));
			tabs.Tabs.Add(new ModuleTab(Text("Studio.StartersTab"), "fa-clone", "LmsStudio", "Starters", active == "Starters"));
			if (aiTab)
				tabs.Tabs.Add(new ModuleTab(Text("Studio.DraftTab"), "fa-magic", "LmsStudio", "Draft", active == "Draft"));
			ViewData["SubTabs"] = tabs;
		}

		#endregion Gates and helpers

		#region Starter library

		[HttpGet]
		public async Task<IActionResult> Starters()
		{
			var actor = await ActorAsync();
			if (!actor.MayManage)
				return NotFound();
			var status = await _drafts.GetStatusAsync(DepartmentId, UserId);
			SubTabs("Starters", status.AiAvailable);
			var catalog = _starters.GetCatalog(Locale);
			// Round 9C: starters in the reader's language; the ones the operating profile suggests come first (nothing is hidden).
			var profile = await _departmentSettings.GetOperatingProfileAsync(DepartmentId);
			return View(Page(new StarterLibraryModel
			{
				Starters = SeedSectorRecommendations.SuggestedFirst(catalog.Where(e => !e.IsPartnerSlot), SeedContentLocalization.StarterCourses, e => e.Key, profile),
				Suggested = SeedSectorRecommendations.Suggested(SeedContentLocalization.StarterCourses, catalog.Select(e => e.Key), profile),
				PartnerSlots = catalog.Where(e => e.IsPartnerSlot).ToList(),
				Status = status
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CloneStarter(string key, CancellationToken cancellationToken = default)
		{
			var result = await _starters.CloneAsync(await ActorAsync(), key, Locale, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["CourseError"] = OutcomeText(result.Outcome);
				return RedirectToAction("Starters");
			}
			TempData["CourseMessage"] = string.Format(Text("Studio.StarterCloned"), result.Lessons, result.Questions);
			return RedirectToAction("Builder", "Courses", new { area = "User", id = result.LmsCourseVersionId });
		}

		#endregion Starter library

		#region Course drafts (Enhanced AI)

		[HttpGet]
		public async Task<IActionResult> Draft(int? protocolId = null)
		{
			var actor = await ActorAsync();
			if (!actor.MayManage)
				return NotFound();
			var status = await _drafts.GetStatusAsync(DepartmentId, UserId);
			SubTabs("Draft", true);
			return View(Page(new CourseDraftModel
			{
				Status = status,
				Protocols = status.AiAvailable ? await _drafts.GetDraftableProtocolsAsync(DepartmentId, UserId) : new List<LmsDraftableProtocol>(),
				ProtocolId = protocolId
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(MaxDraftRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxDraftRequestBytes)]
		public async Task<IActionResult> Draft(int sourceKind, int protocolId, IFormFile file, string title, int questionCount = 5, CancellationToken cancellationToken = default)
		{
			var request = new LmsCourseDraftRequest
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Locale = Locale,
				SourceKind = sourceKind,
				ProtocolId = protocolId,
				Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
				QuestionCount = questionCount
			};

			if (sourceKind == (int)LmsCourseDraftSourceKinds.Document)
			{
				if (file == null || file.Length == 0 || file.Length > LmsCourseDraftRequest.MaxDocumentBytes)
				{
					TempData["CourseError"] = OutcomeText(LmsStudioOutcomes.Invalid);
					return RedirectToAction("Draft");
				}
				using var memory = new MemoryStream();
				await file.CopyToAsync(memory, cancellationToken);
				request.FileName = file.FileName;
				request.ContentType = file.ContentType;
				request.Data = memory.ToArray();
			}

			var result = await _drafts.DraftAsync(request, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["CourseError"] = OutcomeText(result.Outcome);
				return RedirectToAction("Draft", new { protocolId = protocolId > 0 ? protocolId : (int?)null });
			}

			var message = string.Format(Text("Studio.Drafted"), result.Modules, result.Lessons, result.Questions);
			if (result.Dropped > 0)
				message += " " + string.Format(Text("Studio.DraftDropped"), result.Dropped);
			if (result.Truncated)
				message += " " + Text("Studio.DraftTruncated");
			TempData["CourseMessage"] = message;
			return RedirectToAction("Builder", "Courses", new { area = "User", id = result.LmsCourseVersionId });
		}

		#endregion Course drafts (Enhanced AI)

		#region Flashcards (authors)

		private async Task<LmsCourse> EditableCourseAsync(int id)
		{
			var actor = await ActorAsync();
			var course = await _courses.GetCourseAsync(DepartmentId, id);
			if (course == null || course.IsSystem || course.IsDeleted)
				return null;
			return (await _courses.GetRightsAsync(actor, course))?.Edit == true ? course : null;
		}

		[HttpGet]
		public async Task<IActionResult> Flashcards(int id, int? edit = null)
		{
			var course = await EditableCourseAsync(id);
			if (course == null)
				return NotFound();
			var status = await _drafts.GetStatusAsync(DepartmentId, UserId);
			ViewData["SubTabs"] = null;
			return View(Page(new FlashcardsModel
			{
				Course = course,
				Cards = await _flashcards.GetCardsAsync(DepartmentId, UserId, id),
				Choices = status.AiAvailable ? await _flashcards.GetSourceChoicesAsync(DepartmentId, UserId, id) : new List<LmsFlashcardSourceChoice>(),
				Status = status,
				EditingCardId = edit
			}));
		}

		/// <summary>Drafts cards (JSON): nothing is saved but the audit row; the author accepts what to keep.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DraftCards(int id, int sourceKind, int protocolId, int count = 10, CancellationToken cancellationToken = default)
		{
			var draft = await _flashcards.DraftAsync(new LmsFlashcardDraftRequest
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Locale = Locale,
				LmsCourseId = id,
				SourceKind = sourceKind,
				ProtocolId = protocolId,
				Count = count
			}, cancellationToken);
			if (draft.Outcome != LmsStudioOutcomes.Drafted)
				return Json(new { ok = false, message = OutcomeText(draft.Outcome) });
			return Json(new
			{
				ok = true,
				exchangeId = draft.ExchangeId,
				dropped = draft.Dropped,
				droppedText = draft.Dropped > 0 ? string.Format(Text("Studio.CardsDropped"), draft.Dropped) : null,
				source = string.IsNullOrWhiteSpace(draft.SourceVersionLabel) ? draft.SourceTitle : draft.SourceTitle + " " + draft.SourceVersionLabel,
				cards = draft.Cards.Select((c, i) => new { index = i, front = c.Front, back = c.Back, heading = c.Heading })
			});
		}

		/// <summary>Saves the drafted cards the author ticked, read back from the stored draft (JSON).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AcceptCards(int id, string exchangeId, [FromForm] int[] indexes, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.AcceptAsync(DepartmentId, UserId, id, exchangeId, indexes ?? Array.Empty<int>(), cancellationToken);
			if (!result.Succeeded)
				return Json(new { ok = false, message = OutcomeText(result.Outcome) });
			return Json(new { ok = true, reload = result.Count > 0, message = string.Format(Text("Studio.CardsAccepted"), result.Count) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveCard(int id, int cardId, string front, string back, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.SaveAsync(DepartmentId, UserId, cardId, front, back, cancellationToken);
			TempData[result.Succeeded ? "CourseMessage" : "CourseError"] = result.Succeeded ? Text("Studio.CardSaved") : OutcomeText(result.Outcome);
			return RedirectToAction("Flashcards", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ReviewCard(int id, int cardId, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.ReviewAsync(DepartmentId, UserId, cardId, cancellationToken);
			TempData[result.Succeeded ? "CourseMessage" : "CourseError"] = result.Succeeded ? Text("Studio.CardReviewed") : OutcomeText(result.Outcome);
			return RedirectToAction("Flashcards", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveCard(int id, int cardId, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.RemoveAsync(DepartmentId, UserId, cardId, cancellationToken);
			TempData[result.Succeeded ? "CourseMessage" : "CourseError"] = result.Succeeded ? Text("Studio.CardRemoved") : OutcomeText(result.Outcome);
			return RedirectToAction("Flashcards", new { id });
		}

		#endregion Flashcards (authors)

		#region Practice deck (learners)

		/// <summary>
		/// The learner's practice cards for one of their enrollments (JSON, read only). Spaced practice happens in the browser: nothing about it is
		/// sent back, graded or recorded. Empty while either gate (LMS, Enhanced AI) is closed.
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> Deck(int id, CancellationToken cancellationToken = default)
		{
			var deck = await _flashcards.GetDeckAsync(DepartmentId, UserId, id, cancellationToken);
			return Json(new
			{
				cards = deck.Select(c => new
				{
					id = c.Id,
					front = c.Front,
					back = c.Back,
					source = string.Join(" · ", new[] { string.IsNullOrWhiteSpace(c.SourceVersionLabel) ? c.SourceTitle : c.SourceTitle + " " + c.SourceVersionLabel, c.SourceHeading }
						.Where(s => !string.IsNullOrWhiteSpace(s))),
					url = c.SourceUrl
				})
			});
		}

		#endregion Practice deck (learners)
	}
}
