using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Protocols P3 field tools on the web: step-through on a call (and the incident SOP checklists), the block editor's preview, the QR
	/// poster and print view of a protocol, the PDF book of a category or the library, and content translations. Every read applies
	/// the protocol read rule (R-P4); call actions check the call (view to read, edit to write).
	/// </summary>
	[Area("User")]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class ProtocolToolsController : SecureBaseController
	{
		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolStepThroughService _steps;
		private readonly IProtocolBookService _books;
		private readonly IProtocolTranslationService _translations;
		private readonly IDocumentSealingService _sealing;
		private readonly IAiKnowledgeService _knowledge;
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> _signatureText;

		public ProtocolToolsController(IProtocolsService protocolsService, IProtocolAccessService access, IProtocolLibraryService library,
			IProtocolStepThroughService steps, IProtocolBookService books, IProtocolTranslationService translations, IDocumentSealingService sealing,
			IAiKnowledgeService knowledge, ICallsService callsService, IAuthorizationService authorization, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> signatureText)
		{
			_protocolsService = protocolsService;
			_access = access;
			_library = library;
			_steps = steps;
			_books = books;
			_translations = translations;
			_sealing = sealing;
			_knowledge = knowledge;
			_callsService = callsService;
			_authorization = authorization;
			_departments = departments;
			_localizer = localizer;
			_signatureText = signatureText;
		}

		private string Text(string key) => _localizer[key].Value;

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId))
					names[person.UserId] = person.Name?.Trim();
			return names;
		}

		/// <summary>The stable deep link of a protocol (the version in force when opened): what the QR code and the apps open.</summary>
		public static string DeepLink(int protocolId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Protocols/View/" + protocolId.ToString(CultureInfo.InvariantCulture);

		private async Task<string> DosingToolUrlAsync()
		{
			try
			{
				return (await _knowledge.GetSettingsAsync(DepartmentId))?.DosingToolUrl;
			}
			catch (Exception ex)
			{
				// No link rather than a failed page.
				Logging.LogException(ex, "The dosing tool link could not be read.");
				return null;
			}
		}

		#region Block editor preview

		/// <summary>
		/// The block editor's preview (P3): the posted document rendered by the server renderer, sanitized and shown with the reading view's
		/// helpers (block anchors, the dosing-tool link), so the preview is exactly what members will read. Nothing is saved.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> PreviewBlocks(int id, string contentJson)
		{
			if (string.IsNullOrWhiteSpace(contentJson) || contentJson.Length > 2_000_000)
				return BadRequest();
			if (!await _authorization.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var settings = await _library.GetSettingsAsync(DepartmentId);
			var draft = await _library.GetOpenDraftAsync(DepartmentId, id);
			var document = ProtocolBlocks.Normalize(ProtocolContentDocument.Parse(contentJson, null));
			var html = document.Render(ProtocolBlockLabelsProvider.For(settings.ContentLocaleFor(draft)));
			return Json(new
			{
				html = ProtocolContentDocument.InjectDosingLinks(ProtocolContentDocument.DisplayHtml(html), await DosingToolUrlAsync(), Text("DosingToolLinkText")),
				outline = ProtocolBlocks.BlockOutline(document.Blocks).Select(a => new { id = a.Id, text = a.Text, level = a.Level })
			});
		}

		#endregion Block editor preview

		#region Step-through on a call

		/// <summary>One protocol on a call run like a checklist.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Steps(int callId, int callProtocolId)
		{
			if (!await _authorization.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			var call = await _callsService.GetCallByIdAsync(callId);
			var checklist = await _steps.GetCallChecklistAsync(DepartmentId, callId, callProtocolId);
			if (call == null || call.DepartmentId != DepartmentId || checklist == null)
				return NotFound();

			return View(new ProtocolStepRunModel
			{
				Call = call,
				Checklist = checklist,
				CanEdit = !checklist.Row.IsRemoved && await _authorization.CanUserEditCallAsync(UserId, callId),
				Names = await NamesAsync(),
				// Stored text; the view sanitizes it on output (Html.ProtocolContent) and fills the dosing-tool slots.
				ContentHtml = checklist.Version == null || checklist.Version.IsDocument ? null : checklist.Version.ContentHtml,
				DosingToolUrl = await DosingToolUrlAsync(),
				Message = TempData?["StepMessage"] as string,
				Error = TempData?["StepError"] as string
			});
		}

		/// <summary>Starts a protocol (an incident SOP checklist) on the call: attached as a manual pick when it is not on it yet.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> StartSteps(int callId, int protocolId, CancellationToken cancellationToken = default)
		{
			if (!await _authorization.CanUserEditCallAsync(UserId, callId))
				return Unauthorized();

			var result = await _steps.StartAsync(DepartmentId, callId, protocolId, UserId, cancellationToken);
			if (!result.Succeeded && result.Checklist == null)
			{
				TempData["CallProtocolMessage"] = "StepThrough" + result.Error;
				return RedirectToAction("ViewCall", "Dispatch", new { area = "User", callId });
			}

			if (!result.Succeeded)
				TempData["StepError"] = Text("StepError" + result.Error);
			return RedirectToAction("Steps", new { callId, callProtocolId = result.Checklist.Row.CallProtocolId });
		}

		/// <summary>
		/// One step event, timestamped by the server. Called by the run page's script (JSON back) or by its plain form (redirect back), so the
		/// checklist works without script too.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> RecordStep(int callId, int callProtocolId, string stepId, int state, string note, bool json = false,
			CancellationToken cancellationToken = default)
		{
			if (!await _authorization.CanUserEditCallAsync(UserId, callId))
				return json ? StatusCode(StatusCodes.Status403Forbidden) : Unauthorized();

			var result = await _steps.RecordStepAsync(DepartmentId, callId, callProtocolId, stepId, (ProtocolStepStates)state, note, null, UserId, cancellationToken);
			if (json)
			{
				var view = result.Checklist?.Steps.FirstOrDefault(s => string.Equals(s.Step.Id, stepId, StringComparison.Ordinal));
				return Json(new
				{
					success = result.Succeeded,
					error = result.Succeeded ? null : Text("StepError" + result.Error),
					state = (int)(view?.State ?? ProtocolStepStates.Pending),
					at = view?.Last?.At,
					done = result.Checklist?.DoneCount ?? 0,
					total = result.Checklist?.Steps.Count ?? 0
				});
			}

			if (!result.Succeeded)
				TempData["StepError"] = Text("StepError" + result.Error);
			return Redirect(Url.Action("Steps", new { callId, callProtocolId }) + "#step-" + Uri.EscapeDataString(stepId ?? string.Empty));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> CompleteSteps(int callId, int callProtocolId, CancellationToken cancellationToken = default)
		{
			if (!await _authorization.CanUserEditCallAsync(UserId, callId))
				return Unauthorized();

			var result = await _steps.CompleteAsync(DepartmentId, callId, callProtocolId, UserId, cancellationToken);
			TempData[result.Succeeded ? "StepMessage" : "StepError"] = result.Succeeded ? Text("StepsCompletedMessage") : Text("StepError" + result.Error);
			return RedirectToAction("Steps", new { callId, callProtocolId });
		}

		#endregion Step-through on a call

		#region QR, print view and the PDF book

		/// <summary>The QR poster of a protocol: its deep link (the version in force when scanned) for apparatus and station walls.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Qr(int id)
		{
			var protocol = await _access.GetReadableAsync(DepartmentId, UserId, id);
			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return NotFound();

			var link = DeepLink(protocol.DispatchProtocolId);
			string qr = null;
			try
			{
				qr = WebQrCode.DataUrl(link);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A protocol QR code could not be drawn.");
			}

			return View(new ProtocolQrModel
			{
				Protocol = protocol,
				Version = protocol.CurrentVersionId.HasValue ? await _library.GetVersionAsync(DepartmentId, protocol.CurrentVersionId.Value) : null,
				DeepLink = link,
				QrDataUrl = qr
			});
		}

		/// <summary>The print view of a version (the version in force by default), in the reader's language when an approved translation exists.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Print(int id, int? versionId = null, string lang = null)
		{
			var protocol = await _access.GetReadableAsync(DepartmentId, UserId, id);
			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return NotFound();

			var version = await _library.GetVersionAsync(DepartmentId, versionId ?? protocol.CurrentVersionId ?? 0);
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (version == null || version.DispatchProtocolId != id || !ProtocolAccessRules.CanReadVersion(reader, protocol, await _library.GetAudienceAsync(DepartmentId, id), version))
				return NotFound();

			var localized = await _translations.GetLocalizedAsync(DepartmentId, version, string.IsNullOrWhiteSpace(lang) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : lang);
			var approvals = await _library.GetApprovalsAsync(DepartmentId, version.ProtocolVersionId);
			var names = await NamesAsync();
			var approverIds = approvals.Where(a => a.Decision == (int)ProtocolApprovalDecisions.Approved && !string.IsNullOrWhiteSpace(a.DecidedByUserId))
				.Select(a => a.DecidedByUserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var link = DeepLink(id);
			string qr = null;
			try { qr = WebQrCode.DataUrl(link); } catch (Exception ex) { Logging.LogException(ex, "A protocol QR code could not be drawn."); }

			return View(new ProtocolPrintModel
			{
				Protocol = protocol,
				Version = version,
				Html = version.IsDocument ? null : localized.ContentHtml,
				DosingToolUrl = await DosingToolUrlAsync(),
				Locale = localized.Locale,
				IsTranslation = localized.Translation != null,
				ApproverNames = string.Join(", ", approverIds.Select(x => names.TryGetValue(x, out var n) && !string.IsNullOrWhiteSpace(n) ? n : x)),
				DeepLink = link,
				QrDataUrl = qr,
				DepartmentName = (await _departments.GetDepartmentByIdAsync(DepartmentId, false))?.Name
			});
		}

		/// <summary>The PDF book page: the whole library or one category.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Book()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var readable = await _access.FilterReadableAsync(reader, await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId, true) ?? new List<DispatchProtocol>());
			return View(new ProtocolBookModel
			{
				Categories = await _library.GetCategoriesAsync(DepartmentId),
				SealingReady = _sealing.GetAvailability()?.Ready ?? false,
				ReadableCount = readable.Count(p => p.IsPublished && !p.IsDeleted)
			});
		}

		/// <summary>The PDF book: sealed when sealing is configured, else the unsealed rendering with "sealing pending" (fails closed).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> BookPdf(int? categoryId = null, CancellationToken cancellationToken = default)
		{
			try
			{
				var export = await _books.ExportPdfAsync(DepartmentId, UserId, categoryId, BookText(), CultureInfo.CurrentUICulture.Name,
					string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase) ? "Letter" : "A4", cancellationToken);
				if (export?.Pdf == null)
					return NotFound();

				Response.Headers["Cache-Control"] = "no-store";
				Response.Headers["X-Resgrid-Seal"] = export.Sealed ? "sealed" : "pending";
				return File(export.Pdf, "application/pdf", export.FileName);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A protocol book PDF could not be produced.");
				return Problem(Text("BookFailed"), statusCode: StatusCodes.Status500InternalServerError);
			}
		}

		private ProtocolBookText BookText() => new ProtocolBookText
		{
			LibraryTitle = Text("BookLibraryTitle"),
			Contents = Text("BookContents"),
			Version = Text("BookVersion"),
			EffectiveOn = Text("BookEffective"),
			ApprovedBy = Text("BookApprovedBy"),
			ContentHash = Text("BookContent"),
			GeneratedOn = Text("BookGenerated"),
			DocumentBacked = Text("BookDocumentBacked"),
			Empty = Text("BookEmpty"),
			Uncategorized = Text("BookUncategorized"),
			DosingToolLink = Text("DosingToolLinkText")
		};

		#endregion QR, print view and the PDF book

		#region Translations

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Translations(int id, int versionId)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var (protocol, version) = await ReadableVersionAsync(reader, id, versionId);
			if (version == null || !reader.IsManager)
				return NotFound();

			var settings = await _library.GetSettingsAsync(DepartmentId);
			return View(new ProtocolTranslationsModel
			{
				Protocol = protocol,
				Version = version,
				SourceLocale = settings.ContentLocaleFor(version),
				Translations = await _translations.GetForVersionAsync(DepartmentId, versionId),
				Names = await NamesAsync(),
				CanAuthor = reader.MayAuthor,
				CanApprove = reader.MayApprove,
				Message = TempData?["TranslationMessage"] as string,
				Error = TempData?["TranslationError"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> StartTranslation(int id, int versionId, string locale, CancellationToken cancellationToken = default)
		{
			var result = await _translations.StartAsync(await _access.GetReaderAsync(DepartmentId, UserId), versionId, locale, cancellationToken);
			if (result.Succeeded)
				return RedirectToAction("Translation", new { id, translationId = result.Translation.ProtocolVersionTranslationId });

			TempData["TranslationError"] = Text("TranslationError" + result.Error);
			return result.Translation != null
				? RedirectToAction("Translation", new { id, translationId = result.Translation.ProtocolVersionTranslationId })
				: RedirectToAction("Translations", new { id, versionId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Translation(int id, int translationId)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var translation = await _translations.GetAsync(DepartmentId, translationId);
			if (translation == null || translation.DispatchProtocolId != id || !reader.IsManager)
				return NotFound();
			var (protocol, version) = await ReadableVersionAsync(reader, id, translation.ProtocolVersionId);
			if (version == null)
				return NotFound();

			return View(new ProtocolTranslationEditModel
			{
				Protocol = protocol,
				Version = version,
				Translation = translation,
				Source = ProtocolContentDocument.Parse(version.ContentJson, version.ContentHtml),
				Translated = ProtocolContentDocument.Parse(translation.ContentJson, translation.ContentHtml),
				TranslatedHtml = translation.ContentHtml,
				CanEdit = reader.MayAuthor && translation.Status == (int)ProtocolTranslationStatuses.Draft,
				CanApprove = reader.MayApprove && translation.Status == (int)ProtocolTranslationStatuses.Draft,
				SourceChanged = !string.Equals(translation.SourceContentHash, version.ContentHash, StringComparison.OrdinalIgnoreCase),
				Statement = ProtocolSignatureStatements.TranslationApproval(protocol, version, translation, _localizer["TranslationApprovalStatement"].Value),
				Names = await NamesAsync(),
				Message = TempData?["TranslationMessage"] as string,
				Error = TempData?["TranslationError"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> SaveTranslation(int id, int translationId, long rowVersion, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var translation = await _translations.GetAsync(DepartmentId, translationId);
			if (translation == null || translation.DispatchProtocolId != id)
				return NotFound();
			var version = await _library.GetVersionAsync(DepartmentId, translation.ProtocolVersionId);
			if (version == null)
				return NotFound();

			var source = ProtocolContentDocument.Parse(version.ContentJson, version.ContentHtml);
			var posted = TranslationForm.Read(source, Request.Form);
			var result = await _translations.SaveAsync(reader, translationId, posted.IsBlockModel ? posted.ToJson() : null, posted.IsBlockModel ? null : posted.Render(),
				rowVersion, cancellationToken);
			TempData[result.Succeeded ? "TranslationMessage" : "TranslationError"] = result.Succeeded ? Text("TranslationSavedMessage") : Text("TranslationError" + result.Error);
			return RedirectToAction("Translation", new { id, translationId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		[Resgrid.Web.Attributes.RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> ApproveTranslation(int id, int translationId, CancellationToken cancellationToken = default)
		{
			var translation = await _translations.GetAsync(DepartmentId, translationId);
			if (translation == null || translation.DispatchProtocolId != id)
				return NotFound();
			var version = await _library.GetVersionAsync(DepartmentId, translation.ProtocolVersionId);
			var protocol = await _access.GetReadableAsync(DepartmentId, UserId, id);
			var statement = protocol == null || version == null ? null
				: ProtocolSignatureStatements.TranslationApproval(protocol, version, translation, _localizer["TranslationApprovalStatement"].Value);

			var proof = SignatureProof.SessionOnly(UserId, IpAddressHelper.GetRequestIP(Request, true), Request?.Headers["User-Agent"].ToString());
			// Set by RequiresRecentTwoFactor(RequireForOperation = true) from this session's server-side evidence.
			proof.StepUpVerifiedOnUtc = HttpContext?.Items["mfa_verified_at"] as DateTime?;
			var result = await _translations.ApproveAsync(await _access.GetReaderAsync(DepartmentId, UserId), translationId, proof, statement, cancellationToken);
			TempData[result.Succeeded ? "TranslationMessage" : "TranslationError"] = result.Succeeded ? Text("TranslationApprovedMessage") : Text("TranslationError" + result.Error);
			return RedirectToAction("Translation", new { id, translationId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> WithdrawTranslation(int id, int translationId, CancellationToken cancellationToken = default)
		{
			var translation = await _translations.GetAsync(DepartmentId, translationId);
			if (translation == null || translation.DispatchProtocolId != id)
				return NotFound();

			var result = await _translations.WithdrawAsync(await _access.GetReaderAsync(DepartmentId, UserId), translationId, cancellationToken);
			TempData[result.Succeeded ? "TranslationMessage" : "TranslationError"] = result.Succeeded ? Text("TranslationWithdrawnMessage") : Text("TranslationError" + result.Error);
			return RedirectToAction("Translations", new { id, versionId = translation.ProtocolVersionId });
		}

		private async Task<(DispatchProtocol Protocol, ProtocolVersion Version)> ReadableVersionAsync(ProtocolReader reader, int protocolId, int versionId)
		{
			var protocol = await _access.GetReadableAsync(DepartmentId, UserId, protocolId);
			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return (null, null);
			var version = await _library.GetVersionAsync(DepartmentId, versionId);
			if (version == null || version.DispatchProtocolId != protocolId
				|| !ProtocolAccessRules.CanReadVersion(reader, protocol, await _library.GetAudienceAsync(DepartmentId, protocolId), version))
				return (protocol, null);
			return (protocol, version);
		}

		#endregion Translations
	}

	/// <summary>
	/// The translation form's fields (P3): one per translatable field of the source, named <c>tr.{key}.{field}</c> (table cells
	/// <c>tr.{key}.cell.{row}.{column}</c>). Read into a copy of the source with the posted wording; the service aligns it again, so a
	/// posted field can never change a dose, a link or the structure.
	/// </summary>
	public static class TranslationForm
	{
		public static string Name(string key, string field) => "tr." + key + "." + field;

		public static ProtocolContentDocument Read(ProtocolContentDocument source, IFormCollection form)
		{
			string Get(string key, string field)
			{
				var value = form?[Name(key, field)].ToString();
				return string.IsNullOrWhiteSpace(value) ? null : value;
			}

			if (source == null || !source.IsBlockModel)
				return ProtocolContentDocument.FromHtml(Get("html", "html") ?? string.Empty);

			var copy = ProtocolContentDocument.Parse(source.ToJson(), null);
			foreach (var (block, _) in ProtocolBlocks.Flatten(copy))
			{
				var key = block.Key ?? string.Empty;
				block.Text = Get(key, "text") ?? block.Text;
				block.Title = Get(key, "title") ?? block.Title;
				block.Html = Get(key, "html") ?? block.Html;
				block.Alt = Get(key, "alt") ?? block.Alt;
				block.Label = Get(key, "label") ?? block.Label;
				block.Contact = Get(key, "contact") ?? block.Contact;
				foreach (var item in block.Items ?? new List<ProtocolStepItem>())
					item.Html = Get(item.Key ?? string.Empty, "html") ?? item.Html;
				if (block.Rows != null)
					for (var r = 0; r < block.Rows.Count; r++)
						for (var c = 0; c < (block.Rows[r]?.Count ?? 0); c++)
							block.Rows[r][c] = Get(key, "cell." + r.ToString(CultureInfo.InvariantCulture) + "." + c.ToString(CultureInfo.InvariantCulture)) ?? block.Rows[r][c];
				if (block.Medication != null)
				{
					block.Medication.Indication = Get(key, "indication") ?? block.Medication.Indication;
					block.Medication.Notes = Get(key, "notes") ?? block.Medication.Notes;
				}
				foreach (var variant in block.Variants ?? new List<ProtocolVariant>())
					variant.Label = Get(variant.Key ?? string.Empty, "label") ?? variant.Label;
			}

			return copy;
		}
	}
}
