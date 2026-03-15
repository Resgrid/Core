using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CustomMapFloors")]
	public class CustomMapFloor : IEntity
	{
		[Key]
		[Required]
		[MaxLength(36)]
		public string CustomMapFloorId { get; set; }

		[Required]
		[MaxLength(36)]
		public string CustomMapId { get; set; }

		[ForeignKey("CustomMapId")]
		public virtual CustomMap CustomMap { get; set; }

		public int FloorNumber { get; set; }

		[Required]
		[MaxLength(200)]
		public string Name { get; set; }

		[MaxLength(2000)]
		public string ImageUrl { get; set; }

		[MaxLength(2000)]
		public string TileBaseUrl { get; set; }

		/// <summary>
		/// How the background image is stored. See <see cref="CustomMapFloorStorageType"/>.
		/// </summary>
		public int StorageType { get; set; }

		/// <summary>
		/// FK to the Files table when StorageType = DatabaseBlob.
		/// </summary>
		public int? ImageFileId { get; set; }

		/// <summary>Original image width in pixels (used for tile generation).</summary>
		public int? ImageWidthPx { get; set; }

		/// <summary>Original image height in pixels (used for tile generation).</summary>
		public int? ImageHeightPx { get; set; }

		/// <summary>Number of zoom levels in the tile pyramid (e.g. 4 means zoom 0–3).</summary>
		public int? TileZoomLevels { get; set; }

		public double? Elevation { get; set; }
		public int SortOrder { get; set; }
		public bool IsDefault { get; set; }

		public virtual ICollection<CustomMapZone> Zones { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomMapFloorId; }
			set { CustomMapFloorId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "CustomMapFloors";

		[NotMapped]
		public string IdName => "CustomMapFloorId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CustomMap", "Zones" };
	}
}
