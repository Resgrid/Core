namespace Resgrid.WebCore.Models
{
	/// <summary>
	/// Response for getting the health of the Resgrid Website.
	/// </summary>
	public class HealthResult
	{
		/// <summary>
		/// Site\Location of this API
		/// </summary>
		public string SiteId { get; set; }

		/// <summary>
		/// The Version of the Website
		/// </summary>
		public string WebsiteVersion { get; set; }

		/// <summary>
		/// Can the API services talk to the database
		/// </summary>
		public bool DatabaseOnline { get; set; }

		/// <summary>
		/// Can the API services talk to the cache
		/// </summary>
		public bool CacheOnline { get; set; }

		/// <summary>
		/// Can the API services talk to the service bus
		/// </summary>
		public bool ServiceBusOnline { get; set; }
	}
}
