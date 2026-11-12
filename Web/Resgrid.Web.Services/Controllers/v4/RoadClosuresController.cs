using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.RoadClosures;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Road closures (M0314): the department's drawn closed and slowed roads, for maps in the apps. Dispatch ranking and the
	/// Routing endpoints already apply them; closures are managed on the web (Mapping → Road Closures).
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RoadClosuresController : V4AuthenticatedApiControllerbase
	{
		private readonly IRoadClosureService _roadClosureService;

		public RoadClosuresController(IRoadClosureService roadClosureService)
		{
			_roadClosureService = roadClosureService;
		}

		/// <summary>The department's road closures in effect now and still to come (activeOnly: in effect now only).</summary>
		[HttpGet("GetRoadClosures")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetRoadClosuresResult>> GetRoadClosures([FromQuery] bool activeOnly = false)
		{
			var now = DateTime.UtcNow;
			var result = new GetRoadClosuresResult();

			foreach (var closure in await _roadClosureService.GetClosuresForDepartmentAsync(DepartmentId))
			{
				var state = closure.StateAt(now);
				if (state == RoadClosureStates.Ended || state == RoadClosureStates.Disabled)
					continue;

				if (activeOnly && state != RoadClosureStates.Active)
					continue;

				var geometry = RoadClosureShape.Normalize(closure.GeometryJson);

				result.Data.Add(new RoadClosureResultData
				{
					RoadClosureId = closure.RoadClosureId,
					Name = closure.Name,
					ClosureType = closure.ClosureType,
					Severity = closure.Severity,
					SpeedPercent = closure.SpeedPercent,
					AppliesTo = (int)closure.AppliesToModes,
					State = (int)state,
					IsEnabled = closure.IsEnabled,
					Geometry = geometry == null ? null : JToken.Parse(geometry),
					Windows = closure.Windows.Select(w => new RoadClosureWindowData { StartsOn = w.StartsOn, EndsOn = w.EndsOn }).ToList(),
					Reason = closure.Reason,
					ClosedBy = closure.ClosedBy,
					Authority = closure.Authority,
					ContactName = closure.ContactName,
					ContactDetails = closure.ContactDetails,
					Notes = closure.Notes,
					UpdatedOn = closure.UpdatedOn
				});
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}
	}
}
