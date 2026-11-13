using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using GeoJSON.Net.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using SharpKml.Base;
using SharpKml.Dom;
using SharpKml.Dom.GX;
using SharpKml.Engine;
using GeoFeature = GeoJSON.Net.Feature.Feature;
using FeatureCollection = GeoJSON.Net.Feature.FeatureCollection;

namespace Resgrid.Providers.GeoLocationProvider
{
	/// <summary>
	/// Reads an uploaded KML, KMZ or GeoJSON file into the shapes of a map layer (event layers: a marathon route, a festival
	/// site, the vendors along a parade). Every placemark or feature becomes plain points, lines and polygons -- multi
	/// geometries and GPS tracks are split or flattened, polygon holes are kept -- with the name, a plain-text description
	/// (HTML stripped, ExtendedData/extra properties appended), the KML style or GeoJSON stroke/fill color, and the KML folder
	/// as "category". Coordinates are rounded to 7 decimals (about 1 cm).
	/// </summary>
	/// <remarks>
	/// The layer is stored as one document and every map loads all of it, so the file, shape and coordinate counts are capped
	/// (<see cref="MapLayerFileLimits"/>). Descriptions are stored as plain text: maps render them as text, never as HTML.
	/// </remarks>
	public static class MapLayerFileReader
	{
		private const int MaxNameLength = 250;
		private const int MaxDescriptionLength = 2000;
		private const int MaxCategoryLength = 100;
		private const int MaxExtraFields = 20;

		private static readonly Regex HexColor = new Regex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);
		private static readonly Regex LineBreakTags = new Regex(@"<\s*(br|/p|/div|/li|/tr|/h[1-6])\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);
		private static readonly Regex Spaces = new Regex(@"[ \t ]+", RegexOptions.Compiled);

		/// <summary>GeoJSON properties that are styling or already mapped, so they are not repeated in the description.</summary>
		private static readonly HashSet<string> GeoJsonReservedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"name", "title", "label", "description", "desc", "notes", "color", "colour", "stroke", "fill", "marker-color",
			"stroke-width", "stroke-opacity", "fill-opacity", "marker-size", "marker-symbol", "category", "folder",
			"shape", "radius", "text", "styleUrl", "styleHash", "styleMapHash", "icon", "id"
		};

		/// <summary>Reads a file by its name and content. KMZ is recognised by content, whatever the file is called.</summary>
		public static MapLayerFileReadResult Read(Stream input, string fileName)
		{
			if (input == null)
				return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.Unreadable };

			byte[] content;
			using (var buffer = new MemoryStream())
			{
				var block = new byte[81920];
				int read;
				while ((read = input.Read(block, 0, block.Length)) > 0)
				{
					buffer.Write(block, 0, read);
					if (buffer.Length > MapLayerFileLimits.MaxFileBytes)
						return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.TooLarge };
				}

				content = buffer.ToArray();
			}

			return Read(content, fileName);
		}

		/// <inheritdoc cref="Read(Stream, string)"/>
		public static MapLayerFileReadResult Read(byte[] content, string fileName)
		{
			if (content == null || content.Length == 0)
				return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.Unreadable };

			if (content.LongLength > MapLayerFileLimits.MaxFileBytes)
				return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.TooLarge };

			var extension = (Path.GetExtension(fileName ?? string.Empty) ?? string.Empty).ToLowerInvariant();
			var isZip = content.Length > 3 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;

			if (isZip || extension == ".kml" || extension == ".kmz")
				return isZip && !KmzFitsLimit(content) ? new MapLayerFileReadResult { Error = MapLayerFileReadErrors.TooLarge } : ReadKml(content);

			if (extension == ".geojson" || extension == ".json")
				return ReadGeoJson(content);

			// No telling extension: sniff the first character.
			var first = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 512)).TrimStart('﻿', ' ', '\t', '\r', '\n');
			if (first.StartsWith("{") || first.StartsWith("["))
				return ReadGeoJson(content);
			if (first.StartsWith("<"))
				return ReadKml(content);

			return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.UnsupportedType };
		}

		/// <summary>The KML inside a KMZ must fit the same limit as a plain file (a small zip can inflate a long way).</summary>
		private static bool KmzFitsLimit(byte[] content)
		{
			try
			{
				using (var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
					return archive.Entries.Sum(e => e.Length) <= MapLayerFileLimits.MaxFileBytes * 4;
			}
			catch (InvalidDataException)
			{
				return true; // Damaged: the KML read reports it.
			}
		}

		#region KML

		private static MapLayerFileReadResult ReadKml(byte[] content)
		{
			Element root;
			try
			{
				root = KmlDocumentReader.ReadRoot(content);
			}
			catch (Exception ex) when (ex is XmlException || ex is InvalidDataException || ex is FileNotFoundException || ex is DecoderFallbackException || ex is ArgumentException)
			{
				return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.Unreadable };
			}

			if (root == null)
				return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.Unreadable };

			var builder = new Builder();
			var elements = root.Flatten().ToList();
			var styles = new Dictionary<string, Style>(StringComparer.Ordinal);
			var styleMaps = new Dictionary<string, StyleMapCollection>(StringComparer.Ordinal);

			foreach (var style in elements.OfType<Style>().Where(s => !string.IsNullOrWhiteSpace(s.Id)))
				styles[style.Id] = style;
			foreach (var map in elements.OfType<StyleMapCollection>().Where(s => !string.IsNullOrWhiteSpace(s.Id)))
				styleMaps[map.Id] = map;

			foreach (var placemark in elements.OfType<Placemark>())
			{
				var shapes = new List<IGeometryObject>();
				var invalid = 0;
				CollectKmlGeometry(placemark.Geometry, shapes, ref invalid);
				builder.Result.SkippedInvalid += invalid;

				if (shapes.Count == 0)
				{
					if (invalid == 0)
						builder.Result.SkippedWithoutGeometry++;
					continue;
				}

				var style = ResolveStyle(placemark, styles, styleMaps);
				var description = JoinDescription(PlainText(placemark.Description?.Text), KmlExtendedData(placemark.ExtendedData));
				var category = Trim(NearestFolderName(placemark), MaxCategoryLength);

				foreach (var shape in shapes)
				{
					var color = ColorFor(style, shape);
					if (!builder.Add(shape, Trim(PlainText(placemark.Name), MaxNameLength), description, color, category))
						return builder.Result;
				}
			}

			return builder.Finish();
		}

		private static void CollectKmlGeometry(SharpKml.Dom.Geometry geometry, List<IGeometryObject> shapes, ref int invalid)
		{
			switch (geometry)
			{
				case null:
					return;
				case SharpKml.Dom.Point point:
					if (point.Coordinate != null && TryPosition(point.Coordinate.Longitude, point.Coordinate.Latitude, out var position))
						shapes.Add(new GeoJSON.Net.Geometry.Point(position));
					else
						invalid++;
					return;
				case SharpKml.Dom.LineString line:
					AddLine(line.Coordinates?.Select(v => (v.Longitude, v.Latitude)), shapes, ref invalid);
					return;
				case LinearRing ring:
					AddLine(ring.Coordinates?.Select(v => (v.Longitude, v.Latitude)), shapes, ref invalid);
					return;
				case SharpKml.Dom.Polygon polygon:
					var outer = polygon.OuterBoundary?.LinearRing?.Coordinates?.Select(v => (v.Longitude, v.Latitude));
					var holes = polygon.InnerBoundary?.Select(b => b.LinearRing?.Coordinates?.Select(v => (v.Longitude, v.Latitude)));
					AddPolygon(outer, holes, shapes, ref invalid);
					return;
				case MultipleGeometry multiple:
					foreach (var child in (IEnumerable<SharpKml.Dom.Geometry>)multiple.Geometry ?? Enumerable.Empty<SharpKml.Dom.Geometry>())
						CollectKmlGeometry(child, shapes, ref invalid);
					return;
				case Track track:
					AddLine(track.Coordinates?.Select(v => (v.Longitude, v.Latitude)), shapes, ref invalid);
					return;
				case MultipleTrack multipleTrack:
					foreach (var child in multipleTrack.Tracks ?? Enumerable.Empty<Track>())
						CollectKmlGeometry(child, shapes, ref invalid);
					return;
				default:
					// Model and other geometries have nothing to draw on a map.
					invalid++;
					return;
			}
		}

		private static Style ResolveStyle(Placemark placemark, Dictionary<string, Style> styles, Dictionary<string, StyleMapCollection> styleMaps)
		{
			foreach (var selector in placemark.Styles ?? Enumerable.Empty<StyleSelector>())
			{
				var inline = FromSelector(selector, styles, styleMaps, 0);
				if (inline != null)
					return inline;
			}

			return FromUrl(placemark.StyleUrl, styles, styleMaps, 0);
		}

		private static Style FromSelector(StyleSelector selector, Dictionary<string, Style> styles, Dictionary<string, StyleMapCollection> styleMaps, int depth)
		{
			if (selector is Style style)
				return style;

			if (selector is StyleMapCollection map && depth < 4)
			{
				var pair = map.FirstOrDefault(p => p.State == StyleState.Normal) ?? map.FirstOrDefault();
				if (pair == null)
					return null;

				return pair.Selector != null ? FromSelector(pair.Selector, styles, styleMaps, depth + 1) : FromUrl(pair.StyleUrl, styles, styleMaps, depth + 1);
			}

			return null;
		}

		private static Style FromUrl(Uri url, Dictionary<string, Style> styles, Dictionary<string, StyleMapCollection> styleMaps, int depth)
		{
			if (url == null || depth > 4)
				return null;

			var text = url.OriginalString ?? string.Empty;
			var hash = text.LastIndexOf('#');
			var id = hash >= 0 ? text.Substring(hash + 1) : text;

			if (styles.TryGetValue(id, out var style))
				return style;

			if (styleMaps.TryGetValue(id, out var map))
				return FromSelector(map, styles, styleMaps, depth + 1);

			return null;
		}

		private static string ColorFor(Style style, IGeometryObject shape)
		{
			if (style == null)
				return null;

			Color32? color = null;
			switch (shape)
			{
				case GeoJSON.Net.Geometry.Point _:
					color = style.Icon?.Color ?? style.Label?.Color;
					break;
				case GeoJSON.Net.Geometry.Polygon _:
					// The outline usually carries the event's color; the fill is often near transparent.
					color = style.Line?.Color ?? style.Polygon?.Color;
					break;
				default:
					color = style.Line?.Color;
					break;
			}

			if (!color.HasValue)
				return null;

			var c = color.Value;
			// An icon left white is the KML default, not a chosen color, and vanishes on most base maps.
			if (shape is GeoJSON.Net.Geometry.Point && c.Red == 255 && c.Green == 255 && c.Blue == 255)
				return null;

			return string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", c.Red, c.Green, c.Blue);
		}

		private static string NearestFolderName(Element element)
		{
			for (var parent = element?.Parent; parent != null; parent = parent.Parent)
			{
				if (parent is Folder folder && !string.IsNullOrWhiteSpace(folder.Name))
					return PlainText(folder.Name);
			}

			return null;
		}

		private static IEnumerable<string> KmlExtendedData(ExtendedData data)
		{
			if (data == null)
				yield break;

			var count = 0;
			foreach (var item in data.Data ?? Enumerable.Empty<Data>())
			{
				var label = PlainText(item.DisplayName) ?? PlainText(item.Name);
				var value = PlainText(item.Value);
				if (string.IsNullOrWhiteSpace(value) || count++ >= MaxExtraFields)
					continue;
				yield return string.IsNullOrWhiteSpace(label) ? value : $"{label}: {value}";
			}

			foreach (var schema in data.SchemaData ?? Enumerable.Empty<SchemaData>())
			{
				foreach (var item in schema.SimpleData ?? Enumerable.Empty<SimpleData>())
				{
					var value = PlainText(item.Text);
					if (string.IsNullOrWhiteSpace(value) || count++ >= MaxExtraFields)
						continue;
					yield return string.IsNullOrWhiteSpace(item.Name) ? value : $"{PlainText(item.Name)}: {value}";
				}
			}
		}

		#endregion KML

		#region GeoJSON

		private static MapLayerFileReadResult ReadGeoJson(byte[] content)
		{
			JToken root;
			try
			{
				var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
				using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None, MaxDepth = 128 })
					root = JToken.ReadFrom(reader);
			}
			catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
			{
				return new MapLayerFileReadResult { Error = MapLayerFileReadErrors.Unreadable };
			}

			var builder = new Builder();
			if (!ReadGeoJsonNode(root, null, builder))
				return builder.Result;

			return builder.Finish();
		}

		/// <summary>Walks FeatureCollection, Feature, bare geometries and arrays of them. False when a limit was hit.</summary>
		private static bool ReadGeoJsonNode(JToken node, JObject properties, Builder builder)
		{
			if (node is JArray array)
			{
				foreach (var item in array)
				{
					if (!ReadGeoJsonNode(item, properties, builder))
						return false;
				}

				return true;
			}

			if (!(node is JObject obj))
				return true;

			var type = obj.Value<string>("type") ?? string.Empty;

			if (type.Equals("FeatureCollection", StringComparison.OrdinalIgnoreCase))
				return ReadGeoJsonNode(obj["features"], null, builder);

			if (type.Equals("Feature", StringComparison.OrdinalIgnoreCase))
			{
				var featureProperties = obj["properties"] as JObject ?? new JObject();
				if (obj["geometry"] == null || obj["geometry"].Type == JTokenType.Null)
				{
					builder.Result.SkippedWithoutGeometry++;
					return true;
				}

				return ReadGeoJsonNode(obj["geometry"], featureProperties, builder);
			}

			var shapes = new List<IGeometryObject>();
			var invalid = 0;
			CollectGeoJsonGeometry(obj, shapes, ref invalid);
			builder.Result.SkippedInvalid += invalid;

			if (shapes.Count == 0)
				return true;

			properties = properties ?? new JObject();
			var name = Trim(PlainText(FirstString(properties, "name", "title", "label")), MaxNameLength);
			var description = JoinDescription(PlainText(FirstString(properties, "description", "desc", "notes")), GeoJsonExtraProperties(properties));
			var color = FirstString(properties, "color", "colour", "stroke", "marker-color", "fill");
			var category = Trim(PlainText(FirstString(properties, "category", "folder")), MaxCategoryLength);

			foreach (var shape in shapes)
			{
				if (!builder.Add(shape, name, description, color, category, properties))
					return false;
			}

			return true;
		}

		private static void CollectGeoJsonGeometry(JObject geometry, List<IGeometryObject> shapes, ref int invalid)
		{
			var type = geometry.Value<string>("type") ?? string.Empty;
			var coordinates = geometry["coordinates"];

			switch (type.ToLowerInvariant())
			{
				case "point":
					if (TryJsonPosition(coordinates, out var position))
						shapes.Add(new GeoJSON.Net.Geometry.Point(position));
					else
						invalid++;
					break;
				case "multipoint":
					foreach (var item in coordinates as JArray ?? new JArray())
					{
						if (TryJsonPosition(item, out var p))
							shapes.Add(new GeoJSON.Net.Geometry.Point(p));
						else
							invalid++;
					}
					break;
				case "linestring":
					AddLine(JsonPairs(coordinates), shapes, ref invalid);
					break;
				case "multilinestring":
					foreach (var line in coordinates as JArray ?? new JArray())
						AddLine(JsonPairs(line), shapes, ref invalid);
					break;
				case "polygon":
					AddJsonPolygon(coordinates as JArray, shapes, ref invalid);
					break;
				case "multipolygon":
					foreach (var polygon in coordinates as JArray ?? new JArray())
						AddJsonPolygon(polygon as JArray, shapes, ref invalid);
					break;
				case "geometrycollection":
					foreach (var child in geometry["geometries"] as JArray ?? new JArray())
					{
						if (child is JObject childObject)
							CollectGeoJsonGeometry(childObject, shapes, ref invalid);
					}
					break;
				default:
					invalid++;
					break;
			}
		}

		private static void AddJsonPolygon(JArray rings, List<IGeometryObject> shapes, ref int invalid)
		{
			if (rings == null || rings.Count == 0)
			{
				invalid++;
				return;
			}

			AddPolygon(JsonPairs(rings[0]), rings.Skip(1).Select(JsonPairs), shapes, ref invalid);
		}

		private static IEnumerable<(double Longitude, double Latitude)> JsonPairs(JToken coordinates)
		{
			foreach (var item in coordinates as JArray ?? new JArray())
			{
				if (item is JArray pair && pair.Count >= 2 && TryNumber(pair[0], out var lon) && TryNumber(pair[1], out var lat))
					yield return (lon, lat);
				else
					yield return (double.NaN, double.NaN);
			}
		}

		private static bool TryJsonPosition(JToken coordinates, out Position position)
		{
			position = null;
			return coordinates is JArray pair && pair.Count >= 2 && TryNumber(pair[0], out var lon) && TryNumber(pair[1], out var lat)
				&& TryPosition(lon, lat, out position);
		}

		private static bool TryNumber(JToken token, out double value)
		{
			value = double.NaN;
			if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
				return false;

			value = token.Value<double>();
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}

		private static string FirstString(JObject properties, params string[] names)
		{
			foreach (var name in names)
			{
				var property = properties.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
				if (property?.Value != null && property.Value.Type != JTokenType.Null && property.Value.Type != JTokenType.Object && property.Value.Type != JTokenType.Array)
				{
					var text = property.Value.ToString();
					if (!string.IsNullOrWhiteSpace(text))
						return text;
				}
			}

			return null;
		}

		private static IEnumerable<string> GeoJsonExtraProperties(JObject properties)
		{
			var count = 0;
			foreach (var property in properties.Properties())
			{
				if (GeoJsonReservedProperties.Contains(property.Name) || property.Value == null)
					continue;
				if (property.Value.Type == JTokenType.Null || property.Value.Type == JTokenType.Object || property.Value.Type == JTokenType.Array)
					continue;

				var value = PlainText(property.Value.ToString());
				if (string.IsNullOrWhiteSpace(value) || count++ >= MaxExtraFields)
					continue;

				yield return $"{PlainText(property.Name)}: {value}";
			}
		}

		#endregion GeoJSON

		#region Shapes

		private static void AddLine(IEnumerable<(double Longitude, double Latitude)> coordinates, List<IGeometryObject> shapes, ref int invalid)
		{
			var positions = Positions(coordinates, out var bad);
			if (bad || positions.Count < 2)
			{
				invalid++;
				return;
			}

			shapes.Add(new GeoJSON.Net.Geometry.LineString(positions));
		}

		private static void AddPolygon(IEnumerable<(double Longitude, double Latitude)> outer, IEnumerable<IEnumerable<(double Longitude, double Latitude)>> holes,
			List<IGeometryObject> shapes, ref int invalid)
		{
			var outerRing = Ring(outer);
			if (outerRing == null)
			{
				invalid++;
				return;
			}

			var rings = new List<GeoJSON.Net.Geometry.LineString> { outerRing };
			foreach (var hole in holes ?? Enumerable.Empty<IEnumerable<(double, double)>>())
			{
				var holeRing = Ring(hole);
				if (holeRing != null)
					rings.Add(holeRing); // A broken hole is left out; the outline still draws.
			}

			shapes.Add(new GeoJSON.Net.Geometry.Polygon(rings));
		}

		/// <summary>A closed ring of at least three distinct points, or null.</summary>
		private static GeoJSON.Net.Geometry.LineString Ring(IEnumerable<(double Longitude, double Latitude)> coordinates)
		{
			if (coordinates == null)
				return null;

			var positions = Positions(coordinates, out var bad);
			if (bad)
				return null;

			if (positions.Count > 1 && SamePosition(positions[0], positions[positions.Count - 1]))
				positions.RemoveAt(positions.Count - 1);

			if (positions.Count < 3)
				return null;

			positions.Add(positions[0]);
			return new GeoJSON.Net.Geometry.LineString(positions);
		}

		/// <summary>Rounded positions without consecutive repeats; <paramref name="bad"/> when any coordinate is unusable.</summary>
		private static List<IPosition> Positions(IEnumerable<(double Longitude, double Latitude)> coordinates, out bool bad)
		{
			bad = false;
			var positions = new List<IPosition>();
			if (coordinates == null)
				return positions;

			foreach (var (longitude, latitude) in coordinates)
			{
				if (!TryPosition(longitude, latitude, out var position))
				{
					bad = true;
					return positions;
				}

				if (positions.Count == 0 || !SamePosition(positions[positions.Count - 1], position))
					positions.Add(position);
			}

			return positions;
		}

		private static bool TryPosition(double longitude, double latitude, out Position position)
		{
			position = null;
			if (double.IsNaN(longitude) || double.IsNaN(latitude) || double.IsInfinity(longitude) || double.IsInfinity(latitude))
				return false;
			if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
				return false;

			position = new Position(Math.Round(latitude, 7), Math.Round(longitude, 7));
			return true;
		}

		private static bool SamePosition(IPosition a, IPosition b) =>
			a.Latitude.Equals(b.Latitude) && a.Longitude.Equals(b.Longitude);

		private static int PositionCount(IGeometryObject shape)
		{
			switch (shape)
			{
				case GeoJSON.Net.Geometry.Point _:
					return 1;
				case GeoJSON.Net.Geometry.LineString line:
					return line.Coordinates.Count;
				case GeoJSON.Net.Geometry.Polygon polygon:
					return polygon.Coordinates.Sum(r => r.Coordinates.Count);
				default:
					return 0;
			}
		}

		#endregion Shapes

		#region Text

		/// <summary>Plain text from KML/GeoJSON text that may hold HTML (CDATA descriptions, entities).</summary>
		public static string PlainText(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var text = LineBreakTags.Replace(value, "\n");
			text = Tags.Replace(text, " ");
			text = WebUtility.HtmlDecode(text);

			var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
				.Select(line => Spaces.Replace(line, " ").Trim())
				.Where(line => line.Length > 0);

			var result = string.Join("\n", lines).Trim();
			return result.Length == 0 ? null : result;
		}

		private static string JoinDescription(string description, IEnumerable<string> extra)
		{
			var lines = new List<string>();
			if (!string.IsNullOrWhiteSpace(description))
				lines.Add(description);

			foreach (var line in extra ?? Enumerable.Empty<string>())
			{
				if (string.IsNullOrWhiteSpace(line))
					continue;

				// Google My Maps repeats the description (and name) as data fields: a value the text already holds adds nothing.
				var separator = line.IndexOf(": ", StringComparison.Ordinal);
				var value = separator >= 0 ? line.Substring(separator + 2) : line;
				if (description != null && description.Contains(value))
					continue;

				lines.Add(line);
			}

			return Trim(lines.Count == 0 ? null : string.Join("\n", lines), MaxDescriptionLength);
		}

		private static string Trim(string value, int length)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			value = value.Trim();
			return value.Length <= length ? value : value.Substring(0, length).TrimEnd();
		}

		#endregion Text

		/// <summary>Accumulates features and counts, and stops at the limits.</summary>
		private sealed class Builder
		{
			private int _positions;

			public MapLayerFileReadResult Result { get; } = new MapLayerFileReadResult();

			/// <summary>False (with the error set) when a limit is exceeded.</summary>
			public bool Add(IGeometryObject shape, string name, string description, string color, string category, JObject sourceProperties = null)
			{
				if (Result.ShapeCount + 1 > MapLayerFileLimits.MaxFeatures)
				{
					Result.Error = MapLayerFileReadErrors.TooManyShapes;
					Result.Features = new FeatureCollection();
					return false;
				}

				_positions += PositionCount(shape);
				if (_positions > MapLayerFileLimits.MaxPositions)
				{
					Result.Error = MapLayerFileReadErrors.TooManyPoints;
					Result.Features = new FeatureCollection();
					return false;
				}

				var properties = new Dictionary<string, object>();
				if (!string.IsNullOrWhiteSpace(name))
					properties["name"] = name;
				if (!string.IsNullOrWhiteSpace(description))
					properties["description"] = description;
				if (!string.IsNullOrWhiteSpace(color) && HexColor.IsMatch(color.Trim()))
					properties["color"] = color.Trim().ToLowerInvariant();
				if (!string.IsNullOrWhiteSpace(category))
					properties["category"] = category;

				switch (shape)
				{
					case GeoJSON.Net.Geometry.Point _:
						Result.Points++;
						// A layer exported from Resgrid keeps its circles and text labels.
						var shapeName = sourceProperties == null ? null : FirstString(sourceProperties, "shape");
						if (shapeName == "Circle" && double.TryParse(FirstString(sourceProperties, "radius"), NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) && radius > 0)
						{
							properties["shape"] = "Circle";
							properties["radius"] = radius;
						}
						else if (shapeName == "Text" && !string.IsNullOrWhiteSpace(FirstString(sourceProperties, "text")))
						{
							properties["shape"] = "Text";
							properties["text"] = Trim(PlainText(FirstString(sourceProperties, "text")), MaxNameLength);
						}
						break;
					case GeoJSON.Net.Geometry.LineString _:
						Result.Lines++;
						break;
					default:
						Result.Polygons++;
						break;
				}

				Result.Features.Features.Add(new GeoFeature(shape, properties));
				return true;
			}

			public MapLayerFileReadResult Finish()
			{
				if (Result.Success && Result.ShapeCount == 0)
					Result.Error = MapLayerFileReadErrors.NoShapes;

				return Result;
			}
		}
	}
}
