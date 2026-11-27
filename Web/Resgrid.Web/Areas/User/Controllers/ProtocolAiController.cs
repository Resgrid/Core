using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Enhanced AI on protocols (round 7A; Enhanced AI only, never the LMS): protocol flashcards (authors draft, accept, edit, review and remove;
	/// members practise the deck on the protocol view), "Draft a training from this protocol" (a baseline Training created unassigned after the
	/// author reviews the draft) and "Draft translation with AI" on a draft translation (the editor shows the drafted wording; the author saves
	/// it through the normal translation save, and it still needs its L2 approval). The services decide every gate (Enhanced AI with knowledge
	/// answers, the author right, "no AI" and licensed content, Advanced Data Protection) and fail closed.
	/// </summary>
	[Area("User")]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class ProtocolAiController : SecureBaseController
	{
		private readonly IProtocolFlashcardService _flashcards;
		private readonly IProtocolTrainingDraftService _trainingDrafts;
		private readonly IProtocolTranslationDraftService _translationDrafts;
		private readonly IProtocolTranslationService _translations;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.ProtocolAi.ProtocolAi> _localizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _protocolText;

		public ProtocolAiController(IProtocolFlashcardService flashcards, IProtocolTrainingDraftService trainingDrafts, IProtocolTranslationDraftService translationDrafts,
			IProtocolTranslationService translations, IProtocolAccessService access, IProtocolLibraryService library, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.ProtocolAi.ProtocolAi> localizer, IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> protocolText)
		{
			_flashcards = flashcards;
			_trainingDrafts = trainingDrafts;
			_translationDrafts = translationDrafts;
			_translations = translations;
			_access = access;
			_library = library;
			_departments = departments;
			_localizer = localizer;
			_protocolText = protocolText;
		}

		private string Text(string key) => _localizer[key].Value;

		private string OutcomeText(string outcome) => Text("Outcome." + (string.IsNullOrWhiteSpace(outcome) ? ProtocolAiOutcomes.Unavailable : outcome));

		private static string Locale => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

		/// <summary>The protocol when the member authors or approves protocols and may read it (the services check again).</summary>
		private async Task<DispatchProtocol> AuthorableAsync(int protocolId)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (reader == null || !(reader.MayAuthor || reader.MayApprove))
				return null;
			var protocol = await _access.GetReadableAsync(DepartmentId, UserId, protocolId);
			return protocol != null && protocol.DepartmentId == DepartmentId && !protocol.IsDeleted ? protocol : null;
		}

		#region Flashcards (authors)

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Flashcards(int id, int? edit = null, CancellationToken cancellationToken = default)
		{
			var protocol = await AuthorableAsync(id);
			if (protocol == null)
				return NotFound();

			return View(new ProtocolFlashcardsModel
			{
				Protocol = protocol,
				Status = await _flashcards.GetStatusAsync(DepartmentId, UserId, id, cancellationToken),
				Cards = await _flashcards.GetCardsAsync(DepartmentId, UserId, id),
				EditingCardId = edit,
				Message = TempData?["ProtocolAiMessage"] as string,
				Error = TempData?["ProtocolAiError"] as string
			});
		}

		/// <summary>Drafts cards from the protocol's version in force (JSON; nothing is saved but the audit row).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> DraftCards(int id, int count = 10, CancellationToken cancellationToken = default)
		{
			var draft = await _flashcards.DraftAsync(new ProtocolFlashcardDraftRequest { DepartmentId = DepartmentId, UserId = UserId, Locale = Locale, ProtocolId = id, Count = count },
				cancellationToken);
			if (draft.Outcome != ProtocolAiOutcomes.Drafted)
				return Json(new { ok = false, message = OutcomeText(draft.Outcome) });
			return Json(new
			{
				ok = true,
				exchangeId = draft.ExchangeId,
				dropped = draft.Dropped,
				droppedText = draft.Dropped > 0 ? string.Format(Text("CardsDropped"), draft.Dropped) : null,
				source = string.IsNullOrWhiteSpace(draft.SourceVersionLabel) ? draft.SourceTitle : draft.SourceTitle + " " + draft.SourceVersionLabel,
				cards = draft.Cards.Select((c, i) => new { index = i, front = c.Front, back = c.Back, heading = c.Heading })
			});
		}

		/// <summary>Saves the drafted cards the author ticked, read back from the stored draft (JSON).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> AcceptCards(int id, string exchangeId, [FromForm] int[] indexes, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.AcceptAsync(DepartmentId, UserId, id, exchangeId, indexes ?? Array.Empty<int>(), cancellationToken);
			if (!result.Succeeded)
				return Json(new { ok = false, message = OutcomeText(result.Outcome) });
			return Json(new { ok = true, reload = result.Count > 0, message = string.Format(Text("CardsAccepted"), result.Count) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> SaveCard(int id, int cardId, string front, string back, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.SaveAsync(DepartmentId, UserId, cardId, front, back, cancellationToken);
			TempData[result.Succeeded ? "ProtocolAiMessage" : "ProtocolAiError"] = result.Succeeded ? Text("CardSaved") : OutcomeText(result.Outcome);
			return RedirectToAction("Flashcards", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ReviewCard(int id, int cardId, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.ReviewAsync(DepartmentId, UserId, cardId, cancellationToken);
			TempData[result.Succeeded ? "ProtocolAiMessage" : "ProtocolAiError"] = result.Succeeded ? Text("CardReviewed") : OutcomeText(result.Outcome);
			return RedirectToAction("Flashcards", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> RemoveCard(int id, int cardId, CancellationToken cancellationToken = default)
		{
			var result = await _flashcards.RemoveAsync(DepartmentId, UserId, cardId, cancellationToken);
			TempData[result.Succeeded ? "ProtocolAiMessage" : "ProtocolAiError"] = result.Succeeded ? Text("CardRemoved") : OutcomeText(result.Outcome);
			return RedirectToAction("Flashcards", new { id });
		}

		#endregion Flashcards (authors)

		#region Practice deck (members)

		/// <summary>The member's practice cards for a protocol they may read (JSON; empty while Enhanced AI is off). Practice is never recorded.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
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
					source = string.Join(" · ", new[] { c.SourceVersionLabel, c.SourceHeading }.Where(s => !string.IsNullOrWhiteSpace(s))),
					url = c.SourceUrl
				})
			});
		}

		#endregion Practice deck (members)

		#region Training draft

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> DraftTraining(int id, CancellationToken cancellationToken = default)
		{
			var protocol = await AuthorableAsync(id);
			if (protocol == null)
				return NotFound();
			return View(new ProtocolTrainingDraftModel
			{
				Protocol = protocol,
				Status = await _trainingDrafts.GetStatusAsync(DepartmentId, UserId, id, cancellationToken),
				Error = TempData?["ProtocolAiError"] as string
			});
		}

		/// <summary>Drafts the training and shows it for review (nothing is created until the author accepts it).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		[ActionName("DraftTraining")]
		public async Task<IActionResult> DraftTrainingPost(int id, int questionCount = 5, CancellationToken cancellationToken = default)
		{
			var protocol = await AuthorableAsync(id);
			if (protocol == null)
				return NotFound();

			var draft = await _trainingDrafts.DraftAsync(new ProtocolTrainingDraftRequest
			{
				DepartmentId = DepartmentId, UserId = UserId, Locale = Locale, ProtocolId = id, QuestionCount = questionCount
			}, cancellationToken);
			var model = new ProtocolTrainingDraftModel
			{
				Protocol = protocol,
				Status = await _trainingDrafts.GetStatusAsync(DepartmentId, UserId, id, cancellationToken),
				QuestionCount = questionCount
			};
			if (draft.Outcome == ProtocolAiOutcomes.Drafted)
				model.Draft = draft;
			else
				model.Error = OutcomeText(draft.Outcome);
			return View("DraftTraining", model);
		}

		/// <summary>Creates the reviewed draft as an unassigned training (read back from the stored draft, never the form) and opens it in the editor.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		[Authorize(Policy = ResgridResources.Training_Create)]
		public async Task<IActionResult> CreateTraining(int id, string exchangeId, string title, CancellationToken cancellationToken = default)
		{
			var result = await _trainingDrafts.CreateAsync(DepartmentId, UserId, id, exchangeId, title, Locale, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["ProtocolAiError"] = OutcomeText(result.Outcome);
				return RedirectToAction("DraftTraining", new { id });
			}

			TempData["TrainingMessage"] = Text("TrainingCreatedMessage");
			return RedirectToAction("Edit", "Trainings", new { area = "User", trainingId = result.TrainingId });
		}

		#endregion Training draft

		#region Translation draft

		/// <summary>
		/// Drafts the wording of a draft translation with Enhanced AI and shows it in the translation editor, unsaved: the author reviews it and
		/// saves through the editor's normal save (structure, doses and links stay the source's), and the translation still needs its L2 approval.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> DraftTranslation(int id, int translationId, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var translation = await _translations.GetAsync(DepartmentId, translationId);
			if (translation == null || translation.DispatchProtocolId != id || reader == null || !reader.IsManager)
				return NotFound();
			var protocol = await _access.GetReadableAsync(DepartmentId, UserId, id);
			var version = await _library.GetVersionAsync(DepartmentId, translation.ProtocolVersionId);
			if (protocol == null || protocol.DepartmentId != DepartmentId || version == null || version.DispatchProtocolId != id)
				return NotFound();

			var draft = await _translationDrafts.DraftAsync(DepartmentId, UserId, translationId, cancellationToken);
			if (draft.Outcome != ProtocolAiOutcomes.Drafted || draft.Document == null)
			{
				TempData["TranslationError"] = OutcomeText(draft.Outcome);
				return RedirectToAction("Translation", "ProtocolTools", new { area = "User", id, translationId });
			}

			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId))
					names[person.UserId] = person.Name?.Trim();

			var notice = string.Format(Text("TranslationDraftedNotice"), draft.Translated);
			if (draft.Dropped > 0)
				notice += " " + string.Format(Text("TranslationDroppedNotice"), draft.Dropped);
			if (draft.Remaining > 0)
				notice += " " + string.Format(Text("TranslationRemainingNotice"), draft.Remaining);

			return View("~/Areas/User/Views/ProtocolTools/Translation.cshtml", new ProtocolTranslationEditModel
			{
				Protocol = protocol,
				Version = version,
				Translation = translation,
				Source = ProtocolContentDocument.Parse(version.ContentJson, version.ContentHtml),
				Translated = draft.Document,
				TranslatedHtml = translation.ContentHtml,
				CanEdit = reader.MayAuthor && translation.Status == (int)ProtocolTranslationStatuses.Draft,
				// Approval is for what is saved: not offered on an unsaved draft.
				CanApprove = false,
				SourceChanged = !string.Equals(translation.SourceContentHash, version.ContentHash, StringComparison.OrdinalIgnoreCase),
				Statement = ProtocolSignatureStatements.TranslationApproval(protocol, version, translation, _protocolText["TranslationApprovalStatement"].Value),
				Names = names,
				IsAiDraft = true,
				AiNotice = notice
			});
		}

		#endregion Translation draft
	}
}
