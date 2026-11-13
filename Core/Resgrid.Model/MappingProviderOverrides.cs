using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department setting 125 (MappingProviderOverrides): a department's own map provider accounts, used instead of the
	/// install's (Department → Mapping Settings → Your own map providers). Empty provider = the system default. Keys and
	/// key-bearing URLs are stored encrypted per department (IEncryptionService.EncryptForDepartment) and only decrypted
	/// server-side by IDepartmentMapProvidersService; the Mapbox traffic token is public (pk.) and stored as is.
	/// </summary>
	[ProtoContract]
	public class MappingProviderOverrides
	{
		/// <summary>Routing for routes, drive times, route plans and navigation: "" (system), mapbox, google, valhalla or osrm.</summary>
		[ProtoMember(1)]
		public string RoutingProvider { get; set; }

		/// <summary>Valhalla or OSRM base URL (public https only).</summary>
		[ProtoMember(2)]
		public string RoutingUrl { get; set; }

		/// <summary>Encrypted Mapbox token, Google key or Valhalla key.</summary>
		[ProtoMember(3)]
		public string RoutingApiKeyEncrypted { get; set; }

		/// <summary>Valhalla: "apikey" (X-Api-Key) or "bearer".</summary>
		[ProtoMember(4)]
		public string RoutingAuthScheme { get; set; }

		/// <summary>Google: "legacy" (Directions / Distance Matrix) or "routes" (Routes API).</summary>
		[ProtoMember(5)]
		public string GoogleRoutingApi { get; set; }

		/// <summary>Live traffic layer: "" (system), "mapbox" (Mapbox Traffic with the department's token) or "tiles" (a raster tile URL).</summary>
		[ProtoMember(6)]
		public string TrafficSource { get; set; }

		/// <summary>Public (pk.) Mapbox token for Mapbox Traffic; it is sent to browsers and apps.</summary>
		[ProtoMember(7)]
		public string TrafficMapboxToken { get; set; }

		/// <summary>Encrypted raster traffic tile URL template with {z}, {x} and {y} (may carry the provider's key).</summary>
		[ProtoMember(8)]
		public string TrafficTileUrlEncrypted { get; set; }

		/// <summary>Address lookups: "" (system), google, mapbox or locationiq.</summary>
		[ProtoMember(9)]
		public string GeocodingProvider { get; set; }

		/// <summary>Encrypted geocoding key.</summary>
		[ProtoMember(10)]
		public string GeocodingApiKeyEncrypted { get; set; }

		public const string TrafficSourceMapbox = "mapbox";
		public const string TrafficSourceTiles = "tiles";

		/// <summary>True when nothing overrides the system defaults.</summary>
		public bool IsEmpty =>
			string.IsNullOrWhiteSpace(RoutingProvider) && string.IsNullOrWhiteSpace(TrafficSource) && string.IsNullOrWhiteSpace(GeocodingProvider);
	}
}
