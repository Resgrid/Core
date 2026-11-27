using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Lms;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// A course's practice flashcards for the learner (Training T5, round 6F): the active cards of one of the member's enrollments whose source
	/// version is still in force (and, for a protocol card, a protocol the member may open). Read only; spaced practice runs on the client and is
	/// never posted back, graded or recorded. Empty while Enhanced AI or the LMS is off. Refused while the Trainings module is off. Omni would use
	/// this; Omni is not built here.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsFlashcardsController : V4AuthenticatedApiControllerbase
	{
		private readonly ILmsFlashcardService _flashcards;
		private readonly ILmsAccessService _access;

		public LmsFlashcardsController(ILmsFlashcardService flashcards, ILmsAccessService access)
		{
			_flashcards = flashcards;
			_access = access;
		}

		/// <summary>The practice cards of one of the member's enrollments.</summary>
		[HttpGet("GetDeck")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsFlashcardsResult>> GetDeck(int enrollmentId, CancellationToken cancellationToken = default)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return NotFound();

			var deck = await _flashcards.GetDeckAsync(DepartmentId, UserId, enrollmentId, cancellationToken);
			var result = new LmsFlashcardsResult
			{
				Data = deck.Select(c => new LmsFlashcardData
				{
					FlashcardId = c.Id,
					Front = c.Front,
					Back = c.Back,
					SourceTitle = c.SourceTitle,
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
