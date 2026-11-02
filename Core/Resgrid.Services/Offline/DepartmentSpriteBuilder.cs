using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Resgrid.Services.Offline
{
	/// <summary>
	/// The department sprite for offline maps (maplibre-mapping-routing-navigation-plan.md §19.2, kind Sprite): every
	/// uploaded unit-type, call-type, POI-type and role icon packed into one sheet per pixel ratio, with the MapLibre sprite
	/// index (name → x, y, width, height, pixelRatio). Images are named by their icon key, the value the map data carries.
	/// </summary>
	public static class DepartmentSpriteBuilder
	{
		private const int MaxSheetWidth = 1024;
		private const int Padding = 2;

		/// <summary>The four sprite files: sprite.json, sprite.png, sprite@2x.json, sprite@2x.png.</summary>
		public static Dictionary<string, byte[]> Build(IEnumerable<(string Name, byte[] Image)> icons)
		{
			var decoded = new List<(string Name, Image<Rgba32> Image)>();

			try
			{
				foreach (var (name, bytes) in icons ?? Enumerable.Empty<(string, byte[])>())
				{
					if (string.IsNullOrWhiteSpace(name) || bytes == null || bytes.Length == 0 || decoded.Any(d => d.Name == name))
						continue;

					try
					{
						decoded.Add((name, Image.Load<Rgba32>(bytes)));
					}
					catch (Exception)
					{
						// An icon that cannot be decoded is left out; the map shows the type's default pin.
					}
				}

				var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
				foreach (var ratio in new[] { 1, 2 })
				{
					var (png, json) = Sheet(decoded, ratio);
					var suffix = ratio == 1 ? string.Empty : "@2x";
					files["sprite" + suffix + ".json"] = json;
					files["sprite" + suffix + ".png"] = png;
				}

				return files;
			}
			finally
			{
				foreach (var (_, image) in decoded)
					image.Dispose();
			}
		}

		private static (byte[] Png, byte[] Json) Sheet(List<(string Name, Image<Rgba32> Image)> icons, int ratio)
		{
			// Icons are uploaded at map-marker size (about 32-64 px); 1x keeps them under 48 px, 2x doubles that.
			var sized = icons.Select(icon =>
			{
				var scale = Math.Min(1.0, 48.0 / Math.Max(icon.Image.Width, icon.Image.Height)) * ratio;
				var width = Math.Max(1, (int)Math.Round(icon.Image.Width * scale));
				var height = Math.Max(1, (int)Math.Round(icon.Image.Height * scale));
				return (icon.Name, icon.Image, Width: width, Height: height);
			}).OrderByDescending(i => i.Height).ThenBy(i => i.Name, StringComparer.Ordinal).ToList();

			// Shelf packing: rows of icons, tallest first.
			var placements = new List<(string Name, Image<Rgba32> Image, int X, int Y, int Width, int Height)>();
			int x = 0, y = 0, rowHeight = 0, sheetWidth = 1;
			foreach (var icon in sized)
			{
				if (x > 0 && x + icon.Width > MaxSheetWidth * ratio)
				{
					x = 0;
					y += rowHeight + Padding;
					rowHeight = 0;
				}

				placements.Add((icon.Name, icon.Image, x, y, icon.Width, icon.Height));
				x += icon.Width + Padding;
				rowHeight = Math.Max(rowHeight, icon.Height);
				sheetWidth = Math.Max(sheetWidth, x);
			}

			var sheetHeight = Math.Max(1, y + rowHeight);
			var index = new SortedDictionary<string, object>(StringComparer.Ordinal);

			using var sheet = new Image<Rgba32>(sheetWidth, sheetHeight, new Rgba32(0, 0, 0, 0));
			foreach (var placement in placements)
			{
				using var resized = placement.Image.Clone(context => context.Resize(placement.Width, placement.Height));
				sheet.Mutate(context => context.DrawImage(resized, new Point(placement.X, placement.Y), 1f));
				index[placement.Name] = new { x = placement.X, y = placement.Y, width = placement.Width, height = placement.Height, pixelRatio = ratio };
			}

			using var output = new MemoryStream();
			sheet.SaveAsPng(output);
			return (output.ToArray(), JsonSerializer.SerializeToUtf8Bytes(index));
		}
	}
}
