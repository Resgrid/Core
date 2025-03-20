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

		public CallProtocolsController(IProtocolsService protocolsService)
		{
			_protocolsService = protocolsService;
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

			var priorities = await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId);

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
		public async Task<ActionResult> GetFile(int protocolAttachmentId)
		{
			var attachment = await _protocolsService.GetAttachmentByIdAsync(protocolAttachmentId);

			if (attachment == null)
				return NotFound();

			var protocol = await _protocolsService.GetProtocolByIdAsync(attachment.DispatchProtocolId);

			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return Unauthorized();

			return File(attachment.Data, attachment.FileType);
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
				trigger.Geofence = t.Geofence;

				protocol.Triggers.Add(trigger);
			}

			foreach (var a in dp.Attachments)
			{
				var attachment = new ProtocolTriggerAttachmentResultData();
				attachment.Id = a.DispatchProtocolAttachmentId.ToString();
				attachment.FileName = a.FileName;
				attachment.FileType = a.FileType;

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
