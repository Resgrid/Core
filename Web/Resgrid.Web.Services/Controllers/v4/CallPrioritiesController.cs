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

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallPrioritiesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;

		public CallPrioritiesController(
			ICallsService callsService
			)
		{
			_callsService = callsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the call priorities in a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllCallPriorites")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallPrioritiesResult>> GetAllCallPriorites()
		{
			var result = new CallPrioritiesResult();

			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId);

			if (priorities != null && priorities.Any())
			{
				foreach (var p in priorities)
				{
					result.Data.Add(ConvertPriorityData(p));
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

		public static CallPriorityResultData ConvertPriorityData(DepartmentCallPriority p)
		{
			var priority = new CallPriorityResultData();

			priority.Id = p.DepartmentCallPriorityId;
			priority.DepartmentId = p.DepartmentId;
			priority.Name = StringHelpers.SanitizeHtmlInString(p.Name);
			priority.Color = p.Color;
			priority.Sort = p.Sort;
			priority.IsDeleted = p.IsDeleted;
			priority.IsDefault = p.IsDefault;
			priority.Tone = p.Tone;

			return priority;
		}
	}
}
