namespace Resgrid.Web.Services.Controllers.Version3.Models.Health
{
	/// <summary>
	/// Response for getting the health of the Resgrid API.
	/// </summary>
	public class HealthResult
	{
		/// <summary>
		/// Site\Location of this API
		/// </summary>
		public string SiteId { get; set; }

		/// <summary>
		/// The Version of the Services
		/// </summary>
		public string ServicesVersion { get; set; }

		/// <summary>
		/// Gets the current API version
		/// </summary>
		public string ApiVersion { get; set; }

		/// <summary>
		/// Can the API services talk to the database
		/// </summary>
		public bool DatabaseOnline { get; set; }

		/// <summary>
		/// Can the API services talk to the cache
		/// </summary>
		public bool CacheOnline { get; set; }
	}
}
