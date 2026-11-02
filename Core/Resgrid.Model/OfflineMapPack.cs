using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>What an offline map pack holds (maplibre-mapping-routing-navigation-plan.md §19.2). Persisted: never renumber.</summary>
	public enum OfflineMapPackKind
	{
		/// <summary>One z10 cell of the shared base map (PMTiles, vector), z0 to the configured max zoom.</summary>
		Basemap = 0,
		/// <summary>A bounding box around a call outside the response area (PMTiles, vector).</summary>
		IncidentDetail = 1,
		/// <summary>A buffer along the parts of a route plan outside the response area (PMTiles, vector).</summary>
		RouteCorridor = 2,
		/// <summary>One georeferenced custom map layer's Web Mercator tiles (PMTiles, raster PNG).</summary>
		CustomMapRaster = 3,
		/// <summary>The department's map data bundle (gzip JSON); generated on request, never stored as a row.</summary>
		DepartmentData = 4,
		/// <summary>The department sprite (custom pins, unit-type icons, POI icons) at 1x and 2x.</summary>
		Sprite = 5
	}

	/// <summary>Build state of an offline map pack (§19.4). Persisted: never renumber.</summary>
	public enum OfflineMapPackStatus
	{
		Queued = 0,
		Building = 1,
		Ready = 2,
		Failed = 3
	}

	/// <summary>
	/// A built (or queued) offline map pack (OfflineMapPacks, M0309). Base-map cells are shared by every department
	/// (DepartmentId null) and keyed by cell, max zoom and archive version, so neighbouring departments reuse files.
	/// </summary>
	public class OfflineMapPack : IEntity
	{
		public string OfflineMapPackId { get; set; }

		/// <summary>Null for shared base-map cells.</summary>
		public int? DepartmentId { get; set; }

		/// <summary><see cref="OfflineMapPackKind"/>.</summary>
		public int Kind { get; set; }

		/// <summary>Unique: what the pack is and from which inputs, e.g. "basemap:z10/163/395:15:20261028".</summary>
		public string PackKey { get; set; }

		public string Name { get; set; }

		/// <summary>The custom map layer (IndoorMapFloorId) of a CustomMapRaster pack.</summary>
		public string SourceId { get; set; }

		public string Version { get; set; }

		/// <summary>"west,south,east,north".</summary>
		public string Bounds { get; set; }

		public int MinZoom { get; set; }

		public int MaxZoom { get; set; }

		public long SizeBytes { get; set; }

		/// <summary>Lower-case hex SHA-256 of the stored file (the sprite's files are listed in <see cref="FilesJson"/>).</summary>
		public string Sha256 { get; set; }

		/// <summary>Object key (or prefix, for the sprite) in the pack store.</summary>
		public string StoragePath { get; set; }

		/// <summary>Multi-file packs (the sprite): JSON array of { Name, Sha256, SizeBytes }.</summary>
		public string FilesJson { get; set; }

		/// <summary><see cref="OfflineMapPackStatus"/>.</summary>
		public int Status { get; set; }

		public DateTime AddedOn { get; set; }

		public DateTime? BuiltOn { get; set; }

		public DateTime? LastRequestedOn { get; set; }

		public int Attempts { get; set; }

		public string Error { get; set; }

		[NotMapped]
		public string TableName => "OfflineMapPacks";

		[NotMapped]
		public string IdName => "OfflineMapPackId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return OfflineMapPackId; }
			set { OfflineMapPackId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One file of a multi-file pack (<see cref="OfflineMapPack.FilesJson"/>).</summary>
	public class OfflineMapPackFile
	{
		public string Name { get; set; }

		public string Sha256 { get; set; }

		public long SizeBytes { get; set; }
	}
}
