using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Departments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// General department level options
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class DepartmentController : V3AuthenticatedApiControllerbase
	{
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;

		public DepartmentController(
			ICallsService callsService,
			IDepartmentGroupsService departmentGroupsService,
			IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService
			)
		{
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
		}

		/// <summary>
		/// Returns all the available responding options (Calls/Stations) for the department
		/// </summary>
		/// <returns>Array of RespondingOptionResult objects for each responding option in the department</returns>
		[HttpGet("GetRespondingOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<RespondingOptionResult>>> GetRespondingOptions()
		{
			var result = new List<RespondingOptionResult>();

			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);

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

		/// <summary>
		/// Returns basic high level department information for the request department
		/// </summary>
		/// <param name="departmentId"></param>
		/// <returns>Array of DepartmentResult objects with the department data populated</returns>
		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<DepartmentResult>>> Get(int departmentId)
		{
			List<DepartmentResult> result = new List<DepartmentResult>();

			if (departmentId == 0 && IsSystem)
			{
				// Get All
				var departments = await _departmentsService.GetAllAsync();

				foreach (var department in departments)
				{
					result.Add(await DepartmentToResult(department));
				}

				return Ok(result);
			}
			else if (departmentId >= 1)
			{
				if (departmentId == DepartmentId)
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
					result.Add(await DepartmentToResult(department));

					return Ok(result);
				}
				else
				{
					return Unauthorized();
				}
			}
			else
			{
				return BadRequest();
			}
		}


		/// <summary>
		/// Returns basic high level department information for the request department
		/// </summary>
		/// <param name="departmentId"></param>
		/// <returns>Array of DepartmentResult objects with the department data populated</returns>
		[HttpGet("GetDepartmentOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<DepartmentOptionResult>>> GetDepartmentOptions(int departmentId)
		{
			List<DepartmentOptionResult> result = new List<DepartmentOptionResult>();

			if (departmentId == 0 && IsSystem)
			{
				// Get All
				var departments = await _departmentsService.GetAllAsync();

				foreach (var department in departments)
				{
					var emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(department.DepartmentId);

					var optionsResult = new DepartmentOptionResult();
					optionsResult.DepartmentId = department.DepartmentId;
					optionsResult.EmailFormatType = emailSettings.FormatType;
					result.Add(optionsResult);
				}

				return result;
			}
			else if (departmentId >= 1)
			{
				if (departmentId == DepartmentId)
				{
					var emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(departmentId);

					var optionsResult = new DepartmentOptionResult();
					optionsResult.DepartmentId = departmentId;
					optionsResult.EmailFormatType = emailSettings.FormatType;
					result.Add(optionsResult);

					return result;
				}
				else
				{
					return Unauthorized();
				}
			}
			else
			{
				return BadRequest();
			}
		}


		private async Task<DepartmentResult> DepartmentToResult(Department department)
		{
			var departmentResult = new DepartmentResult();
			departmentResult.Id = department.DepartmentId;
			departmentResult.Name = department.Name;
			departmentResult.ManagingUserId = department.ManagingUserId;
			departmentResult.AddressId = department.AddressId;
			departmentResult.DepartmentType = department.DepartmentType;
			departmentResult.TimeZone = department.TimeZone;
			departmentResult.CreatedOn = department.CreatedOn;
			departmentResult.UpdatedOn = department.UpdatedOn;
			departmentResult.Use24HourTime = department.Use24HourTime;
			departmentResult.AdminUsers = department.AdminUsers;

			if (department.Members != null && department.Members.Any())
				departmentResult.Members = department.Members.Select(x => x.UserId).ToList();
			else
				departmentResult.Members = new List<string>();

			departmentResult.EmailCode = await _departmentSettingsService.GetDispatchEmailForDepartmentAsync(department.DepartmentId);

			return departmentResult;
		}
	}
}
