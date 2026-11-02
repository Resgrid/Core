using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Resgrid.Services
{
	/// <summary>
	/// Turns a Mapbox style JSON into one MapLibre can load (maplibre-mapping-routing-navigation-plan.md D6): every
	/// mapbox:// style, source, sprite, glyph and tile URL is rewritten to https://api.mapbox.com with the public token,
	/// and Mapbox-only root keys, layer types, source types and properties are removed. The rules match Omni's client-side
	/// fallback (packages/maps/src/provider/mapbox-compat.ts) so both paths render the same.
	/// </summary>
	public static class MapboxStyleTransformer
	{
		private const string MapboxApi = "https://api.mapbox.com";

		private static readonly Regex StyleUrl = new Regex(@"^mapbox://styles/([^/?#]+)/([^/?#]+)/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex HttpsStyleUrl = new Regex(@"^https://api\.mapbox\.com/styles/v1/([^/?#]+)/([^/?#]+)/?(\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex MapboxHttps = new Regex(@"^https://([a-z0-9-]+\.)*(api\.)?mapbox\.com/", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
		{
			"version", "name", "metadata", "center", "centerAltitude", "zoom", "bearing", "pitch", "roll", "light", "sky",
			"sources", "sprite", "glyphs", "transition", "layers", "state", "font-faces"
		};

		private static readonly HashSet<string> LayerTypes = new HashSet<string>(StringComparer.Ordinal)
		{
			"background", "fill", "line", "symbol", "circle", "heatmap", "fill-extrusion", "raster", "hillshade", "color-relief"
		};

		private static readonly HashSet<string> SourceTypes = new HashSet<string>(StringComparer.Ordinal)
		{
			"vector", "raster", "raster-dem", "geojson", "image", "video"
		};

		private static readonly string[] MapboxOnlyPropertyParts =
		{
			"emissive-strength", "z-offset", "z-elevate", "elevation-reference", "occlusion-opacity", "cross-fade", "use-theme",
			"ambient-occlusion", "flood-light", "rounded-roof", "edge-radius", "vertical-scale", "cutoff-fade-range", "line-trim-",
			"line-border-", "raster-color", "raster-array-band", "raster-elevation", "tunnel-structure", "clip-layer-"
		};

		/// <summary>The https URL a mapbox:// (or api.mapbox.com) style is fetched from, with the token; null when not a Mapbox style.</summary>
		public static string StyleFetchUrl(string styleUrl, string token)
		{
			var (owner, id) = ParseStyleUrl(styleUrl);
			return owner == null ? null : WithToken($"{MapboxApi}/styles/v1/{owner}/{id}", token);
		}

		public static (string Owner, string Id) ParseStyleUrl(string styleUrl)
		{
			var value = styleUrl?.Trim() ?? string.Empty;
			var match = StyleUrl.Match(value);
			if (!match.Success)
				match = HttpsStyleUrl.Match(value);

			return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (null, null);
		}

		public enum ResourceKind
		{
			Style,
			Source,
			Sprite,
			Glyphs,
			Tiles
		}

		public static string RewriteUrl(string url, string token, ResourceKind kind)
		{
			var trimmed = url?.Trim() ?? string.Empty;

			if (MapboxHttps.IsMatch(trimmed))
				return WithToken(trimmed, token);

			if (!trimmed.StartsWith("mapbox://", StringComparison.OrdinalIgnoreCase))
				return trimmed;

			var rest = trimmed.Substring("mapbox://".Length);

			switch (kind)
			{
				case ResourceKind.Style:
				{
					var (owner, id) = ParseStyleUrl(trimmed);
					return owner == null ? trimmed : WithToken($"{MapboxApi}/styles/v1/{owner}/{id}", token);
				}
				case ResourceKind.Sprite:
				{
					var path = Regex.Replace(rest, "^sprites/", string.Empty, RegexOptions.IgnoreCase);
					var parts = path.Split('/');
					var suffix = parts.Length > 2 ? "/" + string.Join("/", parts.Skip(2)) : string.Empty;
					return parts.Length >= 2 ? WithToken($"{MapboxApi}/styles/v1/{parts[0]}/{parts[1]}{suffix}/sprite", token) : trimmed;
				}
				case ResourceKind.Glyphs:
					return WithToken($"{MapboxApi}/fonts/v1/{Regex.Replace(rest, "^fonts/", string.Empty, RegexOptions.IgnoreCase)}", token);
				case ResourceKind.Tiles:
					return WithToken($"{MapboxApi}/v4/{Regex.Replace(rest, "^tiles/", string.Empty, RegexOptions.IgnoreCase)}", token);
				default:
					return WithToken($"{MapboxApi}/v4/{rest}.json?secure", token);
			}
		}

		/// <summary>Rewrites and cleans a Mapbox style for MapLibre.</summary>
		public static JsonObject Transform(JsonObject input, string token, string attribution)
		{
			var style = new JsonObject();

			foreach (var (key, value) in input)
			{
				if (RootKeys.Contains(key))
					style[key] = value?.DeepClone();
			}

			style["version"] = 8;

			var sources = new JsonObject();
			if (input["sources"] is JsonObject inputSources)
			{
				foreach (var (id, source) in inputSources)
				{
					var rewritten = RewriteSource(source as JsonObject, token, attribution);
					if (rewritten != null)
						sources[id] = rewritten;
				}
			}

			style["sources"] = sources;

			if (style["sprite"] is JsonValue spriteValue && spriteValue.TryGetValue<string>(out var sprite))
				style["sprite"] = RewriteUrl(sprite, token, ResourceKind.Sprite);
			else if (style["sprite"] is JsonArray sprites)
			{
				foreach (var entry in sprites.OfType<JsonObject>())
				{
					if (entry["url"] is JsonValue url && url.TryGetValue<string>(out var spriteUrl))
						entry["url"] = RewriteUrl(spriteUrl, token, ResourceKind.Sprite);
				}
			}

			if (style["glyphs"] is JsonValue glyphValue && glyphValue.TryGetValue<string>(out var glyphs))
				style["glyphs"] = RewriteUrl(glyphs, token, ResourceKind.Glyphs);

			var layers = new JsonArray();
			if (input["layers"] is JsonArray inputLayers)
			{
				foreach (var layer in inputLayers.OfType<JsonObject>())
				{
					var type = (layer["type"] as JsonValue)?.GetValue<string>();
					if (type == null || !LayerTypes.Contains(type))
						continue;

					var source = (layer["source"] as JsonValue)?.TryGetValue<string>(out var sourceId) == true ? sourceId : null;
					if (source != null && type != "background" && !sources.ContainsKey(source))
						continue;

					var next = (JsonObject)layer.DeepClone();
					next.Remove("slot");
					CleanProperties(next, "paint");
					CleanProperties(next, "layout");
					layers.Add(next);
				}
			}

			style["layers"] = layers;
			return style;
		}

		/// <summary>Adds the attribution to every non-GeoJSON source of a self-hosted style that lacks one.</summary>
		public static void InjectAttribution(JsonObject style, string attribution)
		{
			if (string.IsNullOrWhiteSpace(attribution) || style["sources"] is not JsonObject sources)
				return;

			foreach (var (_, node) in sources)
			{
				if (node is JsonObject source && source["attribution"] == null && (source["type"] as JsonValue)?.GetValue<string>() != "geojson")
					source["attribution"] = attribution;
			}
		}

		private static JsonObject RewriteSource(JsonObject source, string token, string attribution)
		{
			var type = (source?["type"] as JsonValue)?.TryGetValue<string>(out var sourceType) == true ? sourceType : null;
			if (type == null || !SourceTypes.Contains(type))
				return null;

			var next = (JsonObject)source.DeepClone();

			if (next["url"] is JsonValue url && url.TryGetValue<string>(out var sourceUrl))
				next["url"] = RewriteUrl(sourceUrl, token, ResourceKind.Source);

			if (next["tiles"] is JsonArray tiles)
			{
				var rewritten = new JsonArray();
				foreach (var tile in tiles)
					rewritten.Add(tile is JsonValue value && value.TryGetValue<string>(out var tileUrl) ? RewriteUrl(tileUrl, token, ResourceKind.Tiles) : tile?.DeepClone());
				next["tiles"] = rewritten;
			}

			if (!string.IsNullOrWhiteSpace(attribution) && next["attribution"] == null && type != "geojson")
				next["attribution"] = attribution;

			return next;
		}

		private static void CleanProperties(JsonObject layer, string name)
		{
			if (layer[name] is not JsonObject properties)
				return;

			foreach (var key in properties.Select(p => p.Key).ToList())
			{
				if (MapboxOnlyPropertyParts.Any(part => key.Contains(part, StringComparison.Ordinal)))
					properties.Remove(key);
			}
		}

		private static string WithToken(string url, string token)
		{
			if (string.IsNullOrWhiteSpace(token) || Regex.IsMatch(url, "[?&]access_token="))
				return url;

			return url + (url.Contains('?') ? "&" : "?") + "access_token=" + WebUtility.UrlEncode(token);
		}
	}
}
