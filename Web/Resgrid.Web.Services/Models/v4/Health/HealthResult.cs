namespace Resgrid.Web.Services.Models.v4.Health
{
	/// <summary>
	/// Result of the Health check API
	/// </summary>
	public class HealthResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public HealthResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public HealthResult()
		{
			Data = new HealthResultData();
		}
	}

	/// <summary>
	/// Health check data for the current state of the api server handling the request
	/// </summary>
	public class HealthResultData
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

		/// <summary>Search host enabled in this process (SearchConfig.Enabled).</summary>
		public bool SearchEnabled { get; set; }

		/// <summary>A local reader is open for the global index (after a pull or a write).</summary>
		public bool SearchOnline { get; set; }

		/// <summary>Documents in this process's copy of the global and records indexes.</summary>
		public int SearchIndexDocCount { get; set; }
	}
}
