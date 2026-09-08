using System;
using System.Collections.Generic;
using System.IO;
using Resgrid.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;

namespace Resgrid.Services.Records
{
	/// <summary>Thrown when an attachment fails media hygiene or the scanner; the message is safe to show to the uploader.</summary>
	public class RecordAttachmentRejectedException : ArgumentException
	{
		public RecordAttachmentRejectedException(string message) : base(message) { }
	}

	public sealed class AttachmentHygieneResult
	{
		public byte[] Data { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public bool IsImage { get; set; }
		public bool MetadataStripped { get; set; }

		/// <summary>True when the definition's policy kept the EXIF GPS block; recorded on the attachment.</summary>
		public bool LocationRetained { get; set; }
	}

	/// <summary>
	/// Media hygiene for Records attachments (RMS plan section 4.7): raster images are decoded and re-encoded so
	/// EXIF, XMP, IPTC and text chunks (location, device, author) never reach storage; SVG and active content
	/// types are refused outright; everything else passes through byte-for-byte for the scanner to judge.
	/// </summary>
	public static class RecordAttachmentHygiene
	{
		public const int MaxBytes = 25 * 1024 * 1024;
		public const long MaxPixels = 40_000_000;
		public const int MaxFrames = 64;

		private static readonly HashSet<string> BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".exe", ".dll", ".com", ".scr", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".psm1", ".sh", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".jar", ".cpl", ".reg", ".lnk", ".svg", ".svgz"
		};

		private static readonly HashSet<string> BlockedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"image/svg+xml", "application/x-msdownload", "application/x-msdos-program", "application/x-executable", "application/x-sh",
			"application/x-bat", "application/javascript", "text/javascript", "application/x-javascript", "application/java-archive", "application/hta"
		};

		private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".png", ".jpg", ".jpeg", ".jpe", ".gif", ".webp", ".bmp", ".tif", ".tiff"
		};

		public static AttachmentHygieneResult Sanitize(string fileName, string contentType, byte[] data) => Sanitize(fileName, contentType, data, false);

		/// <summary>
		/// <paramref name="retainLocation"/> keeps the EXIF GPS block and nothing else — device identity, serial
		/// numbers, author, software and timestamps still go. It is set only by a definition whose profile needs the
		/// coordinates (RMS plan RMS-1D), and the caller records which way the decision went on the attachment.
		/// </summary>
		public static AttachmentHygieneResult Sanitize(string fileName, string contentType, byte[] data, bool retainLocation)
		{
			if (data == null || data.Length == 0)
				throw new RecordAttachmentRejectedException("Attachment content is required.");
			if (data.Length > MaxBytes)
				throw new RecordAttachmentRejectedException($"Attachment '{fileName}' exceeds the {MaxBytes / (1024 * 1024)} MB limit.");

			// Path.GetFileName alone is host-relative: on Linux a backslash is an ordinary filename character, so
			// "C:\temp\..\report.pdf" would survive whole. GetSafeFileName normalises the separator first.
			var safeName = FileHelper.GetSafeFileName(fileName);
			var extension = Path.GetExtension(safeName) ?? string.Empty;
			var declaredType = (contentType ?? string.Empty).Trim();

			if (BlockedExtensions.Contains(extension) || BlockedContentTypes.Contains(declaredType))
				throw new RecordAttachmentRejectedException($"Attachment '{safeName}' is not an accepted file type.");

			var looksLikeImage = ImageExtensions.Contains(extension) || declaredType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
			if (!looksLikeImage)
				return new AttachmentHygieneResult { Data = data, FileName = safeName, ContentType = declaredType, IsImage = false, MetadataStripped = false };

			try
			{
				var options = new DecoderOptions { MaxFrames = MaxFrames };
				using var input = new MemoryStream(data, writable: false);
				using var image = Image.Load(options, input);

				if ((long)image.Width * image.Height > MaxPixels)
					throw new RecordAttachmentRejectedException($"Attachment '{safeName}' is larger than the {MaxPixels / 1_000_000} megapixel limit.");

				var format = image.Metadata.DecodedImageFormat ?? PngFormat.Instance;
				var location = retainLocation ? ExtractLocation(image) : null;
				StripMetadata(image);
				if (location != null)
					image.Metadata.ExifProfile = location;

				using var output = new MemoryStream();
				image.Save(output, EncoderFor(format));

				return new AttachmentHygieneResult
				{
					Data = output.ToArray(),
					FileName = safeName,
					ContentType = format.DefaultMimeType,
					IsImage = true,
					MetadataStripped = true,
					LocationRetained = location != null
				};
			}
			catch (RecordAttachmentRejectedException)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Undecodable "images" are refused rather than stored raw: a mismatched type is exactly the case hygiene exists for.
				throw new RecordAttachmentRejectedException($"Attachment '{safeName}' could not be read as an image ({ex.GetType().Name}).");
			}
		}

		/// <summary>
		/// The source image's GPS tags on a profile of their own, or null when it carries none. Copying tag by tag
		/// rather than filtering a clone is deliberate: only what is named here can survive, so a future EXIF tag
		/// carrying device or person identity cannot slip through by default.
		/// </summary>
		private static ExifProfile ExtractLocation(Image image)
		{
			var source = image.Metadata.ExifProfile;
			if (source == null)
				return null;

			var kept = new ExifProfile();
			Copy(source, kept, ExifTag.GPSVersionID);
			Copy(source, kept, ExifTag.GPSLatitudeRef);
			Copy(source, kept, ExifTag.GPSLatitude);
			Copy(source, kept, ExifTag.GPSLongitudeRef);
			Copy(source, kept, ExifTag.GPSLongitude);
			Copy(source, kept, ExifTag.GPSAltitudeRef);
			Copy(source, kept, ExifTag.GPSAltitude);
			Copy(source, kept, ExifTag.GPSTimestamp);
			Copy(source, kept, ExifTag.GPSSatellites);
			Copy(source, kept, ExifTag.GPSStatus);
			Copy(source, kept, ExifTag.GPSMeasureMode);
			Copy(source, kept, ExifTag.GPSDOP);
			Copy(source, kept, ExifTag.GPSSpeedRef);
			Copy(source, kept, ExifTag.GPSSpeed);
			Copy(source, kept, ExifTag.GPSTrackRef);
			Copy(source, kept, ExifTag.GPSTrack);
			Copy(source, kept, ExifTag.GPSImgDirectionRef);
			Copy(source, kept, ExifTag.GPSImgDirection);
			Copy(source, kept, ExifTag.GPSMapDatum);
			Copy(source, kept, ExifTag.GPSDateStamp);
			Copy(source, kept, ExifTag.GPSDifferential);

			return kept.Values.Count == 0 ? null : kept;
		}

		private static void Copy<TValue>(ExifProfile source, ExifProfile destination, ExifTag<TValue> tag)
		{
			if (source.TryGetValue(tag, out var value) && value != null && value.Value != null)
				destination.SetValue(tag, value.Value);
		}

		private static void StripMetadata(Image image)
		{
			image.Metadata.ExifProfile = null;
			image.Metadata.XmpProfile = null;
			image.Metadata.IptcProfile = null;
			// ICC stays: it carries colour, never identity or location.

			var png = image.Metadata.GetPngMetadata();
			png.TextData.Clear();

			foreach (var frame in image.Frames)
			{
				frame.Metadata.ExifProfile = null;
				frame.Metadata.XmpProfile = null;
				frame.Metadata.IptcProfile = null;
			}
		}

		private static IImageEncoder EncoderFor(IImageFormat format)
		{
			switch (format.Name.ToUpperInvariant())
			{
				case "JPEG": return new JpegEncoder { Quality = 90 };
				case "GIF": return new GifEncoder();
				case "WEBP": return new WebpEncoder();
				case "BMP": return new BmpEncoder();
				case "TIFF": return new TiffEncoder();
				default: return new PngEncoder();
			}
		}
	}
}
