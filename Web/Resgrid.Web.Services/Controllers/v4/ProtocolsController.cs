using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Models.v4.Protocols;
using System.Collections.Generic;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The options for Protocols in the Resgrid system: the department's protocols, matching them to a call, and writing them
	/// (create, update in place, disable). Attachment bytes leave only through <see cref="GetFile"/>.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ProtocolsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IProtocolsService _protocolsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ICallsService _callsService;
		private readonly IEventAggregator _eventAggregator;

		public ProtocolsController(IProtocolsService protocolsService, IAuthorizationService authorizationService = null, ICallsService callsService = null, IEventAggregator eventAggregator = null)
		{
			_protocolsService = protocolsService;
			_authorizationService = authorizationService;
			_callsService = callsService;
			_eventAggregator = eventAggregator;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the department's enabled protocols (disabled and deleted ones are left out). No attachment bytes: a file comes
		/// from <see cref="GetFile"/>.
		/// </summary>
		[HttpGet("GetAllProtocols")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetAllProtocolsResult>> GetAllProtocols()
		{
			var result = new GetAllProtocolsResult();
			result.Data = new List<ProtocolResultData>();

			var protocols = await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId, true);

			if (protocols != null && protocols.Any())
			{
				foreach (var p in protocols)
				{
					result.Data.Add(ConvertProtocolData(p));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets a single protocol by id. A deleted protocol is still returned inside its department: calls that cite it keep
		/// showing it.
		/// </summary>
		[HttpGet("GetProtocol")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolResult>> GetProtocol(int protocolId)
		{
			var result = new GetProtocolResult();
			var protocol = await _protocolsService.GetProtocolByIdAsync(protocolId);

			if (protocol == null)
				return NotFound();

			if (protocol.DepartmentId != DepartmentId)
				return Unauthorized();

			result.Data = ConvertProtocolData(protocol);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		/// <summary>
		/// Gets a protocol attachment by its id: the one endpoint the bytes leave through. Unknown, or another department's, is a 404.
		/// </summary>
		/// <param name="protocolAttachmentId">ID of the protocol attachment</param>
		[HttpGet("GetFile")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult> GetFile(int protocolAttachmentId)
		{
			var attachment = await _protocolsService.GetAttachmentForDepartmentAsync(DepartmentId, protocolAttachmentId);

			if (attachment == null || attachment.Data == null)
				return NotFound();

			return File(attachment.Data, string.IsNullOrWhiteSpace(attachment.FileType) ? "application/octet-stream" : attachment.FileType, attachment.FileName);
		}

		/// <summary>
		/// Every enabled protocol evaluated for a call: the matcher the web call forms use (D18). Pass the call's priority id and
		/// its type (by id, or by name), and optionally the call's logged time in UTC (now when left out) or an existing call's id,
		/// whose logged time is then used and whose protocols are left out.
		/// </summary>
		[HttpGet("GetProtocolsForCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetProtocolsForCallResult>> GetProtocolsForCall(int priority, int? callTypeId = null, string type = null, DateTime? loggedOnUtc = null, int? callId = null)
		{
			var at = loggedOnUtc.HasValue ? (DateTime?)DateTime.SpecifyKind(loggedOnUtc.Value.Kind == DateTimeKind.Local ? loggedOnUtc.Value.ToUniversalTime() : loggedOnUtc.Value, DateTimeKind.Utc) : null;
			var onCall = new HashSet<int>();

			if (callId.HasValue && callId.Value > 0 && _callsService != null && _authorizationService != null && await _authorizationService.CanUserViewCallAsync(UserId, callId.Value))
			{
				var call = await _callsService.GetCallByIdAsync(callId.Value);
				if (call != null && call.DepartmentId == DepartmentId)
				{
					at = call.LoggedOn;
					onCall = new HashSet<int>((await _callsService.GetCallProtocolsByCallIdAsync(call.CallId) ?? new List<CallProtocol>()).Select(x => x.DispatchProtocolId));
				}
			}

			var context = await _protocolsService.BuildCallContextAsync(DepartmentId, priority, callTypeId, type, at);
			var matches = await _protocolsService.GetProtocolsForCallAsync(DepartmentId, context);

			var result = new GetProtocolsForCallResult();
			foreach (var match in matches.Where(x => !onCall.Contains(x.Protocol.DispatchProtocolId)))
			{
				result.Data.Add(new ProtocolCallMatchData
				{
					ProtocolId = match.Protocol.DispatchProtocolId.ToString(),
					Name = match.Protocol.Name,
					Code = match.Protocol.Code,
					MinimumWeight = match.Protocol.MinimumWeight,
					State = (int)match.State,
					MatchedTriggerId = match.MatchedTrigger?.DispatchProtocolTriggerId.ToString(),
					MatchedTriggerType = match.MatchedTrigger?.Type,
					Questions = ConvertQuestions(match.Protocol)
				});
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>Creates a protocol with its triggers, questions and answers.</summary>
		[HttpPost("SaveProtocol")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<ActionResult<SaveProtocolResult>> SaveProtocol([FromBody] ProtocolInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var protocol = await BuildAsync(input, null, true);
			var errors = ProtocolRules.Validate(protocol);
			errors.AddRange(await ValidateReferencesAsync(protocol, null));

			if (errors.Count > 0)
				return BadRequest(Refused(errors));

			protocol.DispatchProtocolId = 0;
			protocol.DepartmentId = DepartmentId;
			protocol.CreatedOn = DateTime.UtcNow;
			protocol.CreatedByUserId = UserId;
			protocol.UpdatedOn = DateTime.UtcNow;
			protocol.UpdatedByUserId = UserId;

			var saved = await _protocolsService.SaveProtocolAsync(protocol, cancellationToken);
			Audit(AuditLogTypes.ProtocolCreated, null, saved);

			return Ok(Saved(saved));
		}

		/// <summary>
		/// Updates a protocol in place: its triggers, questions and answers are matched by id (changed, added or removed), so ids
		/// stay stable. An id that is not one of this protocol's rows is refused (400).
		/// </summary>
		[HttpPost("UpdateProtocol")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<ActionResult<SaveProtocolResult>> UpdateProtocol([FromBody] ProtocolInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ProtocolId <= 0)
				return BadRequest();

			if (_authorizationService == null || !await _authorizationService.CanUserModifyProtocolAsync(UserId, input.ProtocolId))
				return Unauthorized();

			var current = await _protocolsService.GetProtocolByIdAsync(input.ProtocolId);
			if (current == null || current.DepartmentId != DepartmentId)
				return NotFound();

			var edited = await BuildAsync(input, current, false);
			edited.DispatchProtocolId = current.DispatchProtocolId;
			edited.DepartmentId = DepartmentId;

			var removeIds = (input.RemoveAttachmentIds ?? new List<int>()).Distinct().ToList();
			if (ProtocolRules.ForeignChildIds(current, edited).Count > 0 || removeIds.Any(x => current.Attachments.All(a => a.DispatchProtocolAttachmentId != x)))
				return BadRequest(Refused(new List<string> { "ErrorForeignRows" }));

			var errors = ProtocolRules.Validate(edited);
			errors.AddRange(await ValidateReferencesAsync(edited, current));
			if (errors.Count > 0)
				return BadRequest(Refused(errors));

			var wasDisabled = current.IsDisabled;
			var before = Snapshot(current);
			var saved = await _protocolsService.UpdateProtocolAsync(edited, removeIds, UserId, cancellationToken);
			Audit(AuditLogTypes.ProtocolUpdated, before, saved);
			if (wasDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, before, saved);

			return Ok(Saved(saved));
		}

		/// <summary>Disables (or enables) a protocol. A disabled protocol is never suggested, listed for the apps or attached.</summary>
		[HttpPost("SetDisabled")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<ActionResult<SaveProtocolResult>> SetDisabled([FromBody] SetProtocolDisabledInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ProtocolId <= 0)
				return BadRequest();

			if (_authorizationService == null || !await _authorizationService.CanUserModifyProtocolAsync(UserId, input.ProtocolId))
				return Unauthorized();

			var before = await _protocolsService.GetProtocolHeaderByIdAsync(input.ProtocolId);
			var beforeSnapshot = Snapshot(before);
			var wasDisabled = before?.IsDisabled ?? false;

			var saved = await _protocolsService.SetProtocolDisabledAsync(input.ProtocolId, input.Disabled, UserId, cancellationToken);
			if (saved == null)
				return NotFound();

			if (wasDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, beforeSnapshot, saved);

			return Ok(Saved(await _protocolsService.GetProtocolByIdAsync(saved.DispatchProtocolId) ?? saved));
		}

		private async Task<DispatchProtocol> BuildAsync(ProtocolInput input, DispatchProtocol current, bool isNew)
		{
			var types = _callsService == null ? new List<CallType>() : await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			return new DispatchProtocol
			{
				Name = input.Name?.Trim(),
				Code = ProtocolRules.NormalizeCode(input.Code),
				Description = input.Description,
				ProtocolText = input.ProtocolText,
				MinimumWeight = input.MinimumWeight,
				IsDisabled = input.IsDisabled,
				Triggers = (input.Triggers ?? new List<ProtocolTriggerInput>()).Where(x => x != null).Select(t =>
				{
					var needsType = t.Type == (int)ProtocolTriggerTypes.CallType || t.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;
					var type = needsType && t.CallTypeId.HasValue ? types.FirstOrDefault(x => x.CallTypeId == t.CallTypeId.Value) : null;
					var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == t.ProtocolTriggerId);

					return new DispatchProtocolTrigger
					{
						DispatchProtocolTriggerId = isNew ? 0 : t.ProtocolTriggerId,
						Type = t.Type,
						StartsOn = Utc(t.StartsOn),
						EndsOn = Utc(t.EndsOn),
						Priority = t.Type == (int)ProtocolTriggerTypes.CallType ? null : t.Priority,
						CallTypeId = needsType ? t.CallTypeId : null,
						// The type's current name, or the one the trigger kept when its type was deleted since.
						CallType = needsType ? type?.Type ?? (existing?.CallTypeId == t.CallTypeId ? existing?.CallType : null) : null
					};
				}).ToList(),
				Questions = (input.Questions ?? new List<ProtocolQuestionInput>()).Where(x => x != null).Select(q => new DispatchProtocolQuestion
				{
					DispatchProtocolQuestionId = isNew ? 0 : q.ProtocolQuestionId,
					Question = q.Question?.Trim(),
					Answers = (q.Answers ?? new List<ProtocolAnswerInput>()).Where(x => x != null).Select(a => new DispatchProtocolQuestionAnswer
					{
						DispatchProtocolQuestionAnswerId = isNew ? 0 : a.ProtocolQuestionAnswerId,
						Answer = a.Answer?.Trim(),
						Weight = a.Weight
					}).ToList()
				}).ToList()
			};
		}

		/// <summary>The call types and priorities the triggers name must be the department's (or what the trigger already had).</summary>
		private async Task<List<string>> ValidateReferencesAsync(DispatchProtocol protocol, DispatchProtocol current)
		{
			var errors = new List<string>();
			if (_callsService == null)
				return errors;

			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			foreach (var trigger in protocol.Triggers)
			{
				var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == trigger.DispatchProtocolTriggerId && trigger.DispatchProtocolTriggerId > 0);

				if (trigger.Priority.HasValue && !priorities.Any(x => x.DepartmentCallPriorityId == trigger.Priority.Value && !x.IsDeleted) && existing?.Priority != trigger.Priority)
					errors.Add("ErrorTriggerPriority");

				if (trigger.CallTypeId.HasValue && types.All(x => x.CallTypeId != trigger.CallTypeId.Value) && existing?.CallTypeId != trigger.CallTypeId)
					errors.Add("ErrorTriggerCallType");
			}

			return errors.Distinct().ToList();
		}

		private static DateTime? Utc(DateTime? value) =>
			value.HasValue ? DateTime.SpecifyKind(value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value, DateTimeKind.Utc) : null;

		private static SaveProtocolResult Refused(List<string> errors)
		{
			var result = new SaveProtocolResult { Errors = errors, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private static SaveProtocolResult Saved(DispatchProtocol protocol)
		{
			var result = new SaveProtocolResult { Data = ConvertProtocolData(protocol), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private static string Snapshot(DispatchProtocol protocol) => protocol == null ? null : Newtonsoft.Json.JsonConvert.SerializeObject(new
		{
			protocol.DispatchProtocolId,
			protocol.Name,
			protocol.Code,
			protocol.IsDisabled,
			protocol.MinimumWeight,
			Triggers = protocol.Triggers?.Count,
			Questions = protocol.Questions?.Count
		});

		private void Audit(AuditLogTypes type, string before, DispatchProtocol after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = type,
				Before = before,
				After = Snapshot(after),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request?.Headers["User-Agent"]} {Request?.Headers["Accept-Language"]}"
			});
		}

		private static List<ProtocolTriggerQuestionResult> ConvertQuestions(DispatchProtocol dp)
		{
			return (dp.Questions ?? Enumerable.Empty<DispatchProtocolQuestion>()).OrderBy(x => x.DispatchProtocolQuestionId).Select(q => new ProtocolTriggerQuestionResult
			{
				ProtocolTriggerQuestionId = q.DispatchProtocolQuestionId.ToString(),
				Question = q.Question,
				Answers = (q.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).OrderBy(x => x.DispatchProtocolQuestionAnswerId).Select(a => new ProtocolQuestionAnswerResult
				{
					ProtocolQuestionAnswerId = a.DispatchProtocolQuestionAnswerId.ToString(),
					Answer = a.Answer,
					Weight = a.Weight
				}).ToList()
			}).ToList();
		}

		public static ProtocolResultData ConvertProtocolData(DispatchProtocol dp)
		{
			var protocol = new ProtocolResultData();
			protocol.ProtocolId = dp.DispatchProtocolId.ToString();
			protocol.DepartmentId = dp.DepartmentId;
			protocol.Name = dp.Name;
			protocol.Code = dp.Code;
			protocol.IsDisabled = dp.IsDisabled;
			protocol.Description = dp.Description;
			protocol.ProtocolText = dp.ProtocolText;
			protocol.CreatedOn = dp.CreatedOn;
			protocol.CreatedByUserId = dp.CreatedByUserId;
			protocol.UpdatedOn = dp.UpdatedOn;
			protocol.MinimumWeight = dp.MinimumWeight;
			protocol.UpdatedByUserId = dp.UpdatedByUserId;
			protocol.State = (int)dp.State;
			protocol.Triggers = new List<ProtocolTriggerResult>();
			protocol.Attachments = new List<ProtocolTriggerAttachmentResult>();
			protocol.Questions = ConvertQuestions(dp);

			foreach (var t in dp.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>())
			{
				var trigger = new ProtocolTriggerResult();
				trigger.ProtocolTriggerId = t.DispatchProtocolTriggerId.ToString();
				trigger.Type = t.Type;
				trigger.StartsOn = t.StartsOn;
				trigger.EndsOn = t.EndsOn;
				trigger.Priority = t.Priority;
				trigger.CallType = t.CallType;
				trigger.CallTypeId = t.CallTypeId;
				trigger.Geofence = t.Geofence;

				protocol.Triggers.Add(trigger);
			}

			foreach (var a in dp.Attachments ?? Enumerable.Empty<DispatchProtocolAttachment>())
			{
				var attachment = new ProtocolTriggerAttachmentResult();
				attachment.ProtocolTriggerAttachmentId = a.DispatchProtocolAttachmentId.ToString();
				attachment.FileName = a.FileName;
				attachment.FileType = a.FileType;
				attachment.Size = a.Size;

				protocol.Attachments.Add(attachment);
			}

			return protocol;
		}
	}
}
