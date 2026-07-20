using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.IncidentCommand;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Anonymous, token-scoped public incident status feed. It exposes only records explicitly marked Public and only
	/// while the Incident Commander/PIO has public sharing enabled. Disabling sharing revokes the token immediately.
	/// </summary>
	[AllowAnonymous]
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class PublicIncidentController : ControllerBase
	{
		private readonly IIncidentCommandService _incidentCommandService;

		public PublicIncidentController(IIncidentCommandService incidentCommandService)
		{
			_incidentCommandService = incidentCommandService;
		}

		[HttpGet("Get/{publicShareToken}")]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<PublicIncidentInformationResult>> Get(string publicShareToken)
		{
			var information = await _incidentCommandService.GetPublicInformationAsync(publicShareToken);
			if (information == null)
				return NotFound();

			var result = new PublicIncidentInformationResult
			{
				Data = information,
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("Download/{publicShareToken}/{incidentAttachmentId}")]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> Download(string publicShareToken, string incidentAttachmentId)
		{
			var attachment = await _incidentCommandService.GetPublicAttachmentAsync(publicShareToken, incidentAttachmentId);
			if (attachment?.Data == null)
				return NotFound();

			return File(attachment.Data, attachment.ContentType ?? MediaTypeNames.Application.Octet, attachment.FileName);
		}
	}
}
