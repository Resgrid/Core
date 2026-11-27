using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.ProtocolContent;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// A protocol's practice flashcards for the member (round 7A, Enhanced AI only, no LMS): the active cards drafted from the protocol's version
	/// in force, for a protocol the member may read (the library's read rule) that is allowed to AI. Read only; spaced practice runs on the client
	/// and is never posted back, graded or recorded. Empty while Enhanced AI is off. Omni would use this; Omni is not built here.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class ProtocolFlashcardsController : V4AuthenticatedApiControllerbase
	{
		private readonly IProtocolFlashcardService _flashcards;

		/// <summary>The protocol flashcard service (Enhanced AI only).</summary>
		public ProtocolFlashcardsController(IProtocolFlashcardService flashcards)
		{
			_flashcards = flashcards;
		}

		/// <summary>The practice cards of a protocol the member may read.</summary>
		[HttpGet("GetDeck")]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ProtocolFlashcardsResult>> GetDeck(int protocolId, CancellationToken cancellationToken = default)
		{
			var deck = await _flashcards.GetDeckAsync(DepartmentId, UserId, protocolId, cancellationToken);
			var result = new ProtocolFlashcardsResult
			{
				Data = deck.Select(c => new ProtocolFlashcardData
				{
					FlashcardId = c.Id,
					Front = c.Front,
					Back = c.Back,
					SourceVersionLabel = c.SourceVersionLabel,
					SourceHeading = c.SourceHeading,
					SourcePath = c.SourceUrl
				}).ToList()
			};
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}
	}
}
