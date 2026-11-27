using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Academy;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Guidance;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// A3 in-product guidance for Omni (protocols-training-knowledge-plan §15): the member's role tour and the release's "What's new" lesson,
	/// both remembered per member, and the free keyword search over the Resgrid Academy and the product reference (no AI; "How do I…?"
	/// answers use Knowledge/Ask with ScopeKind 5, the Academy, when the department has knowledge answers). Every member may use these.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class GuidanceController : V4AuthenticatedApiControllerbase
	{
		private readonly IAcademyGuidanceService _guidance;

		public GuidanceController(IAcademyGuidanceService guidance)
		{
			_guidance = guidance;
		}

		private AcademyGuidanceActor Actor() => new AcademyGuidanceActor
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			Locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
			IsDepartmentAdmin = User.HasClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update),
			MayAssignTraining = User.HasClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update)
		};

		private static T Populate<T>(T result, int pageSize) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private static GuidanceTourData Tour(AcademyTourView tour) => tour == null ? null : new GuidanceTourData
		{
			Key = tour.Key,
			Title = tour.Title,
			Intro = tour.Intro,
			IsMine = tour.IsMine,
			Dismissed = tour.Dismissed,
			Completed = tour.Completed,
			Tasks = tour.Tasks.Select(t => new GuidanceTourTaskData { Key = t.Key, Title = t.Title, Hint = t.Hint, PageUrl = t.PageUrl, LessonUrl = t.LessonUrl, Done = t.Done }).ToList()
		};

		private static GuidanceWhatsNewData Release(AcademyWhatsNewView release) => release == null ? null : new GuidanceWhatsNewData
		{
			Release = release.Release,
			PublishedOn = release.PublishedOn,
			Title = release.Title,
			Summary = release.Summary,
			Minutes = release.Minutes,
			Dismissed = release.Dismissed,
			Items = release.Items.Select(i => new GuidanceWhatsNewItemData { Key = i.Key, Title = i.Title, Body = i.Body, PageUrl = i.PageUrl, LessonUrl = i.LessonUrl }).ToList()
		};

		/// <summary>The home card: the member's role tour while it is open, and the newest release lesson not yet read.</summary>
		[HttpGet("GetDashboard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GuidanceDashboardResult>> GetDashboard(CancellationToken cancellationToken)
		{
			var view = await _guidance.GetDashboardAsync(Actor(), DateTime.UtcNow, cancellationToken) ?? new AcademyGuidanceView();
			var result = new GuidanceDashboardResult();
			result.Data.Tour = Tour(view.Tour);
			result.Data.WhatsNew = Release(view.WhatsNew);
			return Ok(Populate(result, 1));
		}

		/// <summary>Every guided tour with the member's progress; their role's tour first.</summary>
		[HttpGet("GetTours")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GuidanceToursResult>> GetTours(CancellationToken cancellationToken)
		{
			var result = new GuidanceToursResult();
			result.Data = (await _guidance.GetToursAsync(Actor(), cancellationToken)).Select(Tour).ToList();
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>A release's "What's new" lesson (the newest when release is empty).</summary>
		[HttpGet("GetWhatsNew")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GuidanceWhatsNewResult>> GetWhatsNew(string release, CancellationToken cancellationToken)
		{
			var view = await _guidance.GetWhatsNewAsync(Actor(), release, cancellationToken);
			if (view == null)
				return NotFound();
			return Ok(Populate(new GuidanceWhatsNewResult { Data = Release(view) }, 1));
		}

		/// <summary>Marks one tour task done or not done for the member.</summary>
		[HttpPost("SetTourTask")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GuidanceSavedResult>> SetTourTask([FromBody] GuidanceTourTaskInput input, CancellationToken cancellationToken)
		{
			if (input == null || !await _guidance.SetTaskDoneAsync(Actor(), input.Tour, input.Task, input.Done, cancellationToken))
				return NotFound();
			return Ok(Populate(new GuidanceSavedResult { Data = true }, 1));
		}

		/// <summary>Closes a tour ("tour:…") or a release lesson ("whatsnew:…") for the member; it stays closed.</summary>
		[HttpPost("Dismiss")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GuidanceSavedResult>> Dismiss([FromBody] GuidanceDismissInput input, CancellationToken cancellationToken)
		{
			if (input == null || !await _guidance.DismissAsync(Actor(), input.Key, cancellationToken))
				return NotFound();
			return Ok(Populate(new GuidanceSavedResult { Data = true }, 1));
		}

		/// <summary>Free keyword search over the Academy lessons the department can see and the product reference (no AI).</summary>
		[HttpGet("Search")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<GuidanceSearchResult>> Search(string query, CancellationToken cancellationToken)
		{
			var found = await _guidance.SearchAsync(Actor(), query, cancellationToken) ?? new AcademyHelpSearchResult();
			var result = new GuidanceSearchResult();
			result.Data.Query = found.Query;
			GuidanceSearchHitData Hit(AcademyHelpHit h) => new GuidanceSearchHitData
			{
				Kind = h.Kind.ToString(), Id = h.Id, Title = h.Title, Excerpt = h.Excerpt, Url = h.Url, PageUrl = h.PageUrl, Minutes = h.Minutes
			};
			result.Data.Lessons = found.Lessons.Select(Hit).ToList();
			result.Data.Docs = found.Docs.Select(Hit).ToList();
			return Ok(Populate(result, result.Data.Lessons.Count + result.Data.Docs.Count));
		}
	}
}
