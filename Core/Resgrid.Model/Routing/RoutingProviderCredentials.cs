namespace Resgrid.Model
{
	/// <summary>
	/// A department's own account for one routing provider (Department → Mapping Settings → your own map providers), decrypted
	/// for use. Passed to <see cref="Providers.IRoutingProvider.WithCredentials"/>; never sent to clients or logged.
	/// </summary>
	public class RoutingProviderCredentials
	{
		public int DepartmentId { get; set; }

		/// <summary>A <see cref="RoutingProviderNames"/> value.</summary>
		public string Provider { get; set; }

		/// <summary>Base URL for Valhalla or OSRM. Must be a public https address; the server never connects to a private one for a department.</summary>
		public string Url { get; set; }

		/// <summary>Mapbox token, Google key or Valhalla key.</summary>
		public string ApiKey { get; set; }

		/// <summary>Valhalla only: "apikey" (X-Api-Key) or "bearer".</summary>
		public string AuthScheme { get; set; }

		/// <summary>Google only: "legacy" (Directions / Distance Matrix) or "routes" (Routes API).</summary>
		public string GoogleRoutingApi { get; set; }
	}
}
