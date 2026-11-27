using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
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
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.ProtocolContent;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Protocols P3 for Omni (the older apps get no client change; their v4 GetProtocol keeps returning ProtocolText): the offline manifest
	/// by hash, the staleness answer ("v3 viewed, v4 effective since …"), a version in the reader's language (approved translation or the
	/// source) with its steps, and step-through on a call (the incident SOP checklists included). Every read applies the protocol read
	/// rule (R-P4); call writes need the right to edit the call.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ProtocolContentController : V4AuthenticatedApiControllerbase
	{
		private readonly IProtocolOfflineService _offline;
		private readonly IProtocolStepThroughService _steps;
		private readonly IProtocolTranslationService _translations;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly IAiKnowledgeService _knowledge;
		private readonly IAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;
		private readonly IProtocolDocumentService _documents;

		public ProtocolContentController(IProtocolOfflineService offline, IProtocolStepThroughService steps, IProtocolTranslationService translations,
			IProtocolAccessService access, IProtocolLibraryService library, IAiKnowledgeService knowledge, IAuthorizationService authorization,
			IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer, IProtocolDocumentService documents)
		{
			_offline = offline;
			_steps = steps;
			_translations = translations;
			_access = access;
			_library = library;
			_knowledge = knowledge;
			_authorization = authorization;
			_localizer = localizer;
			_documents = documents;
		}

		/// <summary>The offline manifest: the readable protocols in force with their version hash, the SHA-256 of each file and the approved translations.</summary>
		[HttpGet("GetOfflineManifest")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolOfflineManifestResult>> GetOfflineManifest()
		{
			var manifest = await _offline.GetManifestAsync(DepartmentId, UserId);
			var result = new GetProtocolOfflineManifestResult { Data = manifest, PageSize = manifest.Items.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Whether the version (or the content hash) a device shows is still the one in force, with the sentence to show when it is not.</summary>
		[HttpGet("GetStaleness")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolStalenessResult>> GetStaleness(int protocolId, int? protocolVersionId = null, string contentHash = null)
		{
			var answer = await _offline.GetStalenessAsync(DepartmentId, UserId, protocolId, protocolVersionId, contentHash);
			if (answer == null)
				return NotFound();

			if (answer.IsStale)
			{
				var viewed = answer.ViewedLabel == null ? "-" : "v" + answer.ViewedLabel;
				answer.Message = answer.IsRetired
					? string.Format(_localizer["StaleRetiredMessage"].Value, viewed)
					: string.Format(_localizer["StaleVersionMessage"].Value, viewed, "v" + answer.CurrentLabel,
						answer.CurrentEffectiveOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-");
			}

			var result = new GetProtocolStalenessResult { Data = answer, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>A version as the reader is shown it: the approved translation in <paramref name="locale"/>, else the source; with its steps.</summary>
		[HttpGet("GetVersionContent")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolVersionContentResult>> GetVersionContent(int protocolVersionId, string locale = null)
		{
			var version = await _library.GetVersionAsync(DepartmentId, protocolVersionId);
			var protocol = version == null ? null : await _access.GetReadableAsync(DepartmentId, UserId, version.DispatchProtocolId);
			if (version == null || protocol == null)
				return NotFound();

			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (!ProtocolAccessRules.CanReadVersion(reader, protocol, await _library.GetAudienceAsync(DepartmentId, protocol.DispatchProtocolId), version))
				return NotFound();

			var localized = await _translations.GetLocalizedAsync(DepartmentId, version, locale);
			string dosing = null;
			try { dosing = (await _knowledge.GetSettingsAsync(DepartmentId))?.DosingToolUrl; } catch (Exception ex) { Logging.LogException(ex, "The dosing tool link could not be read."); }

			var data = new ProtocolVersionContentData
			{
				ProtocolId = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
				ProtocolVersionId = version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
				Label = version.Label,
				EffectiveOn = version.EffectiveOn,
				ContentHash = version.ContentHash,
				Locale = localized.Locale,
				TranslationId = localized.Translation?.ProtocolVersionTranslationId.ToString(CultureInfo.InvariantCulture),
				TranslationContentHash = localized.Translation?.ContentHash,
				IsFallback = localized.IsFallback,
				AvailableLocales = localized.AvailableLocales,
				ContentHtml = version.IsDocument ? null : ProtocolContentDocument.SanitizeForOutput(localized.ContentHtml),
				ContentJson = version.IsDocument ? null : localized.ContentJson,
				DosingToolUrl = dosing,
				DeepLink = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Protocols/View/" + protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
				Steps = version.IsDocument ? new List<ProtocolStepResultData>() : ProtocolSteps.Extract(localized.ContentJson, localized.ContentHtml).Select(s => Step(s, null)).ToList(),
				IsDocument = version.IsDocument
			};

			if (version.IsDocument)
			{
				// Round 10: the file's name, type and size for the reader's download button (metadata only; the bytes leave through GetVersionFile).
				var file = await _documents.GetFileInfoAsync(DepartmentId, version.ProtocolVersionId);
				if (file != null && file.ProtocolVersionId == version.ProtocolVersionId)
				{
					data.DocumentFileName = ProtocolDocumentRules.SafeFileName(file.FileName);
					data.DocumentContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
					data.DocumentSize = file.Size;
				}
			}

			var result = new GetProtocolVersionContentResult { Data = data, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The call's step-through checklists, each step with its state, time and who.</summary>
		[HttpGet("GetCallChecklists")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetCallProtocolChecklistsResult>> GetCallChecklists(int callId)
		{
			if (!await _authorization.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			var result = new GetCallProtocolChecklistsResult();
			foreach (var checklist in await _steps.GetCallChecklistsAsync(DepartmentId, callId))
				result.Data.Add(Checklist(callId, checklist));
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The incident SOP library: Procedure protocols in force the member may read and start on a call (MAYDAY, RIT, hazmat entry).</summary>
		[HttpGet("GetSopLibrary")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetSopLibraryResult>> GetSopLibrary()
		{
			var result = new GetSopLibraryResult();
			foreach (var protocol in await _steps.GetSopLibraryAsync(DepartmentId, UserId))
				result.Data.Add(new SopLibraryItemData
				{
					ProtocolId = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
					Code = protocol.Code,
					Name = protocol.Name,
					VersionId = protocol.CurrentVersionId?.ToString(CultureInfo.InvariantCulture)
				});
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Starts a protocol (an incident SOP checklist) on a call; attached as a manual pick when it is not on the call yet.</summary>
		[HttpPost("StartCallChecklist")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<CallProtocolChecklistResult>> StartCallChecklist([FromBody] StartCallChecklistInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.CallId <= 0 || input.ProtocolId <= 0)
				return BadRequest();
			if (!await _authorization.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			return Outcome(input.CallId, await _steps.StartAsync(DepartmentId, input.CallId, input.ProtocolId, UserId, cancellationToken));
		}

		/// <summary>One step event (state 1 done, 2 skipped, 3 not applicable, 0 back to pending), timestamped by the server.</summary>
		[HttpPost("RecordCallStep")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<CallProtocolChecklistResult>> RecordCallStep([FromBody] RecordCallStepInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.CallId <= 0 || input.CallProtocolId <= 0 || string.IsNullOrWhiteSpace(input.StepId))
				return BadRequest();
			if (!await _authorization.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			return Outcome(input.CallId, await _steps.RecordStepAsync(DepartmentId, input.CallId, input.CallProtocolId, input.StepId, (ProtocolStepStates)input.State,
				input.Note, input.DeviceTimestamp, UserId, cancellationToken, input.ClientStepId));
		}

		[HttpPost("CompleteCallChecklist")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<CallProtocolChecklistResult>> CompleteCallChecklist([FromBody] CompleteCallChecklistInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.CallId <= 0 || input.CallProtocolId <= 0)
				return BadRequest();
			if (!await _authorization.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			return Outcome(input.CallId, await _steps.CompleteAsync(DepartmentId, input.CallId, input.CallProtocolId, UserId, cancellationToken));
		}

		private ActionResult<CallProtocolChecklistResult> Outcome(int callId, ProtocolStepResult outcome)
		{
			var result = new CallProtocolChecklistResult
			{
				Data = outcome.Checklist == null ? null : Checklist(callId, outcome.Checklist),
				Error = outcome.Succeeded ? null : outcome.Error.ToString(),
				PageSize = outcome.Checklist == null ? 0 : 1,
				Status = outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);

			switch (outcome.Error)
			{
				case ProtocolStepErrors.None:
					return Ok(result);
				case ProtocolStepErrors.NotFound:
					return NotFound(result);
				case ProtocolStepErrors.NotAuthorized:
					return StatusCode(StatusCodes.Status403Forbidden, result);
				case ProtocolStepErrors.Conflict:
				case ProtocolStepErrors.Completed:
					return Conflict(result);
				default:
					return BadRequest(result);
			}
		}

		private static CallProtocolChecklistResultData Checklist(int callId, ProtocolCallChecklist checklist) => new CallProtocolChecklistResultData
		{
			CallId = callId.ToString(CultureInfo.InvariantCulture),
			CallProtocolId = checklist.Row.CallProtocolId.ToString(CultureInfo.InvariantCulture),
			ProtocolId = checklist.Protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
			Code = checklist.Protocol.Code,
			Name = checklist.Protocol.Name,
			Kind = checklist.Protocol.Kind,
			IsIncidentSop = checklist.IsIncidentSop,
			ProtocolVersionId = checklist.Version?.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
			VersionLabel = checklist.Version?.Label,
			StartedOn = checklist.Log?.StartedOn,
			StartedByUserId = checklist.Log?.StartedByUserId,
			CompletedOn = checklist.Log?.CompletedOn,
			CompletedByUserId = checklist.Log?.CompletedByUserId,
			Done = checklist.DoneCount,
			Total = checklist.Steps.Count,
			Steps = checklist.Steps.Select(s => Step(s.Step, s)).ToList()
		};

		private static ProtocolStepResultData Step(ProtocolStepDefinition step, ProtocolStepView view) => new ProtocolStepResultData
		{
			Id = step.Id,
			Text = step.Text,
			Kind = step.Kind,
			Group = step.Group,
			Anchor = step.Anchor,
			Order = step.Order,
			State = (int)(view?.State ?? ProtocolStepStates.Pending),
			At = view?.Last?.At,
			ByUserId = view?.Last?.UserId,
			Note = view?.Last?.Note
		};
	}
}
