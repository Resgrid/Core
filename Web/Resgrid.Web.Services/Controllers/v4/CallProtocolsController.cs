using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.CallPriorities;
using System.Linq;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.CallProtocols;
using System.Collections.Generic;
using System;
using System.Net.Mime;
using System.Threading;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallProtocolsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IProtocolsService _protocolsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IEventAggregator _eventAggregator;

		public CallProtocolsController(IProtocolsService protocolsService, IAuthorizationService authorizationService = null, IEventAggregator eventAggregator = null)
		{
			_protocolsService = protocolsService;
			_authorizationService = authorizationService;
			_eventAggregator = eventAggregator;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the call protocols in a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllCallProtocols")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<CallProtocolsResult>> GetAllCallProtocols()
		{
			var result = new CallProtocolsResult();

			// Enabled protocols only (D19); never an attachment's bytes.
			var priorities = await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId, true);

			if (priorities != null && priorities.Any())
			{
				foreach (var p in priorities)
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
		/// Gets a single protocol by id
		/// </summary>
		/// <returns>List of ProtocolResult objects.</returns>
		[HttpGet("GetProtocol")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<CallProtocolResult>> GetProtocol(int protocolId)
		{
			var result = new CallProtocolResult();
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
		/// Gets a protocol attachment by it's id
		/// </summary>
		/// <param name="protocolAttachmentId">ID of the protocol attachment</param>
		/// <returns></returns>
		[HttpGet("GetFile")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult> GetFile(int protocolAttachmentId)
		{
			// The same department-checked download as Protocols/GetFile; unknown or another department's is a 404.
			var attachment = await _protocolsService.GetAttachmentForDepartmentAsync(DepartmentId, protocolAttachmentId);

			if (attachment == null || attachment.Data == null)
				return NotFound();

			return File(attachment.Data, string.IsNullOrWhiteSpace(attachment.FileType) ? "application/octet-stream" : attachment.FileType, attachment.FileName);
		}

		/// <summary>
		/// Takes a protocol off a call, with a reason. The row stays on the call, marked removed with who, when and why, and the
		/// removal is written to the department's audit log.
		/// </summary>
		[HttpPost("RemoveCallProtocol")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<RemoveCallProtocolResult>> RemoveCallProtocol([FromBody] RemoveCallProtocolInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.CallId <= 0 || input.CallProtocolId <= 0 || string.IsNullOrWhiteSpace(input.Reason))
				return BadRequest();

			if (_authorizationService == null || !await _authorizationService.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			var removed = await _protocolsService.RemoveProtocolFromCallAsync(DepartmentId, input.CallId, input.CallProtocolId, input.Reason, UserId, cancellationToken);
			if (removed == null)
				return NotFound();

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.CallProtocolRemoved,
				After = Newtonsoft.Json.JsonConvert.SerializeObject(new { removed.CallId, removed.CallProtocolId, removed.DispatchProtocolId, Code = removed.Data, removed.RemovedReason, removed.RemovedOn }),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request?.Headers["User-Agent"]} {Request?.Headers["Accept-Language"]}"
			});

			var result = new RemoveCallProtocolResult { Id = removed.CallProtocolId.ToString(), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// The protocol as a call's protocol: its definition and, from the call's row, how it came there and the answers
		/// recorded when it was attached.
		/// </summary>
		public static CallProtocolResultData ConvertProtocolData(DispatchProtocol dp, CallProtocol row)
		{
			var protocol = ConvertProtocolData(dp);
			if (row == null)
				return protocol;

			protocol.CallProtocolId = row.CallProtocolId.ToString();
			protocol.Source = row.Source;
			protocol.ProtocolVersionId = row.ProtocolVersionId?.ToString();
			protocol.ContentHash = row.ContentHash;
			protocol.Score = row.Score;
			protocol.Determinant = row.Determinant;
			protocol.OverrideReason = row.OverrideReason;
			protocol.Case = CallProtocolCase.Parse(row.CaseJson);

			if (!string.IsNullOrWhiteSpace(row.AnswersJson))
			{
				try
				{
					var answers = Newtonsoft.Json.JsonConvert.DeserializeObject<CallProtocolAnswers>(row.AnswersJson);
					protocol.RecordedAnswers = answers?.Answers?.Select(a => new CallProtocolAnswerResultData { Question = a.Question, Answer = a.Answer, Weight = a.Weight }).ToList();
				}
				catch (Newtonsoft.Json.JsonException)
				{
					protocol.RecordedAnswers = null;
				}
			}

			return protocol;
		}

		public static CallProtocolResultData ConvertProtocolData(DispatchProtocol dp)
		{
			var protocol = new CallProtocolResultData();
			protocol.Id = dp.DispatchProtocolId.ToString();
			protocol.DepartmentId = dp.DepartmentId.ToString();
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
			protocol.Triggers = new List<ProtocolTriggerResultData>();
			protocol.Attachments = new List<ProtocolTriggerAttachmentResultData>();
			protocol.Questions = new List<ProtocolTriggerQuestionResultData>();

			foreach (var t in dp.Triggers)
			{
				var trigger = new ProtocolTriggerResultData();
				trigger.Id = t.DispatchProtocolTriggerId.ToString();
				trigger.Type = t.Type;
				trigger.StartsOn = t.StartsOn;
				trigger.EndsOn = t.EndsOn;
				trigger.Priority = t.Priority;
				trigger.CallType = t.CallType;
				trigger.CallTypeId = t.CallTypeId;
				trigger.Geofence = t.Geofence;
				trigger.GeofenceGroupId = t.GeofenceGroupId;
				trigger.DaysOfWeek = t.DaysOfWeek;
				trigger.StartMinute = t.StartMinute;
				trigger.EndMinute = t.EndMinute;
				trigger.Keywords = t.Keywords;
				trigger.PoiTypeId = t.PoiTypeId;
				trigger.CallSources = t.CallSources;

				protocol.Triggers.Add(trigger);
			}

			foreach (var a in dp.Attachments)
			{
				var attachment = new ProtocolTriggerAttachmentResultData();
				attachment.Id = a.DispatchProtocolAttachmentId.ToString();
				attachment.FileName = a.FileName;
				attachment.FileType = a.FileType;
				attachment.Size = a.Size;

				protocol.Attachments.Add(attachment);
			}

			foreach (var q in dp.Questions)
			{
				var question = new ProtocolTriggerQuestionResultData();
				question.Id = q.DispatchProtocolQuestionId.ToString();
				question.Question = q.Question;
				question.Answers = new List<ProtocolQuestionAnswerResultData>();

				foreach (var a in q.Answers)
				{
					var answer = new ProtocolQuestionAnswerResultData();
					answer.Id = a.DispatchProtocolQuestionAnswerId.ToString();
					answer.Answer = a.Answer;
					answer.Weight = a.Weight;

					question.Answers.Add(answer);
				}

				protocol.Questions.Add(question);
			}


			return protocol;
		}
	}
}
