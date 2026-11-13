using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// A department's own map provider accounts, decrypted for server-side use (setting 125). A null member means the
	/// system default applies for that part.
	/// </summary>
	public class DepartmentMapProviders
	{
		public static readonly DepartmentMapProviders None = new DepartmentMapProviders();

		/// <summary>The department's routing account, tried before the system routing chain; null = system only.</summary>
		public RoutingProviderCredentials Routing { get; set; }

		/// <summary>google, mapbox or locationiq, tried before the system geocoding chain; null = system only.</summary>
		public string GeocodingProvider { get; set; }

		public string GeocodingApiKey { get; set; }

		/// <summary>The department's live traffic layer; null = the system layer (if any).</summary>
		public TrafficLayerSource Traffic { get; set; }
	}

	/// <summary>A live traffic tile layer for MapLibre maps (web kit and apps).</summary>
	public class TrafficLayerSource
	{
		public const string VectorType = "vector";
		public const string RasterType = "raster";

		/// <summary>Tile URL template with {z}, {x} and {y}.</summary>
		public string Url { get; set; }

		/// <summary><see cref="VectorType"/> (Mapbox Traffic v1, source-layer "traffic") or <see cref="RasterType"/>.</summary>
		public string Type { get; set; }

		/// <summary>True when the layer comes from the department's own account.</summary>
		public bool IsDepartment { get; set; }
	}

	/// <summary>
	/// What a department administrator submits for its own map providers. A blank key keeps the stored one (keys are never
	/// shown back); <see cref="RoutingProvider"/> etc. empty switches that part back to the system default and drops its keys.
	/// </summary>
	public class DepartmentMapProvidersUpdate
	{
		public string RoutingProvider { get; set; }
		public string RoutingUrl { get; set; }
		public string RoutingApiKey { get; set; }
		public bool ClearRoutingApiKey { get; set; }
		public string RoutingAuthScheme { get; set; }
		public string GoogleRoutingApi { get; set; }

		public string TrafficSource { get; set; }
		public string TrafficMapboxToken { get; set; }
		public string TrafficTileUrl { get; set; }

		public string GeocodingProvider { get; set; }
		public string GeocodingApiKey { get; set; }
	}

	/// <summary>Why a <see cref="DepartmentMapProvidersUpdate"/> was refused (localized by the Mapping Settings page).</summary>
	public enum DepartmentMapProvidersError
	{
		UnknownRoutingProvider = 0,
		RoutingKeyRequired = 1,
		RoutingUrlRequired = 2,
		/// <summary>Not an https URL, or its host is not on a public address.</summary>
		RoutingUrlNotPublic = 3,
		UnknownTrafficSource = 4,
		/// <summary>Missing, or not a public (pk.) Mapbox token.</summary>
		TrafficMapboxTokenInvalid = 5,
		/// <summary>Not an https URL with {z}, {x} and {y}.</summary>
		TrafficTileUrlInvalid = 6,
		UnknownGeocodingProvider = 7,
		GeocodingKeyRequired = 8
	}

	public class DepartmentMapProvidersSaveResult
	{
		public bool Saved { get; set; }
		public List<DepartmentMapProvidersError> Errors { get; set; } = new List<DepartmentMapProvidersError>();
	}
}
