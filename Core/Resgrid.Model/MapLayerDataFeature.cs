using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	//[JsonObject]
	public class MapLayerDataFeature
	{
		[BsonElement("type")]
		//[JsonProperty(PropertyName = "type")]
		public string Type { get; set; }

		[BsonElement("properties")]
		//[JsonProperty(PropertyName = "properties")]
		public MapLayerDataProperties Properties { get; set; }

		[BsonElement("geometry")]
		//[JsonProperty(PropertyName = "geometry")]
		public MapLayerDataGeometry Geometry { get; set; }
		
		[BsonElement("id")]
		//[JsonProperty(PropertyName = "id")]
		public string Id { get; set; }
	}
}
