using System.Collections.Generic;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Protocols;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against dispatch protocols that are established in a department
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class ProtocolsController : V3AuthenticatedApiControllerbase
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IProtocolsService _protocolsService;

		public ProtocolsController(IDepartmentsService departmentsService, IProtocolsService protocolsService)
		{
			_departmentsService = departmentsService;
			_protocolsService = protocolsService;
		}

		/// <summary>
		/// Get's all the protocols for a department
		/// </summary>
		/// <returns>List of ProtocolResult objects.</returns>
		[HttpGet("GetAllProtocols")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<ProtocolResult>>> GetAllProtocols()
		{
			var results = new List<ProtocolResult>();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);


			var protocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId);

			foreach (var p in protocols)
			{
				results.Add(ProtocolResult.Convert(p));
			}

			return Ok(results);
		}

		/// <summary>
		/// Get's all the protocols for a department
		/// </summary>
		/// <returns>List of ProtocolResult objects.</returns>
		[HttpGet("GetProtocol")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult> GetProtocol(int protocolId)
		{
			var protocol = await _protocolsService.GetProtocolByIdAsync(protocolId);

			if (protocol == null)
				return NotFound();

			if (protocol.DepartmentId != DepartmentId)
				return Unauthorized();

			var result = ProtocolResult.Convert(protocol);


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
			var result = Ok();

			var attachment = await _protocolsService.GetAttachmentByIdAsync(protocolAttachmentId);

			if (attachment == null)
				return NotFound();

			var protocol = await _protocolsService.GetProtocolByIdAsync(attachment.DispatchProtocolId);

			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return Unauthorized();

			return File(attachment.Data, attachment.FileType);
		}
	}
}
