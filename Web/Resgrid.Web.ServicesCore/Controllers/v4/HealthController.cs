using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Web.Services.Models.v4.Health;
using System.Reflection;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class HealthController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IHealthService _healthService;

		public HealthController(IHealthService healthService)
		{
			_healthService = healthService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the current users department rights
		/// </summary>
		/// <returns>DepartmentRightsResult object with the department rights and group memberships</returns>
		[HttpGet("GetCurrent")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[AllowAnonymous]
		public async Task<HealthResult> GetCurrent()
		{
			var result = new HealthResult();

			try
			{
				result.Data.ServicesVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
				result.Data.ApiVersion = "v4";
				result.Data.SiteId = "0";
				result.Data.CacheOnline = _healthService.IsCacheProviderConnected();

				var dbTime = await _healthService.GetDatabaseTimestamp();

				if (!string.IsNullOrWhiteSpace(dbTime))
					result.Data.DatabaseOnline = true;
				else
					result.Data.DatabaseOnline = false;

				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			catch (System.Exception)
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}
	}
}
