using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Services;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Controllers.Version3.Models.Departments;
using System.Net.Http;
using System.Net;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// General department level options
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class DepartmentController : V3AuthenticatedApiControllerbase
	{
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public DepartmentController(
			ICallsService callsService, IDepartmentGroupsService departmentGroupsService, IDepartmentSettingsService departmentSettingsService)
		{
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_departmentSettingsService = departmentSettingsService;
		}

		/// <summary>
		/// Returns all the available responding options (Calls/Stations) for the department
		/// </summary>
		/// <returns>Array of RespondingOptionResult objects for each responding option in the department</returns>
		[AcceptVerbs("GET")]
		public List<RespondingOptionResult> GetRespondingOptions()
		{
			var result = new List<RespondingOptionResult>();

			var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);
			var calls = _callsService.GetActiveCallsByDepartment(DepartmentId);

			/* Removed the Parallel.ForEach statement here as this data needs to be sorted
			 * and there isn't enough sort data (at least for calls) for the client to do it.
			 * Also during JMeter testing the perf gains wernt there (10ms average for both).
			 */

			foreach (var s in stations.OrderBy(x => x.Name))
			{
				var respondingTo = new RespondingOptionResult();
				respondingTo.Id = s.DepartmentGroupId;
				respondingTo.Typ = 1;
				respondingTo.Nme = "";

				result.Add(respondingTo);
			}

			foreach (var c in calls.OrderByDescending(x => x.LoggedOn))
			{
				var respondingTo = new RespondingOptionResult();
				respondingTo.Id = c.CallId;
				respondingTo.Typ = 2;
				respondingTo.Nme = c.Name;

				result.Add(respondingTo);
			}

			return result;
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}