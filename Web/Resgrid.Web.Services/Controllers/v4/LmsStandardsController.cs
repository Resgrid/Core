using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Lms;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Training T5 standards for Omni (LMS add-on, dark behind Training.Lms): a fresh launch of a SCORM or cmi5 lesson (the token-bound
	/// content URL on the web site, which the app loads in a sandboxed web view) or the web launch page of an LTI lesson. The launch runs the
	/// learner service's checks (gate, lapse rules, lesson order) and fails closed. Omni itself is out of scope for this build.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsStandardsController : V4AuthenticatedApiControllerbase
	{
		private readonly ILmsAccessService _access;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsStandardsRuntimeService _runtime;

		public LmsStandardsController(ILmsAccessService access, ILmsLearnerService learner, ILmsStandardsRuntimeService runtime)
		{
			_access = access;
			_learner = learner;
			_runtime = runtime;
		}

		/// <summary>Opens a standards lesson of the member's enrollment (a new launch each call; SCORM / cmi5 resume from the stored data).</summary>
		[HttpGet("GetLaunch")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsStandardsLaunchResult>> GetLaunch(int enrollmentId, int lessonId, string item = null, CancellationToken cancellationToken = default)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId) || !await _access.IsFlagEnabledAsync(DepartmentId))
				return NotFound();

			var result = new LmsStandardsLaunchResult();
			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, enrollmentId, lessonId);
			var lesson = player?.Lesson;
			if (player == null || player.Error != LmsErrors.None || lesson == null || lesson.LmsLessonId != lessonId)
			{
				result.Error = (player == null || player.Error == LmsErrors.None ? LmsErrors.LessonLocked : player.Error).ToString();
				return Ok(Populate(result));
			}

			var site = LmsStandardsUrls.SiteBase;
			if (lesson.Kind == (int)LmsLessonKinds.LtiLink)
			{
				result.Data = new LmsStandardsLaunchData
				{
					Kind = LmsLessonKinds.LtiLink.ToString(),
					Url = site + "/User/LmsStandards/Launch?id=" + enrollmentId + "&lesson=" + lessonId
				};
				var registration = await _runtime.GetRegistrationAsync(DepartmentId, enrollmentId, lessonId);
				result.Data.Satisfied = registration?.Satisfied == true;
				result.Data.ScorePercent = registration?.ScorePercent;
				return Ok(Populate(result));
			}
			if (lesson.Kind != (int)LmsLessonKinds.Scorm && lesson.Kind != (int)LmsLessonKinds.Cmi5)
				return NotFound();

			var launch = await _runtime.LaunchAsync(DepartmentId, UserId, enrollmentId, lessonId, item, site, cancellationToken);
			if (!launch.Succeeded)
			{
				result.Error = launch.Error.ToString();
				return Ok(Populate(result));
			}
			result.Data = new LmsStandardsLaunchData
			{
				Kind = ((LmsLessonKinds)lesson.Kind).ToString(),
				Url = site + launch.Url,
				ExpiresOn = launch.ExpiresOn,
				ItemIdentifier = launch.Item?.Identifier,
				Items = launch.Items.Select(i => new LmsStandardsItemData { Identifier = i.Identifier, Title = i.Title, Satisfied = launch.SatisfiedItems.Contains(i.Identifier) }).ToList(),
				Satisfied = launch.Registration?.Satisfied == true,
				ScorePercent = launch.Registration?.ScorePercent
			};
			return Ok(Populate(result));
		}

		private static LmsStandardsLaunchResult Populate(LmsStandardsLaunchResult result)
		{
			result.PageSize = result.Data == null ? 0 : 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
