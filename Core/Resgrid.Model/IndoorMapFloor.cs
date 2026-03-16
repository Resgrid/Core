using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class IndoorMapFloor : IEntity
	{
		public string IndoorMapFloorId { get; set; }

		public string IndoorMapId { get; set; }

		public string Name { get; set; }

		public int FloorOrder { get; set; }

		public byte[] ImageData { get; set; }

		public string ImageContentType { get; set; }

		public decimal? BoundsNELat { get; set; }

		public decimal? BoundsNELon { get; set; }

		public decimal? BoundsSWLat { get; set; }

		public decimal? BoundsSWLon { get; set; }

		public decimal Opacity { get; set; }

		public int LayerType { get; set; }

		public bool IsTiled { get; set; }

		public int? TileMinZoom { get; set; }

		public int? TileMaxZoom { get; set; }

		public long? SourceFileSize { get; set; }

		public string GeoJsonData { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "IndoorMapFloors";

		[NotMapped]
		public string IdName => "IndoorMapFloorId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IndoorMapFloorId; }
			set { IndoorMapFloorId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
