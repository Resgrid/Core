using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	[JsonObject]
	public class MapLayerDataGeometry
	{
		[BsonElement("type")]
		[JsonProperty(PropertyName = "type")]
		public string Type { get; set; }

		[BsonElement("coordinates")]
		[JsonIgnore]
		public GeoJsonCoordinates Coordinates { get; set; }

		[BsonElement("point")]
		[JsonIgnore]
		public GeoJsonPoint<GeoJson2DGeographicCoordinates> Point { get; set; }

		[BsonElement("line")]
		[JsonIgnore]
		public GeoJsonLineStringCoordinates<GeoJson2DGeographicCoordinates> Line { get; set; }

		[BsonElement("polygon")]
		[JsonIgnore]
		public GeoJsonPolygon<GeoJson2DGeographicCoordinates> Polygon { get; set; }

		public void AddPoint(double lon, double lat)
		{
			Point = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(new GeoJson2DGeographicCoordinates(lon, lat));
		}

		public void AddLine(List<IPosition> positions)
		{
			Line = new GeoJsonLineStringCoordinates<GeoJson2DGeographicCoordinates>(positions.Select(x => new GeoJson2DGeographicCoordinates(x.Longitude, x.Longitude)));
		}

		public void AddPolygon(List<LineString> positions)
		{
			List<GeoJson2DGeographicCoordinates> coordinates = new List<GeoJson2DGeographicCoordinates>();

			foreach (var position in positions)
			{
				coordinates.AddRange(position.Coordinates.Select(x => new GeoJson2DGeographicCoordinates(x.Longitude, x.Longitude)));
			}
			
			Polygon = GeoJson.Polygon(coordinates.ToArray());
		}
	}
}
