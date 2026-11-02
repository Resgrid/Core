using System.Linq;
using FluentAssertions;
using GeoJSON.Net.Feature;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Map layer shapes saved by the layer editor round-trip through storage (maplibre plan §22.4): holes stay holes,
	/// a shape cut into parts stays a multipolygon, and circle/text properties survive.
	/// </summary>
	[TestFixture]
	public class MapLayerDataTests
	{
		private const string Layer = @"{""type"":""FeatureCollection"",""features"":[
			{""type"":""Feature"",""properties"":{""shape"":""Polygon"",""color"":""#ff0000"",""name"":""Yard"",""category"":""default""},
			 ""geometry"":{""type"":""Polygon"",""coordinates"":[[[0,0],[10,0],[10,10],[0,10],[0,0]],[[2,2],[4,2],[4,4],[2,4],[2,2]]]}},
			{""type"":""Feature"",""properties"":{""shape"":""Polygon"",""name"":""Split""},
			 ""geometry"":{""type"":""MultiPolygon"",""coordinates"":[[[[20,0],[21,0],[21,1],[20,1],[20,0]]],[[[22,0],[23,0],[23,1],[22,1],[22,0]]]]}},
			{""type"":""Feature"",""properties"":{""shape"":""Circle"",""radius"":125.5,""name"":""Tank""},""geometry"":{""type"":""Point"",""coordinates"":[-119.75,39.55]}},
			{""type"":""Feature"",""properties"":{""shape"":""Text"",""text"":""Gate 3""},""geometry"":{""type"":""Point"",""coordinates"":[-119.7,39.5]}}]}";

		private static FeatureCollection RoundTrip(string json)
		{
			var data = new MapLayerData();
			data.Hydrate(JsonConvert.DeserializeObject<FeatureCollection>(json));

			// Through BSON as well, as the layer is stored.
			var stored = BsonSerializer.Deserialize<MapLayerData>(data.ToBson());
			return stored.Convert();
		}

		[Test]
		public void A_polygon_keeps_its_holes()
		{
			var polygon = (GeoJSON.Net.Geometry.Polygon)RoundTrip(Layer).Features[0].Geometry;

			polygon.Coordinates.Should().HaveCount(2, "the outline and one hole, not one ring joining both");
			polygon.Coordinates[0].Coordinates.Should().HaveCount(5);
			polygon.Coordinates[1].Coordinates.Select(c => (c.Longitude, c.Latitude)).Should().Equal((2d, 2d), (4d, 2d), (4d, 4d), (2d, 4d), (2d, 2d));
		}

		[Test]
		public void A_multipolygon_round_trips()
		{
			var feature = RoundTrip(Layer).Features[1];

			feature.Geometry.Should().BeOfType<GeoJSON.Net.Geometry.MultiPolygon>();
			((GeoJSON.Net.Geometry.MultiPolygon)feature.Geometry).Coordinates.Should().HaveCount(2);
			feature.Properties["name"].Should().Be("Split");
		}

		[Test]
		public void Circle_and_text_properties_survive()
		{
			var features = RoundTrip(Layer).Features;

			features[2].Properties["shape"].Should().Be("Circle");
			features[2].Properties["radius"].Should().Be(125.5);
			features[3].Properties["text"].Should().Be("Gate 3");
		}

		[Test]
		public void A_geometry_storage_cannot_hold_is_left_out_not_added_as_null()
		{
			var json = @"{""type"":""FeatureCollection"",""features"":[
				{""type"":""Feature"",""properties"":{},""geometry"":{""type"":""MultiLineString"",""coordinates"":[[[0,0],[1,1]]]}},
				{""type"":""Feature"",""properties"":{},""geometry"":{""type"":""Point"",""coordinates"":[1,2]}}]}";

			var features = RoundTrip(json).Features;

			features.Should().ContainSingle().Which.Geometry.Should().BeOfType<GeoJSON.Net.Geometry.Point>();
		}
	}
}
