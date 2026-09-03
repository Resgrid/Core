using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>Thrown when a logo upload fails validation; the message is safe to show to the administrator.</summary>
	public class DepartmentLogoRejectedException : ArgumentException
	{
		public DepartmentLogoRejectedException(string message) : base(message) { }
	}

	public sealed class LogoRendition
	{
		public DepartmentProfileMediaKind Kind { get; set; }
		public byte[] Data { get; set; }
		public string ContentType { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
	}

	/// <summary>
	/// Server-side logo handling (RMS plan section 4.10.1): PNG or JPEG only (SVG refused), at most 2 MB, at least
	/// 200 px on the long edge. Every upload is re-encoded with its metadata stripped, PNG transparency preserved,
	/// and the PrintHeader (1200x400 max), EmailMasthead (188 px wide) and Thumbnail (128x128) renditions generated.
	/// </summary>
	public static class DepartmentLogoRenditions
	{
		public const int MaxBytes = 2 * 1024 * 1024;
		public const int MinimumLongEdge = 200;
		public const int RecommendedLongEdge = 512;
		public const int PrintHeaderMaxWidth = 1200;
		public const int PrintHeaderMaxHeight = 400;
		public const int EmailMastheadWidth = 188;
		public const int ThumbnailSize = 128;

		public static List<LogoRendition> Build(byte[] data)
		{
			if (data == null || data.Length == 0)
				throw new DepartmentLogoRejectedException("A logo file is required.");
			if (data.Length > MaxBytes)
				throw new DepartmentLogoRejectedException($"The logo must be {MaxBytes / (1024 * 1024)} MB or smaller.");

			Image image;
			try
			{
				using var input = new MemoryStream(data, writable: false);
				image = Image.Load(new DecoderOptions { MaxFrames = 1 }, input);
			}
			catch (Exception ex)
			{
				throw new DepartmentLogoRejectedException($"The logo could not be read as a PNG or JPEG image ({ex.GetType().Name}).");
			}

			using (image)
			{
				var format = image.Metadata.DecodedImageFormat;
				var isPng = format != null && string.Equals(format.Name, "PNG", StringComparison.OrdinalIgnoreCase);
				var isJpeg = format != null && string.Equals(format.Name, "JPEG", StringComparison.OrdinalIgnoreCase);
				if (!isPng && !isJpeg)
					throw new DepartmentLogoRejectedException("Only PNG or JPEG logos are accepted.");

				if (Math.Max(image.Width, image.Height) < MinimumLongEdge)
					throw new DepartmentLogoRejectedException($"The logo must be at least {MinimumLongEdge} px on its long edge ({RecommendedLongEdge} px or larger is recommended).");

				Strip(image);
				var contentType = isPng ? "image/png" : "image/jpeg";

				return new List<LogoRendition>
				{
					Encode(image, DepartmentProfileMediaKind.PrimaryLogo, isPng, contentType),
					Encode(Fit(image, PrintHeaderMaxWidth, PrintHeaderMaxHeight), DepartmentProfileMediaKind.PrintHeader, isPng, contentType, dispose: true),
					Encode(Fit(image, EmailMastheadWidth, 0), DepartmentProfileMediaKind.EmailMasthead, isPng, contentType, dispose: true),
					Encode(Fit(image, ThumbnailSize, ThumbnailSize), DepartmentProfileMediaKind.Thumbnail, isPng, contentType, dispose: true)
				};
			}
		}

		private static void Strip(Image image)
		{
			image.Metadata.ExifProfile = null;
			image.Metadata.XmpProfile = null;
			image.Metadata.IptcProfile = null;
			image.Metadata.GetPngMetadata().TextData.Clear();
		}

		/// <summary>Fits inside the box without upscaling; a zero height means "width only".</summary>
		private static Image Fit(Image source, int maxWidth, int maxHeight)
		{
			var fitsWidth = source.Width <= maxWidth;
			var fitsHeight = maxHeight <= 0 || source.Height <= maxHeight;
			if (fitsWidth && fitsHeight)
				return source.Clone(ctx => { });

			return source.Clone(ctx => ctx.Resize(new ResizeOptions
			{
				Size = new Size(maxWidth, maxHeight),
				Mode = ResizeMode.Max,
				Sampler = KnownResamplers.Lanczos3
			}));
		}

		private static LogoRendition Encode(Image image, DepartmentProfileMediaKind kind, bool asPng, string contentType, bool dispose = false)
		{
			try
			{
				using var output = new MemoryStream();
				IImageEncoder encoder = asPng ? new PngEncoder { ColorType = PngColorType.RgbWithAlpha } : new JpegEncoder { Quality = 90 };
				image.Save(output, encoder);
				return new LogoRendition { Kind = kind, Data = output.ToArray(), ContentType = contentType, Width = image.Width, Height = image.Height };
			}
			finally
			{
				if (dispose)
					image.Dispose();
			}
		}
	}
}
