using System;
using System.ComponentModel.DataAnnotations;
using GeoJSON.Net.Feature;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using ProtoBuf;
using Resgrid.Model.Repositories;

namespace Resgrid.Model
{
	[BsonCollection("maplayers")]
	public class MapLayer: NoSqlDocument
	{
		[BsonElement("departmentId")]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(250)]
		[BsonElement("name")]
		public string Name { get; set; }

		[BsonElement("type")]
		public int Type { get; set; }

		[Required]
		[MaxLength(50)]
		[BsonElement("color")]
		public string Color { get; set; }

		[BsonElement("data")]
		public MapLayerData Data { get; set; }

		[BsonElement("isSearchable")]
		public bool IsSearchable { get; set; }

		[BsonElement("isOnByDefault")]
		public bool IsOnByDefault { get; set; }

		[BsonElement("addedById")]
		public string AddedById { get; set; }

		[BsonElement("addedOn")]
		public DateTime AddedOn { get; set; }

		[BsonElement("isDeleted")]
		public bool IsDeleted { get; set; }

		[BsonElement("updatedById")]
		public string UpdatedById { get; set; }

		[BsonElement("updatedOn")]
		public DateTime UpdatedOn { get; set; }
	}
}
