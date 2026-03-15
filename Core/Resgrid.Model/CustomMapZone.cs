using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CustomMapZones")]
	public class CustomMapZone : IEntity
	{
		[Key]
		[Required]
		[MaxLength(36)]
		public string CustomMapZoneId { get; set; }

		[Required]
		[MaxLength(36)]
		public string CustomMapFloorId { get; set; }

		[ForeignKey("CustomMapFloorId")]
		public virtual CustomMapFloor Floor { get; set; }

		/// <summary>
		/// Human-readable name, e.g. "Building 1, Room 405a"
		/// </summary>
		[Required]
		[MaxLength(500)]
		public string Name { get; set; }

		/// <summary>
		/// Zone type (Room, Hallway, Hazard, etc.) — see CustomMapZoneType enum
		/// </summary>
		public int ZoneType { get; set; }

		/// <summary>
		/// GeoJSON polygon boundary stored as geo-projected coordinates (lat/lng)
		/// for interoperability with GPS. Supports multiple floors via FloorId FK.
		/// </summary>
		public string PolygonGeoJson { get; set; }

		/// <summary>
		/// Display color (hex) for zone rendering on Leaflet
		/// </summary>
		[MaxLength(20)]
		public string Color { get; set; }

		/// <summary>
		/// JSON blob for custom key-value metadata (pre-plan attachments, hazmat info, etc.)
		/// </summary>
		public string Metadata { get; set; }

		/// <summary>
		/// Elevation/altitude override in meters for this specific zone
		/// </summary>
		public double? Elevation { get; set; }

		/// <summary>
		/// Whether this zone can be resolved by name search / coordinate lookup
		/// </summary>
		public bool IsSearchable { get; set; }

		/// <summary>
		/// Whether this zone is visible on the map
		/// </summary>
		public bool IsActive { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomMapZoneId; }
			set { CustomMapZoneId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "CustomMapZones";

		[NotMapped]
		public string IdName => "CustomMapZoneId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Floor" };
	}
}
