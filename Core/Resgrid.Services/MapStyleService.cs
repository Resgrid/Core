using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc />
	public class MapStyleService : IMapStyleService
	{
		private const string StyleCachePrefix = "MlStyle1_";
		private static readonly TimeSpan StyleCacheLength = TimeSpan.FromHours(6);
		private static readonly HttpClient Http = CreateClient();

		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICacheProvider _cacheProvider;

		// Lazy: the overrides service reads department settings, which reach map services while being built.
		private readonly Lazy<IDepartmentMapProvidersService> _departmentMapProviders;

		public MapStyleService(IDepartmentSettingsService departmentSettingsService, ICacheProvider cacheProvider,
			Lazy<IDepartmentMapProvidersService> departmentMapProviders)
		{
			_departmentSettingsService = departmentSettingsService;
			_cacheProvider = cacheProvider;
			_departmentMapProviders = departmentMapProviders;
		}

		public async Task<TrafficLayerSource> GetTrafficLayerAsync(int departmentId)
		{
			if (departmentId > 0 && _departmentMapProviders != null)
			{
				try
				{
					var own = (await _departmentMapProviders.Value.GetAsync(departmentId))?.Traffic;
					if (own != null)
						return own;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Reading department {departmentId}'s traffic layer failed; the install's layer is used");
				}
			}

			var system = GetTrafficTileUrl();
			return system == null ? null : new TrafficLayerSource { Url = system, Type = TrafficLayerSource.VectorType };
		}

		private static HttpClient CreateClient()
		{
			var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AutomaticDecompression = DecompressionMethods.All })
			{
				Timeout = TimeSpan.FromSeconds(15)
			};
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Resgrid/1.0 (+https://resgrid.com)");
			return client;
		}

		public async Task<MapLibreStyleInfo> ResolveAsync(int departmentId, string appKey)
		{
			var key = string.IsNullOrWhiteSpace(appKey) ? InfoConfig.WebsiteKey : appKey;
			var app = await _departmentSettingsService.GetAppMapConfigForDepartmentAsync(departmentId, key);

			var token = app?.AccessToken;
			if (string.IsNullOrWhiteSpace(token) && key == InfoConfig.WebsiteKey)
				token = MappingConfig.GetWebsiteMapboxPublicAccessToken();

			var provider = MappingConfig.ResolveTileProvider(!string.IsNullOrWhiteSpace(token), app?.IsDepartmentOverride == true);
			var info = new MapLibreStyleInfo
			{
				TileProvider = provider,
				RasterTileUrl = MappingConfig.GetSelfHostedRasterTileUrl(key),
				IsDepartmentOverride = app?.IsDepartmentOverride == true
			};

			if (provider == MappingConfig.MapboxMapProvider)
			{
				info.AccessToken = token;
				info.DayStyleSource = app?.DayStyleUrl;
				info.NightStyleSource = app?.NightStyleUrl;
				info.Attribution = MappingConfig.MapBoxAttribution;
				return info;
			}

			// Self-hosted: department presets map onto the install's style catalog by kind (plan §5.3).
			var day = MapStylePresets.ResolveDayStyle(await _departmentSettingsService.GetMappingMapStyleAsync(departmentId));
			var night = MapStylePresets.ResolveNightStyle(day, await _departmentSettingsService.GetMappingMapStyleNightAsync(departmentId));
			var catalog = ParseCatalog(MappingConfig.SelfHostedStyleCatalog);

			info.AccessToken = string.Empty;
			info.DayStyleSource = Lookup(catalog, MapStylePresets.StyleClass(day)) ?? NullIfEmpty(MappingConfig.SelfHostedStyleUrlDay);
			info.NightStyleSource = Lookup(catalog, MapStylePresets.StyleClass(night)) ?? NullIfEmpty(MappingConfig.SelfHostedStyleUrlNight) ?? info.DayStyleSource;
			info.Attribution = MappingConfig.GetSelfHostedAttribution();
			return info;
		}

		public async Task<string> GetStyleJsonAsync(int departmentId, string appKey, bool night, CancellationToken cancellationToken = default)
		{
			var info = await ResolveAsync(departmentId, appKey);
			var source = night ? info.NightStyleSource : info.DayStyleSource;

			try
			{
				if (info.TileProvider == MappingConfig.MapboxMapProvider)
				{
					var fetchUrl = MapboxStyleTransformer.StyleFetchUrl(source, info.AccessToken);
					if (fetchUrl != null)
						return await CachedAsync(fetchUrl, () => BuildMapboxStyleAsync(fetchUrl, info, cancellationToken));
				}
				else if (!string.IsNullOrWhiteSpace(source) && Uri.TryCreate(source, UriKind.Absolute, out var styleUri) &&
				         (styleUri.Scheme == Uri.UriSchemeHttps || styleUri.Scheme == Uri.UriSchemeHttp))
				{
					return await CachedAsync(source, () => BuildSelfHostedStyleAsync(source, info.Attribution, cancellationToken));
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Map style for department {departmentId} ({appKey}) could not be built from {(info.TileProvider == MappingConfig.MapboxMapProvider ? "Mapbox" : "MappingConfig.SelfHostedStyleUrlDay/Night or SelfHostedStyleCatalog")}; serving the raster style.");
			}

			return BuildRasterStyle(info.RasterTileUrl, info.Attribution);
		}

		public string GetSignedStyleUrl(int departmentId, string appKey, bool night)
		{
			var key = string.IsNullOrWhiteSpace(appKey) ? InfoConfig.WebsiteKey : appKey;
			var url = $"{SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Mapping/GetStyle?key={WebUtility.UrlEncode(key)}&variant={(night ? "night" : "day")}";

			return MapResourceUrls.Sign(url, departmentId, MapResourceUrls.StyleScope(key, night));
		}

		public string GetTrafficTileUrl()
		{
			if (!string.Equals(MappingConfig.TrafficProvider?.Trim(), MappingConfig.MapboxMapProvider, StringComparison.OrdinalIgnoreCase) ||
			    !MappingConfig.IsPublicMapboxAccessToken(MappingConfig.MapboxTrafficAccessToken))
				return null;

			return MapboxTrafficTileUrl(MappingConfig.MapboxTrafficAccessToken);
		}

		/// <summary>Mapbox Traffic v1 vector tiles (source-layer "traffic") with a public (pk.) token.</summary>
		public static string MapboxTrafficTileUrl(string publicToken)
		{
			return "https://api.mapbox.com/v4/mapbox.mapbox-traffic-v1/{z}/{x}/{y}.vector.pbf?access_token=" +
			       WebUtility.UrlEncode(publicToken.Trim());
		}

		private async Task<string> BuildMapboxStyleAsync(string fetchUrl, MapLibreStyleInfo info, CancellationToken cancellationToken)
		{
			var input = JsonNode.Parse(await Http.GetStringAsync(fetchUrl, cancellationToken)) as JsonObject;
			if (input == null)
				throw new InvalidOperationException("Mapbox style is not a JSON object");

			var style = MapboxStyleTransformer.Transform(input, info.AccessToken, info.Attribution);

			// Inline each source's TileJSON so the tile URLs carry the token and MapLibre needs no extra round trip.
			if (style["sources"] is JsonObject sources)
			{
				foreach (var (_, node) in sources)
				{
					if (node is not JsonObject source || source["url"] is not JsonValue urlValue || !urlValue.TryGetValue<string>(out var tileJsonUrl) ||
					    !tileJsonUrl.StartsWith("https://api.mapbox.com/", StringComparison.OrdinalIgnoreCase))
						continue;

					try
					{
						if (JsonNode.Parse(await Http.GetStringAsync(tileJsonUrl, cancellationToken)) is not JsonObject tileJson || tileJson["tiles"] is not JsonArray tiles)
							continue;

						var tokenized = new JsonArray();
						foreach (var tile in tiles)
						{
							if (tile is JsonValue value && value.TryGetValue<string>(out var tileUrl))
								tokenized.Add(MapboxStyleTransformer.RewriteUrl(tileUrl, info.AccessToken, MapboxStyleTransformer.ResourceKind.Tiles));
						}

						source.Remove("url");
						source["tiles"] = tokenized;
						foreach (var copy in new[] { "minzoom", "maxzoom", "bounds", "scheme" })
						{
							if (tileJson[copy] != null && source[copy] == null)
								source[copy] = tileJson[copy].DeepClone();
						}
					}
					catch (HttpRequestException)
					{
						// Leave the TileJSON URL in place; MapLibre fetches it itself.
					}
				}
			}

			return style.ToJsonString();
		}

		private static async Task<string> BuildSelfHostedStyleAsync(string url, string attribution, CancellationToken cancellationToken)
		{
			if (JsonNode.Parse(await Http.GetStringAsync(url, cancellationToken)) is not JsonObject style)
				throw new InvalidOperationException("Self-hosted style is not a JSON object");

			MapboxStyleTransformer.InjectAttribution(style, attribution);
			return style.ToJsonString();
		}

		/// <summary>A one-layer MapLibre style over raster XYZ tiles.</summary>
		public static string BuildRasterStyle(string tileUrl, string attribution)
		{
			var tiles = new JsonArray();
			if (!string.IsNullOrWhiteSpace(tileUrl))
			{
				// {s} subdomain templates (Leaflet) become explicit hosts.
				if (tileUrl.Contains("{s}"))
				{
					foreach (var subdomain in new[] { "a", "b", "c" })
						tiles.Add(tileUrl.Replace("{s}", subdomain));
				}
				else
				{
					tiles.Add(tileUrl);
				}
			}

			var style = new JsonObject
			{
				["version"] = 8,
				["name"] = "Resgrid raster",
				["sources"] = new JsonObject
				{
					["base"] = new JsonObject
					{
						["type"] = "raster",
						["tiles"] = tiles,
						["tileSize"] = 256,
						["maxzoom"] = 19,
						["attribution"] = attribution ?? string.Empty
					}
				},
				["layers"] = new JsonArray(new JsonObject { ["id"] = "base", ["type"] = "raster", ["source"] = "base" })
			};

			return style.ToJsonString();
		}

		private async Task<string> CachedAsync(string sourceKey, Func<Task<string>> build)
		{
			var cacheKey = StyleCachePrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey)), 0, 16).ToLowerInvariant();

			if (SystemBehaviorConfig.CacheEnabled && _cacheProvider != null)
			{
				try
				{
					var cached = await _cacheProvider.GetStringAsync(cacheKey);
					if (!string.IsNullOrWhiteSpace(cached))
						return cached;
				}
				catch
				{
					// Build it instead.
				}
			}

			var json = await build();

			if (SystemBehaviorConfig.CacheEnabled && _cacheProvider != null)
			{
				try
				{
					await _cacheProvider.SetStringAsync(cacheKey, json, StyleCacheLength);
				}
				catch
				{
					// A cache outage only costs a rebuild.
				}
			}

			return json;
		}

		public string GetPresetTileProvider()
		{
			return MappingConfig.ResolveTileProvider(MappingConfig.HasSystemMapboxAccessToken(InfoConfig.WebsiteKey), false);
		}

		public IReadOnlyList<MapStyleTypes> GetAvailablePresets()
		{
			if (GetPresetTileProvider() == MappingConfig.MapboxMapProvider)
				return MapStylePresets.SelectableStyles;

			var catalog = ParseCatalog(MappingConfig.SelfHostedStyleCatalog);
			return MapStylePresets.SelectableStyles
				.Where(style => MapStylePresets.StyleClass(style) is string kind && (kind != "satellite" || Lookup(catalog, kind) != null))
				.ToList();
		}

		public string GetPresetSource(MapStyleTypes style, bool night)
		{
			if (GetPresetTileProvider() == MappingConfig.MapboxMapProvider)
				return MapStylePresets.GetStyleUrl(style);

			var fallback = night ? NullIfEmpty(MappingConfig.SelfHostedStyleUrlNight) ?? NullIfEmpty(MappingConfig.SelfHostedStyleUrlDay) : NullIfEmpty(MappingConfig.SelfHostedStyleUrlDay);
			return Lookup(ParseCatalog(MappingConfig.SelfHostedStyleCatalog), MapStylePresets.StyleClass(style)) ?? fallback;
		}

		private static Dictionary<string, string> ParseCatalog(string json)
		{
			var catalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrWhiteSpace(json))
				return catalog;

			try
			{
				using var document = JsonDocument.Parse(json);
				foreach (var property in document.RootElement.EnumerateObject())
				{
					if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
						catalog[property.Name] = property.Value.GetString();
				}
			}
			catch (JsonException)
			{
				Logging.LogError("MappingConfig.SelfHostedStyleCatalog is not a JSON object of preset → style URL; ignoring it.");
			}

			return catalog;
		}

		private static string Lookup(Dictionary<string, string> catalog, string presetClass)
		{
			return presetClass != null && catalog.TryGetValue(presetClass, out var url) ? url : null;
		}

		private static string NullIfEmpty(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}
	}
}
