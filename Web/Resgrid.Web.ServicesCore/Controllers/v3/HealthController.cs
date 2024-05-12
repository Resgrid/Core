using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Health;

namespace Resgrid.Web.Services.Controllers.v3
{
	/// <summary>
	/// Health Check system to get information and health status of the services
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	[ApiController]
	[AllowAnonymous]
	public class HealthController: Controller
	{
		#region Members and Constructors
		private readonly IHealthService _healthService;
		private readonly IWebHostEnvironment _env;

		public HealthController(
			IHealthService healthService,
			IWebHostEnvironment env
			)
		{
			_healthService = healthService;
			_env = env;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns all the call priorities (including deleted ones) for a department
		/// </summary>
		/// <returns>Array of CallPriorityResult objects for each call priority in the department</returns>
		[AllowAnonymous]
		[HttpGet("GetCurrent")]
		public async Task<HealthResult> GetCurrent()
		{
			var result = new HealthResult();

			result.ServicesVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
			result.ApiVersion = "v3";
			result.SiteId = "0";
			result.CacheOnline = _healthService.IsCacheProviderConnected();

			var dbTime = await _healthService.GetDatabaseTimestamp();

			if (!string.IsNullOrWhiteSpace(dbTime))
				result.DatabaseOnline = true;
			else
				result.DatabaseOnline = false;

			return result;
		}

		//[AllowAnonymous]
		//public HttpResponseMessage Options()
		//{
		//	var response = new HttpResponseMessage();
		//	response.StatusCode = HttpStatusCode.OK;
		//	response.Headers.Add("Access-Control-Allow-Origin", "*");
		//	response.Headers.Add("Access-Control-Request-Headers", "*");
		//	response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

		//	return response;
		//}
	}
}
