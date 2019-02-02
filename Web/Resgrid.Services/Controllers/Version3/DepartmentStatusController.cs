using System.Linq;
using Resgrid.Model.Services;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Attributes;
using Resgrid.Web.Services.Controllers.Version3.Models.Departments;
using System.Net.Http;
using System.Net;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// General department level options
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class DepartmentStatusController : V3AuthenticatedApiControllerbase
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;

		public DepartmentStatusController(IDepartmentSettingsService departmentSettingsService, IDepartmentsService departmentsService)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
		}

		[AcceptVerbs("GET")]
		[NoCache]
		public DepartmentStatusResult GetDepartmentStatus()
		{
			var result = new DepartmentStatusResult();

			result.Lup = _departmentSettingsService.GetDepartmentUpdateTimestamp(DepartmentId);
			result.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId).Select(x => x.UserId.ToString()).ToList();

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