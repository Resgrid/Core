using Resgrid.Model.Services;
using System.Web.Http.Cors;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Hosting;
using System.Web.Http;
using Resgrid.Web.Services.Controllers.Version3.Models.Health;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Health Check system to get information and health status of the services
	/// </summary>
	[System.Web.Mvc.AllowAnonymous]
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class HealthController: ApiController
	{
		#region Members and Constructors
		private readonly IHealthService _healthService;

		public HealthController(
			IHealthService healthService
			)
		{
			_healthService = healthService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns all the call priorities (including deleted ones) for a department
		/// </summary>
		/// <returns>Array of CallPriorityResult objects for each call priority in the department</returns>
		[System.Web.Mvc.AllowAnonymous]
		[System.Web.Http.AcceptVerbs("GET")]
		public async Task<HealthResult> GetCurrent()
		{
			var result = new HealthResult();
			result.ServicesVersion = AssemblyName
				.GetAssemblyName(HostingEnvironment.ApplicationPhysicalPath + "bin\\Resgrid.Web.Services.dll").Version.ToString();
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

		[System.Web.Mvc.AllowAnonymous]
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
