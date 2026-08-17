using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	//[JsonObject]
	// BsonNoId: the Id property below is a GeoJSON feature id, not a Mongo document id. Without this
	// the driver's NamedIdMemberConvention promotes it to the class id member, which historically
	// persisted it as "_id" on the embedded document. BsonIgnoreExtraElements lets those older
	// documents still deserialize.
	[BsonNoId]
	[BsonIgnoreExtraElements]
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
