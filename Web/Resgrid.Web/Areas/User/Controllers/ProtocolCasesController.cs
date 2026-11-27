using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Protocols P4 case flow on the web call forms and View Call: the department's case flows for the protocol panel, instruction audio
	/// (Piper TTS), the Enhanced AI protocol suggestion for a call, the determinant override after the call is saved (reason required) and
	/// the caller follow-up text over the call's intake SMS thread. Attaching a protocol with its case happens when the call form is saved.
	/// </summary>
	[Area("User")]
	public class ProtocolCasesController : SecureBaseController
	{
		private readonly IProtocolCaseService _cases;
		private readonly IProtocolAiSuggestionService _ai;
		private readonly IAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;

		public ProtocolCasesController(IProtocolCaseService cases, IProtocolAiSuggestionService ai, IAuthorizationService authorization,
			IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer)
		{
			_cases = cases;
			_ai = ai;
			_authorization = authorization;
			_localizer = localizer;
		}

		/// <summary>
		/// The case flows of the department's published Dispatch protocols (graph per protocol, version in force), and whether AI suggestions
		/// are available for a saved call. The call forms' protocol panel runs them inline; the server re-walks them on save.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> Definitions(int? callId = null)
		{
			var definitions = await _cases.GetDefinitionsAsync(DepartmentId);
			var aiAvailable = callId.HasValue && callId.Value > 0 && await _authorization.CanUserEditCallAsync(UserId, callId.Value) && await _ai.IsAvailableAsync(DepartmentId);

			// Newtonsoft, so the graph has the shape it is stored and edited in (ContentJson "graph": nodes, key, root, kind...).
			var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
			{
				aiAvailable,
				protocols = definitions.Select(d => new
				{
					protocolId = d.ProtocolId,
					versionId = d.ProtocolVersionId,
					code = d.Code,
					name = d.Name,
					version = d.VersionLabel,
					graph = d.Graph
				})
			}, new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore, StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.EscapeHtml });
			return Content(json, "application/json");
		}

		/// <summary>The audio of an instruction step marked "speak" (Piper TTS, dispatch speaking rate) as URLs to play in order.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> InstructionAudio(int protocolId, string node, CancellationToken cancellationToken = default)
		{
			var urls = await _cases.GetInstructionAudioAsync(DepartmentId, protocolId, node, cancellationToken);
			return Json(new { urls = urls.Select(u => u.ToString()) });
		}

		/// <summary>Asks Enhanced AI for a protocol and prefilled answers for a saved call (Update Call); the dispatcher confirms by saving.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Suggest(int callId, CancellationToken cancellationToken = default)
		{
			if (callId <= 0 || !await _authorization.CanUserEditCallAsync(UserId, callId))
				return Unauthorized();

			var result = await _ai.SuggestAsync(DepartmentId, callId, UserId, cancellationToken);
			if (result.Outcome != ProtocolAiSuggestionOutcomes.Suggested || result.Suggestion == null)
				return Json(new { success = false, message = Text("AiSuggest" + result.Outcome) });

			return Json(new
			{
				success = true,
				suggestionId = result.Suggestion.ProtocolAiSuggestionId,
				protocolId = result.Suggestion.DispatchProtocolId,
				code = result.ProtocolCode,
				name = result.ProtocolName,
				confidence = result.Suggestion.Confidence,
				rationale = result.Suggestion.Rationale,
				answers = result.Suggestion.Answers
			});
		}

		/// <summary>Overrides the determinant of a protocol on a saved call (a reason is required; a blank determinant restores the computed one).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Override(int callId, int callProtocolId, string determinant, string reason, CancellationToken cancellationToken = default)
		{
			if (!await _authorization.CanUserEditCallAsync(UserId, callId))
				return Unauthorized();

			var result = await _cases.OverrideDeterminantAsync(DepartmentId, callId, callProtocolId, determinant, reason, UserId, cancellationToken);
			TempData["CallProtocolMessage"] = "Case" + result.Outcome;
			return RedirectToAction("ViewCall", "Dispatch", new { area = "User", callId });
		}

		/// <summary>Texts a case instruction to the caller over the call's intake SMS thread (text only; logged on the call).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> SendToCaller(int callId, int callProtocolId, string node, CancellationToken cancellationToken = default)
		{
			if (!await _authorization.CanUserEditCallAsync(UserId, callId))
				return Unauthorized();

			var result = await _cases.SendCallerInstructionAsync(DepartmentId, callId, callProtocolId, node, UserId, cancellationToken);
			TempData["CallProtocolMessage"] = "CaseCaller" + result.Outcome;
			return RedirectToAction("ViewCall", "Dispatch", new { area = "User", callId });
		}

		private string Text(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound ? key : value.Value;
		}
	}
}
