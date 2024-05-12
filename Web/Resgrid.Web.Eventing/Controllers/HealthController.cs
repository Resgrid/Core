using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Web.Eventing.Models;

namespace Resgrid.Web.Eventing.Controllers
{
	/// <summary>
	/// Health Check system to get information and health status of the services
	/// </summary>
	[AllowAnonymous]
	[Route("health/")]
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

		[HttpGet("GetCurrent")]
		public async Task<IActionResult> GetCurrent()
		{
			var result = new HealthResult();

			result.WebsiteVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
			result.SiteId = "0";
			result.CacheOnline = false;//_healthService.IsCacheProviderConnected();
			result.ServiceBusOnline = false;//_healthService.IsCacheProviderConnected();

			//var dbTime = await _healthService.GetDatabaseTimestamp();

			//if (!string.IsNullOrWhiteSpace(dbTime))
			//	result.DatabaseOnline = true;
			//else
				result.DatabaseOnline = false;

			return Json(result);
		}
	}
}
