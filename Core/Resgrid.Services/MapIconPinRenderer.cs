using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// Draws the styled built-in map pins and the status badges on uploaded type icons, on request, for the anonymous pin
	/// and icon endpoints of the web app and the API.
	/// <para>
	/// A styled pin is the built-in pin artwork (Resources/MapPins, the same 32 x 37 PNGs the web map and the apps bundle)
	/// recoloured to the style's colour: the pin body's own colour is found, every darker pixel (its border and shading)
	/// keeps its shade of the new colour and every lighter one (the white glyph and its anti-aliasing) keeps its blend
	/// towards white. A badge is a ring in the style's colour at the lower right corner of an uploaded 128 px icon, so a type
	/// with only its own icon still shows the status or priority at a glance.
	/// </para>
	/// Both are deterministic, so results are cached for the life of the process; the pin set is bounded (artworks x styles)
	/// and the badge cache is capped.
	/// </summary>
	public static class MapIconPinRenderer
	{
		private const string ResourcePrefix = "Resgrid.Services.MapPins.";
		private const int BadgeCacheLimit = 2000;

		private static readonly ConcurrentDictionary<string, byte[]> PinCache = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
		private static readonly ConcurrentDictionary<string, byte[]> BadgeCache = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

		/// <summary>True when a pin artwork of that name ships with the renderer.</summary>
		public static bool HasArtwork(string pinBase) =>
			pinBase != null && typeof(MapIconPinRenderer).Assembly.GetManifestResourceInfo(ResourcePrefix + pinBase + ".png") != null;

		/// <summary>The styled pin named <paramref name="pinName"/> ("fire-high") as PNG bytes, or null when there is no such pin.</summary>
		public static byte[] RenderPin(string pinName)
		{
			if (!TypeIcons.TryParsePinName(pinName, out var pinBase, out var style) || !HasArtwork(pinBase))
				return null;

			return PinCache.GetOrAdd(pinName, _ => DrawPin(pinBase, style));
		}

		/// <summary>
		/// <paramref name="iconPng"/> (an uploaded icon, already a square PNG) with a <paramref name="style"/> badge, as PNG
		/// bytes; <paramref name="cacheKey"/> (the icon's checksum) keys the cache. Null for an unknown style or unreadable image.
		/// </summary>
		public static byte[] RenderBadge(byte[] iconPng, string style, string cacheKey)
		{
			if (iconPng == null || iconPng.Length == 0 || !MapIconStyles.IsStyle(style))
				return null;

			var key = $"{cacheKey}:{style}";
			if (cacheKey != null && BadgeCache.TryGetValue(key, out var cached))
				return cached;

			byte[] rendered;
			try
			{
				rendered = DrawBadge(iconPng, style);
			}
			catch (Exception)
			{
				return null;
			}

			if (cacheKey != null)
			{
				if (BadgeCache.Count >= BadgeCacheLimit)
					BadgeCache.Clear();

				BadgeCache[key] = rendered;
			}

			return rendered;
		}

		private static byte[] DrawPin(string pinBase, string style)
		{
			using var stream = typeof(MapIconPinRenderer).Assembly.GetManifestResourceStream(ResourcePrefix + pinBase + ".png");
			using var image = Image.Load<Rgba32>(stream);

			var target = ParseColor(MapIconStyles.ColorOf(style));
			var body = DominantBodyColor(image);
			var bodyLuminance = Luminance(body);

			image.ProcessPixelRows(accessor =>
			{
				for (var y = 0; y < accessor.Height; y++)
				{
					var row = accessor.GetRowSpan(y);
					for (var x = 0; x < row.Length; x++)
					{
						var pixel = row[x];
						if (pixel.A == 0)
							continue;

						var luminance = Luminance(pixel);
						if (luminance >= bodyLuminance)
						{
							// The glyph and its edges: the same blend towards white, over the new colour.
							var t = bodyLuminance >= 255 ? 1d : Math.Min(1d, (luminance - bodyLuminance) / (255d - bodyLuminance));
							row[x] = new Rgba32(Blend(target.R, 255, t), Blend(target.G, 255, t), Blend(target.B, 255, t), pixel.A);
						}
						else
						{
							// The border and shading: the same shade of the new colour.
							var k = bodyLuminance <= 0 ? 0d : Math.Max(0d, luminance / bodyLuminance);
							row[x] = new Rgba32(Scale(target.R, k), Scale(target.G, k), Scale(target.B, k), pixel.A);
						}
					}
				}
			});

			return EncodePng(image);
		}

		private static byte[] DrawBadge(byte[] iconPng, string style)
		{
			using var image = Image.Load<Rgba32>(iconPng);

			if (image.Width != image.Height || image.Width != TypeIcons.StoredSize)
				image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(TypeIcons.StoredSize, TypeIcons.StoredSize), Mode = ResizeMode.Pad, PadColor = Color.Transparent }));

			var fill = ParseColor(MapIconStyles.ColorOf(style));
			var white = new Rgba32(255, 255, 255, 255);

			// At 32 px on the map the ring is about 12 px across: big enough to read, small enough to leave the icon visible.
			var size = image.Width;
			var outer = size * 0.19;
			var inner = outer - size * 0.045;
			var centre = size - outer - 1;

			image.ProcessPixelRows(accessor =>
			{
				var top = (int)Math.Floor(centre - outer - 1);
				for (var y = Math.Max(0, top); y < accessor.Height; y++)
				{
					var row = accessor.GetRowSpan(y);
					for (var x = Math.Max(0, top); x < row.Length; x++)
					{
						var distance = Math.Sqrt(Math.Pow(x + 0.5 - centre, 2) + Math.Pow(y + 0.5 - centre, 2));
						var outerCoverage = Math.Clamp(outer - distance + 0.5, 0d, 1d);
						if (outerCoverage <= 0)
							continue;

						var innerCoverage = Math.Clamp(inner - distance + 0.5, 0d, 1d);
						var ring = Mix(white, fill, innerCoverage);
						row[x] = Over(ring, outerCoverage, row[x]);
					}
				}
			});

			return EncodePng(image);
		}

		private static Rgba32 DominantBodyColor(Image<Rgba32> image)
		{
			var counts = new Dictionary<Rgba32, int>();
			image.ProcessPixelRows(accessor =>
			{
				for (var y = 0; y < accessor.Height; y++)
				{
					foreach (var pixel in accessor.GetRowSpan(y))
					{
						// Opaque and not part of the white glyph.
						if (pixel.A <= 250 || Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) >= 200)
							continue;

						var opaque = new Rgba32(pixel.R, pixel.G, pixel.B, 255);
						counts[opaque] = counts.TryGetValue(opaque, out var count) ? count + 1 : 1;
					}
				}
			});

			return counts.Count == 0 ? new Rgba32(128, 128, 128, 255) : counts.OrderByDescending(x => x.Value).First().Key;
		}

		private static Rgba32 ParseColor(string hex) =>
			new Rgba32(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), 255);

		private static double Luminance(Rgba32 c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

		private static byte Blend(byte from, byte to, double t) => (byte)Math.Round(from + t * (to - from));

		private static byte Scale(byte value, double k) => (byte)Math.Round(Math.Min(255d, value * k));

		private static Rgba32 Mix(Rgba32 outside, Rgba32 inside, double insideCoverage) =>
			new Rgba32(Blend(outside.R, inside.R, insideCoverage), Blend(outside.G, inside.G, insideCoverage), Blend(outside.B, inside.B, insideCoverage), 255);

		// Source-over compositing of an opaque colour with partial coverage onto a straight-alpha pixel.
		private static Rgba32 Over(Rgba32 source, double coverage, Rgba32 destination)
		{
			var destinationAlpha = destination.A / 255d;
			var alpha = coverage + destinationAlpha * (1 - coverage);
			if (alpha <= 0)
				return destination;

			byte Channel(byte s, byte d) => (byte)Math.Round((s * coverage + d * destinationAlpha * (1 - coverage)) / alpha);

			return new Rgba32(Channel(source.R, destination.R), Channel(source.G, destination.G), Channel(source.B, destination.B), (byte)Math.Round(alpha * 255));
		}

		private static byte[] EncodePng(Image<Rgba32> image)
		{
			using var output = new MemoryStream();
			image.Save(output, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, SkipMetadata = true });
			return output.ToArray();
		}
	}
}
