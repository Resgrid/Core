using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Resgrid.Services
{
	/// <inheritdoc />
	public class StaticMapService : IStaticMapService
	{
		private const int TileSize = 256;
		private const int MaxSide = 1280;
		private static readonly HttpClient Http = CreateClient();

		private readonly IMapStyleService _mapStyleService;

		public StaticMapService(IMapStyleService mapStyleService)
		{
			_mapStyleService = mapStyleService;
		}

		private static HttpClient CreateClient()
		{
			var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(15) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Resgrid/1.0 (+https://resgrid.com)");
			return client;
		}

		public string GetSignedTemplate(int departmentId, string appKey)
		{
			var key = string.IsNullOrWhiteSpace(appKey) ? InfoConfig.WebsiteKey : appKey;
			var url = $"{SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Mapping/GetStaticMap?key={WebUtility.UrlEncode(key)}" +
			          "&lat={lat}&lon={lon}&zoom={zoom}&width={width}&height={height}&scale={scale}&scheme={scheme}";

			return MapResourceUrls.Sign(url, departmentId, MapResourceUrls.StaticMapScope);
		}

		public async Task<StaticMapImage> GetStaticMapAsync(int departmentId, string appKey, double latitude, double longitude, double zoom, int width, int height,
			int scale, bool night, bool marker, CancellationToken cancellationToken = default)
		{
			width = Math.Clamp(width, 32, MaxSide);
			height = Math.Clamp(height, 32, MaxSide);
			scale = scale >= 2 ? 2 : 1;
			zoom = Math.Clamp(zoom, 0, 20);

			var info = await _mapStyleService.ResolveAsync(departmentId, appKey);
			var source = night ? info.NightStyleSource : info.DayStyleSource;

			var image = info.TileProvider == MappingConfig.MapboxMapProvider
				? await MapboxStaticAsync(source, info.AccessToken, latitude, longitude, zoom, width, height, scale, marker, cancellationToken)
				: await TileServerStaticAsync(source, latitude, longitude, zoom, width, height, scale, marker, cancellationToken);

			return image ?? await StitchRasterAsync(info.RasterTileUrl, latitude, longitude, zoom, width, height, scale, marker, cancellationToken);
		}

		public async Task<StaticMapImage> GetStylePreviewAsync(MapStyleTypes style, bool night, double latitude, double longitude, double zoom, int width, int height,
			int scale, CancellationToken cancellationToken = default)
		{
			width = Math.Clamp(width, 32, MaxSide);
			height = Math.Clamp(height, 32, MaxSide);
			scale = scale >= 2 ? 2 : 1;
			zoom = Math.Clamp(zoom, 0, 20);

			// The preset as the install renders it, whatever the department has saved or overridden (the page previews a choice).
			var source = _mapStyleService.GetPresetSource(MapStylePresets.Normalize(style), night);

			var image = _mapStyleService.GetPresetTileProvider() == MappingConfig.MapboxMapProvider
				? await MapboxStaticAsync(source, MappingConfig.GetWebsiteMapboxPublicAccessToken(), latitude, longitude, zoom, width, height, scale, false, cancellationToken)
				: await TileServerStaticAsync(source, latitude, longitude, zoom, width, height, scale, false, cancellationToken);

			return image ?? await StitchRasterAsync(MappingConfig.GetSelfHostedRasterTileUrl(InfoConfig.WebsiteKey), latitude, longitude, zoom, width, height, scale, false, cancellationToken);
		}

		/// <summary>Mapbox Static Images for a mapbox:// style; null when the style is not one or the request fails.</summary>
		private static async Task<StaticMapImage> MapboxStaticAsync(string source, string accessToken, double latitude, double longitude, double zoom, int width, int height,
			int scale, bool marker, CancellationToken cancellationToken)
		{
			var (owner, id) = MapboxStyleTransformer.ParseStyleUrl(source);
			if (owner == null || string.IsNullOrWhiteSpace(accessToken))
				return null;

			var lon = longitude.ToString("0.######", CultureInfo.InvariantCulture);
			var lat = latitude.ToString("0.######", CultureInfo.InvariantCulture);
			var overlay = marker ? $"pin-s+e74c3c({lon},{lat})/" : string.Empty;
			var url = $"https://api.mapbox.com/styles/v1/{owner}/{id}/static/{overlay}{lon},{lat},{zoom.ToString("0.##", CultureInfo.InvariantCulture)},0/" +
			          $"{width}x{height}{(scale == 2 ? "@2x" : string.Empty)}?access_token={WebUtility.UrlEncode(accessToken)}";

			using var response = await Http.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
				return null;

			return new StaticMapImage { Bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken), ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/png" };
		}

		/// <summary>
		/// TileServer GL's static image for a self-hosted style served at …/styles/{id}/style.json (plan §6.6); null for any
		/// other style source or a failed request. The pin is drawn here, as TileServer GL markers need configured icons.
		/// </summary>
		private static async Task<StaticMapImage> TileServerStaticAsync(string source, double latitude, double longitude, double zoom, int width, int height,
			int scale, bool marker, CancellationToken cancellationToken)
		{
			var url = TileServerStaticUrl(source, latitude, longitude, zoom, width, height, scale);
			if (url == null)
				return null;

			try
			{
				using var response = await Http.GetAsync(url, cancellationToken);
				if (!response.IsSuccessStatusCode)
					return null;

				var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
				if (!marker)
					return new StaticMapImage { Bytes = bytes, ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/png" };

				using var image = Image.Load<Rgba32>(bytes);
				DrawMarker(image, image.Width / 2, image.Height / 2, 7 * scale);
				using var output = new MemoryStream();
				await image.SaveAsPngAsync(output, cancellationToken);
				return new StaticMapImage { Bytes = output.ToArray(), ContentType = "image/png" };
			}
			catch (HttpRequestException)
			{
				return null;
			}
			catch (UnknownImageFormatException)
			{
				return null;
			}
		}

		/// <summary>The TileServer GL static image URL for a style JSON URL ending /styles/{id}/style.json; null otherwise.</summary>
		public static string TileServerStaticUrl(string source, double latitude, double longitude, double zoom, int width, int height, int scale)
		{
			if (string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) ||
			    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
				return null;

			var path = uri.AbsolutePath;
			const string suffix = "/style.json";
			var styles = path.LastIndexOf("/styles/", StringComparison.OrdinalIgnoreCase);
			if (styles < 0 || !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || styles + "/styles/".Length >= path.Length - suffix.Length)
				return null;

			var stylePath = path.Substring(0, path.Length - suffix.Length);
			var lon = longitude.ToString("0.######", CultureInfo.InvariantCulture);
			var lat = latitude.ToString("0.######", CultureInfo.InvariantCulture);
			return $"{uri.Scheme}://{uri.Authority}{stylePath}/static/{lon},{lat},{zoom.ToString("0.##", CultureInfo.InvariantCulture)}/" +
			       $"{width}x{height}{(scale == 2 ? "@2x" : string.Empty)}.png{uri.Query}";
		}

		/// <summary>Builds the image from 256 px raster tiles (a GL zoom z is raster zoom z + 1 at the same scale).</summary>
		private static async Task<StaticMapImage> StitchRasterAsync(string tileUrl, double latitude, double longitude, double zoom, int width, int height, int scale,
			bool marker, CancellationToken cancellationToken)
		{
			var outWidth = width * scale;
			var outHeight = height * scale;
			var rasterZoom = (int)Math.Clamp(Math.Round(zoom + scale), 0, 19);
			var worldSize = TileSize * Math.Pow(2, rasterZoom);

			var centerX = (longitude + 180.0) / 360.0 * worldSize;
			var sinLat = Math.Sin(latitude * Math.PI / 180.0);
			var centerY = (0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI)) * worldSize;

			var left = centerX - outWidth / 2.0;
			var top = centerY - outHeight / 2.0;
			var firstTileX = (int)Math.Floor(left / TileSize);
			var firstTileY = (int)Math.Floor(top / TileSize);
			var lastTileX = (int)Math.Floor((left + outWidth - 1) / TileSize);
			var lastTileY = (int)Math.Floor((top + outHeight - 1) / TileSize);
			var maxTile = (int)Math.Pow(2, rasterZoom) - 1;

			using var canvas = new Image<Rgba32>(outWidth, outHeight, new Rgba32(230, 230, 230));
			var tiles = new ConcurrentBag<(int X, int Y, byte[] Bytes)>();
			var fetches = new System.Collections.Generic.List<Task>();

			for (var tx = firstTileX; tx <= lastTileX; tx++)
			{
				for (var ty = firstTileY; ty <= lastTileY; ty++)
				{
					if (ty < 0 || ty > maxTile || string.IsNullOrWhiteSpace(tileUrl))
						continue;

					var wrappedX = ((tx % (maxTile + 1)) + maxTile + 1) % (maxTile + 1);
					var url = tileUrl.Replace("{s}", "a").Replace("{z}", rasterZoom.ToString(CultureInfo.InvariantCulture))
						.Replace("{x}", wrappedX.ToString(CultureInfo.InvariantCulture)).Replace("{y}", ty.ToString(CultureInfo.InvariantCulture));
					var x = tx;
					var y = ty;

					fetches.Add(Task.Run(async () =>
					{
						try
						{
							using var response = await Http.GetAsync(url, cancellationToken);
							if (response.IsSuccessStatusCode)
								tiles.Add((x, y, await response.Content.ReadAsByteArrayAsync(cancellationToken)));
						}
						catch (HttpRequestException)
						{
							// A missing tile leaves a grey square.
						}
					}, cancellationToken));
				}
			}

			await Task.WhenAll(fetches);

			foreach (var (x, y, bytes) in tiles)
			{
				try
				{
					using var tile = Image.Load<Rgba32>(bytes);
					var offsetX = (int)Math.Round(x * TileSize - left);
					var offsetY = (int)Math.Round(y * TileSize - top);
					canvas.Mutate(context => context.DrawImage(tile, new Point(offsetX, offsetY), 1f));
				}
				catch (Exception)
				{
					// Unreadable tile; skip it.
				}
			}

			if (marker)
				DrawMarker(canvas, outWidth / 2, outHeight / 2, 7 * scale);

			using var output = new MemoryStream();
			await canvas.SaveAsPngAsync(output, cancellationToken);
			return new StaticMapImage { Bytes = output.ToArray(), ContentType = "image/png" };
		}

		// A filled red dot with a white ring, drawn per pixel so no drawing package is needed.
		private static void DrawMarker(Image<Rgba32> canvas, int cx, int cy, int radius)
		{
			var outer = radius + Math.Max(2, radius / 3);
			var red = new Rgba32(231, 76, 60);
			var white = new Rgba32(255, 255, 255);

			canvas.ProcessPixelRows(rows =>
			{
				for (var y = Math.Max(0, cy - outer); y <= Math.Min(rows.Height - 1, cy + outer); y++)
				{
					var row = rows.GetRowSpan(y);
					for (var x = Math.Max(0, cx - outer); x <= Math.Min(rows.Width - 1, cx + outer); x++)
					{
						var distance = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
						if (distance <= radius)
							row[x] = red;
						else if (distance <= outer)
							row[x] = white;
					}
				}
			});
		}
	}
}
