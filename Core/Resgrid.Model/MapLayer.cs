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
		[JsonProperty(PropertyName = "departmentId")]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(250)]
		[BsonElement("name")]
		[JsonProperty(PropertyName = "name")]
		public string Name { get; set; }

		[BsonElement("type")]
		[JsonProperty(PropertyName = "type")]
		public int Type { get; set; }

		[Required]
		[MaxLength(50)]
		[BsonElement("color")]
		[JsonProperty(PropertyName = "color")]
		public string Color { get; set; }

		[BsonElement("data")]
		[JsonProperty(PropertyName = "data")]
		public MapLayerData Data { get; set; }

		[BsonElement("isSearchable")]
		[JsonProperty(PropertyName = "isSearchable")]
		public bool IsSearchable { get; set; }

		[BsonElement("isOnByDefault")]
		[JsonProperty(PropertyName = "isOnByDefault")]
		public bool IsOnByDefault { get; set; }

		[BsonElement("addedById")]
		[JsonProperty(PropertyName = "addedById")]
		public string AddedById { get; set; }

		[BsonElement("addedOn")]
		[JsonProperty(PropertyName = "addedOn")]
		public DateTime AddedOn { get; set; }

		[BsonElement("isDeleted")]
		[JsonProperty(PropertyName = "isDeleted")]
		public bool IsDeleted { get; set; }

		[BsonElement("updatedById")]
		[JsonProperty(PropertyName = "updatedById")]
		public string UpdatedById { get; set; }

		[BsonElement("updatedOn")]
		[JsonProperty(PropertyName = "updatedOn")]
		public DateTime UpdatedOn { get; set; }

		[BsonIgnore()]
		[JsonProperty(PropertyName = "id")]
		public string PgId { get; set; }

		public string GetId()
		{
			if (!String.IsNullOrWhiteSpace(PgId))
				return PgId;

			return Id.ToString();
		}
	}
}
