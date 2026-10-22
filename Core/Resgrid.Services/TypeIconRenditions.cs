using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>Thrown when a type icon upload fails validation; the message is safe to show to the administrator.</summary>
	public class TypeIconRejectedException : ArgumentException, IUserFacingException
	{
		public TypeIconRejectedException(TypeIconRejections reason, string message) : base(message)
		{
			Reason = reason;
		}

		/// <summary>Which check failed, for pages that show their own localized message.</summary>
		public TypeIconRejections Reason { get; }
	}

	public sealed class TypeIconRendition
	{
		public byte[] Data { get; set; }
		public string ContentType { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
	}

	/// <summary>
	/// Server-side unit, call, POI type and personnel role icon handling: PNG, JPEG, GIF (first frame) or WebP only (SVG refused), at most 1 MB and
	/// 4096 px on either edge (checked from the header before decoding), at least 16 px. Every upload is scaled to fit a
	/// 128 px square, centred on a transparent canvas and re-encoded as PNG with its metadata dropped, so every list and
	/// map can show it at a fixed square size.
	/// </summary>
	public static class TypeIconRenditions
	{
		public const int MaxBytes = TypeIcons.MaxUploadBytes;
		public const int MinimumEdge = TypeIcons.MinimumEdge;
		public const int MaximumEdge = TypeIcons.MaximumEdge;
		public const int Size = TypeIcons.StoredSize;

		private static readonly string[] AcceptedFormats = { "PNG", "JPEG", "GIF", "WEBP" };

		public static TypeIconRendition Build(byte[] data)
		{
			if (data == null || data.Length == 0)
				throw new TypeIconRejectedException(TypeIconRejections.Required, "An icon file is required.");
			if (data.Length > MaxBytes)
				throw new TypeIconRejectedException(TypeIconRejections.TooManyBytes, $"The icon must be {MaxBytes / 1024} KB or smaller.");

			var decoderOptions = new DecoderOptions { MaxFrames = 1 };

			ImageInfo info;
			try
			{
				using var header = new MemoryStream(data, writable: false);
				info = Image.Identify(decoderOptions, header);
			}
			catch (Exception ex)
			{
				throw new TypeIconRejectedException(TypeIconRejections.Unreadable, $"The icon could not be read as a PNG, JPEG, GIF or WebP image ({ex.GetType().Name}).");
			}

			var formatName = info.Metadata.DecodedImageFormat?.Name;
			if (formatName == null || Array.FindIndex(AcceptedFormats, f => string.Equals(f, formatName, StringComparison.OrdinalIgnoreCase)) < 0)
				throw new TypeIconRejectedException(TypeIconRejections.UnsupportedFormat, "Only PNG, JPEG, GIF or WebP icons are accepted.");
			if (info.Width > MaximumEdge || info.Height > MaximumEdge)
				throw new TypeIconRejectedException(TypeIconRejections.TooLarge, $"The icon must be {MaximumEdge} px or smaller on each edge.");
			if (Math.Max(info.Width, info.Height) < MinimumEdge)
				throw new TypeIconRejectedException(TypeIconRejections.TooSmall, $"The icon must be at least {MinimumEdge} px on its long edge.");

			Image<Rgba32> image;
			try
			{
				using var input = new MemoryStream(data, writable: false);
				image = Image.Load<Rgba32>(decoderOptions, input);
			}
			catch (Exception ex)
			{
				throw new TypeIconRejectedException(TypeIconRejections.Unreadable, $"The icon could not be read as a PNG, JPEG, GIF or WebP image ({ex.GetType().Name}).");
			}

			using (image)
			{
				// Scale the long edge to the square (up or down), then centre it on a transparent square canvas.
				var scale = (double)Size / Math.Max(image.Width, image.Height);
				var width = Math.Max(1, (int)Math.Round(image.Width * scale));
				var height = Math.Max(1, (int)Math.Round(image.Height * scale));

				image.Mutate(ctx => ctx
					.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Bicubic })
					.Resize(new ResizeOptions { Size = new Size(Size, Size), Mode = ResizeMode.BoxPad, PadColor = Color.Transparent }));

				image.Metadata.ExifProfile = null;
				image.Metadata.XmpProfile = null;
				image.Metadata.IptcProfile = null;
				image.Metadata.IccProfile = null;

				using var output = new MemoryStream();
				image.Save(output, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, ChunkFilter = PngChunkFilter.ExcludeAll });

				return new TypeIconRendition { Data = output.ToArray(), ContentType = "image/png", Width = image.Width, Height = image.Height };
			}
		}
	}
}
