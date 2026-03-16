using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class CustomMapTile : IEntity
	{
		public string CustomMapTileId { get; set; }

		public string CustomMapLayerId { get; set; }

		public int ZoomLevel { get; set; }

		public int TileX { get; set; }

		public int TileY { get; set; }

		public byte[] TileData { get; set; }

		public string TileContentType { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "CustomMapTiles";

		[NotMapped]
		public string IdName => "CustomMapTileId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomMapTileId; }
			set { CustomMapTileId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
