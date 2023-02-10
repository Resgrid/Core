using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Protocols;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The options for Protocols in the Resgrid system
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ProtocolsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IProtocolsService _protocolsService;

		public ProtocolsController(IProtocolsService protocolsService)
		{
			_protocolsService = protocolsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all protocols for a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllProtocols")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetAllProtocolsResult>> GetAllProtocols()
		{
			var result = new GetAllProtocolsResult();
			result.Data = new List<ProtocolResultData>();

			var protocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId);

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
		/// Gets a single protocol by id
		/// </summary>
		/// <returns>List of ProtocolResult objects.</returns>
		[HttpGet("GetProtocol")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
			protocol.Questions = new List<ProtocolTriggerQuestionResult>();

			foreach (var t in dp.Triggers)
			{
				var trigger = new ProtocolTriggerResult();
				trigger.ProtocolTriggerId = t.DispatchProtocolTriggerId.ToString();
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
				var attachment = new ProtocolTriggerAttachmentResult();
				attachment.ProtocolTriggerAttachmentId = a.DispatchProtocolAttachmentId.ToString();
				attachment.FileName = a.FileName;
				attachment.FileType = a.FileType;

				protocol.Attachments.Add(attachment);
			}

			foreach (var q in dp.Questions)
			{
				var question = new ProtocolTriggerQuestionResult();
				question.ProtocolTriggerQuestionId = q.DispatchProtocolQuestionId.ToString();
				question.Question = q.Question;
				question.Answers = new List<ProtocolQuestionAnswerResult>();

				foreach (var a in q.Answers)
				{
					var answer = new ProtocolQuestionAnswerResult();
					answer.ProtocolQuestionAnswerId = a.DispatchProtocolQuestionAnswerId.ToString();
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
