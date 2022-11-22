using GeoJSON.Net;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.AspNetCore.DataProtection.Repositories;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;
using Newtonsoft.Json;
using ProtoBuf;
using Resgrid.Model.Repositories;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Resgrid.Model
{
	//[JsonObject]
	public class MapLayerData //: BsonDocument
	{
		[BsonElement("type")]
		public string Type { get; set; }

		[BsonElement("features")]
		public List<MapLayerDataFeature> Features { get; set; }

		public void Hydrate(FeatureCollection feature)
		{
			if (Features == null)
				Features = new List<MapLayerDataFeature>();

			Type = ((GeoJSONObjectType)feature.Type).ToString();

			foreach (var f in feature.Features)
			{
				var mapFeature = new MapLayerDataFeature();
				mapFeature.Type = ((GeoJSONObjectType)f.Geometry.Type).ToString();
				mapFeature.Properties = new MapLayerDataProperties();

				if (f.Properties.ContainsKey("name"))
					mapFeature.Properties.Name = f.Properties["name"].ToString();

				if (f.Properties.ContainsKey("shape"))
					mapFeature.Properties.Shape = f.Properties["shape"].ToString();

				if (f.Properties.ContainsKey("color"))
					mapFeature.Properties.Color = f.Properties["color"].ToString();

				if (f.Properties.ContainsKey("description"))
					mapFeature.Properties.Description = f.Properties["description"].ToString();

				if (f.Properties.ContainsKey("category"))
					mapFeature.Properties.Category = f.Properties["category"].ToString();

				if (f.Properties.ContainsKey("text"))
					mapFeature.Properties.Text = f.Properties["text"].ToString();

				if (f.Properties.ContainsKey("radius"))
					mapFeature.Properties.Radius = double.Parse(f.Properties["radius"].ToString());

				mapFeature.Geometry = new MapLayerDataGeometry();
				mapFeature.Geometry.Type = ((GeoJSONObjectType)f.Geometry.Type).ToString();

				switch (f.Geometry.Type)
				{
					case GeoJSONObjectType.Point:
						var point = (GeoJSON.Net.Geometry.Point)f.Geometry;
						mapFeature.Geometry.Point = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(new GeoJson2DGeographicCoordinates(point.Coordinates.Longitude, point.Coordinates.Latitude));
						break;
					case GeoJSONObjectType.LineString:
						var lineString = (GeoJSON.Net.Geometry.LineString)f.Geometry;
						mapFeature.Geometry.Line = new GeoJsonLineStringCoordinates<GeoJson2DGeographicCoordinates>(lineString.Coordinates.ToList().Select(x => new GeoJson2DGeographicCoordinates(x.Longitude, x.Latitude)));
						break;
					case GeoJSONObjectType.Polygon:
						var polygon = (GeoJSON.Net.Geometry.Polygon)f.Geometry;
						List<GeoJson2DGeographicCoordinates> coordinates = new List<GeoJson2DGeographicCoordinates>();

						foreach (var position in polygon.Coordinates.ToList())
						{
							coordinates.AddRange(position.Coordinates.Select(x => new GeoJson2DGeographicCoordinates(x.Longitude, x.Latitude)));
						}

						mapFeature.Geometry.Polygon = GeoJson.Polygon(coordinates.ToArray());

						break;
				}

				Features.Add(mapFeature);
			}
		}

		public FeatureCollection Convert()
		{
			var fc = new FeatureCollection();

			foreach (var f in Features)
			{
				Feature feature = null;

				var properties = new Dictionary<string, object>();

				if (!String.IsNullOrWhiteSpace(f.Properties.Name))
				properties.Add("name", f.Properties.Name);

				if (!String.IsNullOrWhiteSpace(f.Properties.Shape))
					properties.Add("shape", f.Properties.Shape);

				if (!String.IsNullOrWhiteSpace(f.Properties.Color))
					properties.Add("color", f.Properties.Color);

				if (!String.IsNullOrWhiteSpace(f.Properties.Description))
					properties.Add("description", f.Properties.Description);

				if (!String.IsNullOrWhiteSpace(f.Properties.Category))
					properties.Add("category", f.Properties.Category);

				if (!String.IsNullOrWhiteSpace(f.Properties.Text))
					properties.Add("text", f.Properties.Text);

				if (f.Properties.Radius.HasValue)
					properties.Add("radius", f.Properties.Radius);

				switch (f.Geometry.Type)
				{
					case "Point":
						feature = new Feature(new GeoJSON.Net.Geometry.Point(new Position(f.Geometry.Point.Coordinates.Latitude, f.Geometry.Point.Coordinates.Longitude)), properties);
						break;
					case "LineString":
						feature = new Feature(new GeoJSON.Net.Geometry.LineString(f.Geometry.Line.Positions.Select(x => new Position(x.Latitude, x.Longitude)).ToList()), properties);
						break;
					case "Polygon":
						var line = new LineString(f.Geometry.Polygon.Coordinates.Exterior.Positions.Select(x => new Position(x.Latitude, x.Longitude)).ToList());
						feature = new Feature(new GeoJSON.Net.Geometry.Polygon(new List<LineString> { line }), properties);
						break;
						//case "Polygon":
						//	var lines = new List<LineString>();
						//	for (int i = 0; i < f.Geometry.Polygon.Coordinates.Exterior.Positions.Count; i += 2)
						//	{
						//		var line = new LineString(new List<Position> {
						//			new Position(f.Geometry.Polygon.Coordinates.Exterior.Positions[i].Latitude, f.Geometry.Polygon.Coordinates.Exterior.Positions[i].Longitude),
						//			new Position(f.Geometry.Polygon.Coordinates.Exterior.Positions[i + 1].Latitude, f.Geometry.Polygon.Coordinates.Exterior.Positions[i + 1].Longitude)});
						//	}

						//	feature = new Feature(new GeoJSON.Net.Geometry.Polygon(lines), properties);
						//	break;
				}

				fc.Features.Add(feature);
			}

			return fc;
		}
	}
}
