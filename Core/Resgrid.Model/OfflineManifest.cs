using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The offline manifest (maplibre-mapping-routing-navigation-plan.md §19.3), in the shape Omni's client validates
	/// (packages/maps/src/offline/manifest.ts). The service fills it without URLs; the API signs and adds them.
	/// </summary>
	public class OfflineManifest
	{
		public string ManifestVersion { get; set; }

		public string Attribution { get; set; }

		public OfflineManifestPolicy Policy { get; set; } = new OfflineManifestPolicy();

		public OfflineManifestStyles Styles { get; set; }

		public List<OfflineManifestPack> Packs { get; set; } = new List<OfflineManifestPack>();

		public OfflineManifestFile DepartmentData { get; set; }

		public OfflineManifestSprite Sprite { get; set; }

		public List<OfflineManifestFloor> IndoorFloors { get; set; } = new List<OfflineManifestFloor>();
	}

	public class OfflineManifestPolicy
	{
		public bool WifiOnly { get; set; }
		public int RefreshHours { get; set; }
		public int StaleDays { get; set; }
		public bool PreferLocalInAo { get; set; }
		public long AmbientCacheMaxBytes { get; set; }
		public long MaxPackBytes { get; set; }
		public long ResponseAreaMaxBytes { get; set; }
		/// <summary>Downloads only when the user asks (policy "manual"); Omni ignores unknown fields.</summary>
		public bool ManualOnly { get; set; }
	}

	public class OfflineManifestStyles
	{
		public OfflineManifestFile Day { get; set; }
		public OfflineManifestFile Night { get; set; }
		public OfflineManifestFile Glyphs { get; set; }
	}

	public class OfflineManifestFile
	{
		public string Url { get; set; }
		public string Version { get; set; }
		public string Sha256 { get; set; }
		public long? SizeBytes { get; set; }

		/// <summary>Not serialized to clients: the store key or resource the API signs a URL for.</summary>
		[Newtonsoft.Json.JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
		public string ResourceKey { get; set; }
	}

	public class OfflineManifestSpriteFile : OfflineManifestFile
	{
		public string Name { get; set; }
	}

	public class OfflineManifestSprite
	{
		public string Version { get; set; }
		public List<OfflineManifestSpriteFile> Files { get; set; } = new List<OfflineManifestSpriteFile>();

		[Newtonsoft.Json.JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
		public string PackId { get; set; }
	}

	public class OfflineManifestPack
	{
		public string PackId { get; set; }
		public string PackKey { get; set; }
		public int Kind { get; set; }
		public string Name { get; set; }
		/// <summary>[west, south, east, north].</summary>
		public double[] Bounds { get; set; }
		public int MinZoom { get; set; }
		public int MaxZoom { get; set; }
		public long? SizeBytes { get; set; }
		public string Sha256 { get; set; }
		public string Version { get; set; }
		public int Status { get; set; }
		public string DownloadUrl { get; set; }
		public string ExpiresUtc { get; set; }
		public string CustomMapLayerId { get; set; }

		/// <summary>
		/// Incident and corridor packs (kinds 1 and 2): the active calls whose radius touches the cell, so a device keeps
		/// the packs of the calls it works on. A cell is shared, so it can list several calls, and also route plans
		/// whichever kind it carries. Null for other kinds.
		/// </summary>
		public List<int> CallIds { get; set; }

		/// <summary>Incident and corridor packs (kinds 1 and 2): the active route plans whose corridor touches the cell. Null for other kinds.</summary>
		public List<string> RoutePlanIds { get; set; }
	}

	public class OfflineManifestFloor
	{
		public string FloorId { get; set; }
		public string Name { get; set; }
		public string ImageUrl { get; set; }
		/// <summary>[[lon,lat] top-left, top-right, bottom-right, bottom-left]; null for an image with no position.</summary>
		public double[][] Corners { get; set; }
		public string Version { get; set; }
		public string Sha256 { get; set; }
		public long? SizeBytes { get; set; }

		/// <summary>True for a custom map layer image (not an indoor floor); decides which signed image route serves it.</summary>
		[Newtonsoft.Json.JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
		public bool IsCustomMapLayer { get; set; }
	}

	/// <summary>Size of a response area's base map (EstimateResponseArea, §19.3).</summary>
	public class OfflineResponseAreaEstimate
	{
		public bool Valid { get; set; }

		/// <summary>Why the area cannot be used ("invalid", "self-intersecting", "too-large-area", "too-large", "no-archive").</summary>
		public string Error { get; set; }

		public long SizeBytes { get; set; }

		public long MaxBytes { get; set; }

		public bool Fits { get; set; }

		public int CellCount { get; set; }

		public int MaxZoom { get; set; }

		public double AreaSquareKm { get; set; }

		/// <summary>True when the size comes from the base map archive; false when no archive is configured (size unknown).</summary>
		public bool Measured { get; set; }
	}

	/// <summary>The department data bundle (gzip JSON of GeoJSON collections by section).</summary>
	public class OfflineDepartmentData
	{
		public byte[] Content { get; set; }
		public string Sha256 { get; set; }
		public string Version { get; set; }
	}
}
