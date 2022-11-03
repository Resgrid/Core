using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	//[JsonObject]
	public class MapLayerDataProperties
	{
		[BsonElement("shape")]
		//[JsonProperty(PropertyName = "shape")]
		public string Shape { get; set; }

		[BsonElement("name")]
		//[JsonProperty(PropertyName = "name")]
		public string Name { get; set; }

		[BsonElement("category")]
		//[JsonProperty(PropertyName = "category")]
		public string Category { get; set; }

		[BsonElement("description")]
		//[JsonProperty(PropertyName = "description")]
		public string Description { get; set; }
		
		[BsonElement("text")]
		//[JsonProperty(PropertyName = "description")]
		public string Text { get; set; }

		[BsonElement("radius")]
		//[JsonProperty(PropertyName = "radius")]
		public double? Radius { get; set; }

		[BsonElement("color")]
		//[JsonProperty(PropertyName = "color")]
		public string Color { get; set; }
	}
}
