using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3;
using Resgrid.Web.Services.Controllers.Version3.Models.Departments;

namespace Resgrid.Web.Services.Controllers.v3
{
	/// <summary>
	/// General department level options
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class DepartmentStatusController : V3AuthenticatedApiControllerbase
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;

		public DepartmentStatusController(IDepartmentSettingsService departmentSettingsService, IDepartmentsService departmentsService)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
		}

		/// <summary>
		/// Gets the department status.
		/// </summary>
		/// <returns>DepartmentStatusResult.</returns>
		[HttpGet("GetDepartmentStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DepartmentStatusResult>> GetDepartmentStatus()
		{
			var result = new DepartmentStatusResult();

			result.Lup = await _departmentSettingsService.GetDepartmentUpdateTimestampAsync(DepartmentId);
			result.Users = (await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId)).Select(x => x.UserId.ToString()).ToList();

			return result;
		}
	}
}
