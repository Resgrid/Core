using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department setting 123 (OfflineMapPacksConfig): what the apps download for offline use
	/// (maplibre-mapping-routing-navigation-plan.md §19.3). A department with no saved value gets the documented defaults
	/// and the fallback response area. Members default to 0/false so protobuf keeps them (true and non-zero initializers
	/// lose their false/0 values), hence the "Off"/"Exclude" wording.
	/// </summary>
	[ProtoContract]
	public class OfflineMapPacksConfig
	{
		public const int DefaultRefreshHours = 24;
		public const int DefaultStaleDays = 7;
		public const int DefaultIncidentRadiusKm = 3;
		public const int DefaultCorridorBufferMeters = 500;

		/// <summary>
		/// The maximum response area as a GeoJSON Polygon, MultiPolygon, Feature or FeatureCollection string. Empty uses the
		/// fallback circle around the map centre, department address or first station.
		/// </summary>
		[ProtoMember(1)]
		public string ResponseAreaGeoJson { get; set; }

		[ProtoMember(2)]
		public bool ExcludeCustomMaps { get; set; }

		[ProtoMember(3)]
		public bool ExcludeIndoorMaps { get; set; }

		[ProtoMember(4)]
		public bool ExcludeLayers { get; set; }

		[ProtoMember(5)]
		public bool ExcludePois { get; set; }

		[ProtoMember(6)]
		public bool ExcludeHydrants { get; set; }

		/// <summary><see cref="OfflineDownloadPolicy"/>: 0 Wi-Fi only (default), 1 any network, 2 manual.</summary>
		[ProtoMember(7)]
		public int DownloadPolicy { get; set; }

		[ProtoMember(8)]
		public bool IncidentPacksOff { get; set; }

		/// <summary>Kilometres around a call outside the area; 0 is <see cref="DefaultIncidentRadiusKm"/>.</summary>
		[ProtoMember(9)]
		public int IncidentRadiusKm { get; set; }

		[ProtoMember(10)]
		public bool CorridorPacksOff { get; set; }

		/// <summary>Metres either side of a route outside the area; 0 is <see cref="DefaultCorridorBufferMeters"/>.</summary>
		[ProtoMember(11)]
		public int CorridorBufferMeters { get; set; }

		/// <summary>Hours between manifest refreshes; 0 is <see cref="DefaultRefreshHours"/>.</summary>
		[ProtoMember(12)]
		public int RefreshHours { get; set; }

		/// <summary>Days before downloaded maps show as stale; 0 is <see cref="DefaultStaleDays"/>.</summary>
		[ProtoMember(13)]
		public int StaleDays { get; set; }

		/// <summary>Use the downloaded base map inside the area even when online (D16).</summary>
		[ProtoMember(14)]
		public bool PreferLocalInArea { get; set; }

		/// <summary>
		/// The base-map max zoom the saved area was accepted at; kept when a new archive makes the area larger than the
		/// limit, so a re-measure never silently drops coverage (§19.3). 0 is the install maximum.
		/// </summary>
		[ProtoMember(15)]
		public int AcceptedMaxZoom { get; set; }

		public int EffectiveRefreshHours() => RefreshHours > 0 ? System.Math.Min(RefreshHours, 24 * 30) : DefaultRefreshHours;

		public int EffectiveStaleDays() => StaleDays > 0 ? System.Math.Min(StaleDays, 365) : DefaultStaleDays;

		public double EffectiveIncidentRadiusKm() => IncidentRadiusKm > 0 ? System.Math.Min(IncidentRadiusKm, 25) : DefaultIncidentRadiusKm;

		public double EffectiveCorridorBufferMeters() => CorridorBufferMeters > 0 ? System.Math.Min(CorridorBufferMeters, 5000) : DefaultCorridorBufferMeters;
	}

	/// <summary>When the apps download offline packs (<see cref="OfflineMapPacksConfig.DownloadPolicy"/>).</summary>
	public enum OfflineDownloadPolicy
	{
		WifiOnly = 0,
		AnyNetwork = 1,
		Manual = 2
	}
}
