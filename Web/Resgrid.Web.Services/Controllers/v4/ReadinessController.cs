using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Checklists;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class ReadinessController : V4AuthenticatedApiControllerbase
	{
		private readonly IReadinessAccessService _access;
		public ReadinessController(IReadinessAccessService access) => _access = access;

		/// <summary>Independent feature availability and monthly offers. A flag alone is not a paid entitlement.</summary>
		[HttpGet("GetAccess")]
		public async Task<ActionResult<ReadinessAccessResult>> GetAccess()
		{
			var result = new ReadinessAccessResult
			{
				Data = new ReadinessAccessData
				{
					ChecklistsEnabled = await _access.CanUseChecklistsAsync(DepartmentId),
					MaintenanceEnabled = await _access.CanUseMaintenanceAsync(DepartmentId)
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
