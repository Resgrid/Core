using System;
using System.Reflection;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Repositories.NoSqlRepository;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class PostgreSqlMapLayerCodecTests
	{
		private static object Invoke(string name, params object[] args) => typeof(MapLayersDocRepository)
			.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

		[TestCase("Point", "[-87.6,41.8]")]
		[TestCase("LineString", "[[-87.6,41.8],[-87.7,41.9]]")]
		[TestCase("Polygon", "[[[-87.6,41.8],[-87.7,41.8],[-87.7,41.9],[-87.6,41.8]]]")]
		[TestCase("MultiPolygon", "[[[[-87.6,41.8],[-87.7,41.8],[-87.7,41.9],[-87.6,41.8]]]]")]
		public void Coordinates_and_supported_properties_survive_postgres_json(string type, string coordinates)
		{
			var input = JsonConvert.DeserializeObject<FeatureCollection>("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{\"name\":\"Gate 3\",\"shape\":\"" + type + "\"},\"geometry\":{\"type\":\"" + type + "\",\"coordinates\":" + coordinates + "}}]}");
			var layer = new MapLayer { DepartmentId = 51, Type = 0, Name = "Training layer", Data = new MapLayerData() };
			layer.Data.Hydrate(input);
			var stored = JObject.Parse((string)Invoke("SerializeDocument", layer));
			stored["id"] = "42"; // Database-owned numeric ID replaces a serialized object's stale value.
			var actual = (MapLayer)Invoke("DeserializeDocument", stored.ToString());
			Assert.That(actual.GetId(), Is.EqualTo("42"));
			Assert.That(JToken.DeepEquals(JToken.FromObject(input), JToken.FromObject(actual.Data.Convert())), Is.True);
		}

		[Test]
		public void Deleted_and_other_type_legacy_rows_are_excluded_before_geometry_decode()
		{
			const string lost = "{\"type\":1,\"isDeleted\":true,\"data\":{\"Features\":[{}]}}";
			Assert.That(Invoke("IsIncludedDocument", lost, (MapLayerTypes)1), Is.False);
			Assert.That(Invoke("IsIncludedDocument", lost.Replace("true", "false"), (MapLayerTypes)0), Is.False);
			var failure = Assert.Throws<TargetInvocationException>(() => Invoke("DeserializeDocument", lost));
			Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
		}

		[Test]
		public void Empty_layer_remains_empty()
		{
			var layer = (MapLayer)Invoke("DeserializeDocument", "{\"id\":\"7\",\"data\":{\"Features\":[]}}");
			Assert.That(layer.Data.Convert().Features, Is.Empty);
		}
	}
}
