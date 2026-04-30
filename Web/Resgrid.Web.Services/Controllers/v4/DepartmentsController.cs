using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Departments;
using System;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Department-level lookup operations used by external integrations such as the SMTP relay
	/// to resolve dispatch codes to departments.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class DepartmentsController : V4AuthenticatedApiControllerbaseSystemAuth
	{
		#region Members and Constructors
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;

		public DepartmentsController(IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Resolves a dispatch email code to the department it belongs to.
		/// Used by the SMTP relay in single-department or hosted multi-department mode
		/// when the department cannot be determined from the email domain alone.
		/// </summary>
		/// <param name="code">The dispatch email code (local part of the email address) to look up.</param>
		/// <returns>DepartmentResult with the matching department, or a not-found response.</returns>
		[HttpGet("GetDepartmentByDispatchCode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<DepartmentResult>> GetDepartmentByDispatchCode(string code)
		{
			var result = new DepartmentResult();

			if (String.IsNullOrWhiteSpace(code))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			var departmentId = await _departmentSettingsService.GetDepartmentIdForDispatchEmailAsync(code);

			if (!departmentId.HasValue || departmentId.Value <= 0)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			// In SystemApiKey mode, the relay is authorized to look up any department.
			// In OAuth mode, validate that the token's department matches the resolved department.
			if (!IsSystemApiKeyRequest && departmentId.Value != DepartmentId)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId.Value, false);

			if (department == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Data.DepartmentId = department.DepartmentId.ToString();
			result.Data.Name = department.Name;
			result.Data.Code = department.Code;

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
