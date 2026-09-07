using System;
using System.IO;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Services.Records;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.Rms
{
	/// <summary>Media hygiene for Records attachments (RMS plan section 4.7).</summary>
	[TestFixture]
	public class RecordAttachmentHygieneTests
	{
		[Test]
		public void Png_is_re_encoded_without_exif_or_text_chunks()
		{
			var bytes = ImageWithMetadata(img => img.SaveAsPng(new MemoryStream()), "png");

			var result = RecordAttachmentHygiene.Sanitize("scene.png", "image/png", bytes);

			result.IsImage.Should().BeTrue();
			result.MetadataStripped.Should().BeTrue();
			result.ContentType.Should().Be("image/png");
			using var decoded = Image.Load(result.Data);
			decoded.Metadata.ExifProfile.Should().BeNull();
			decoded.Metadata.GetPngMetadata().TextData.Should().BeEmpty();
			decoded.Width.Should().Be(6);
		}

		[Test]
		public void Jpeg_is_re_encoded_without_exif_and_keeps_its_type_even_when_misdeclared()
		{
			var bytes = ImageWithMetadata(null, "jpeg");

			var result = RecordAttachmentHygiene.Sanitize("photo.jpg", "application/octet-stream", bytes);

			result.ContentType.Should().Be("image/jpeg", "the decoded format wins over the declared type");
			using var decoded = Image.Load(result.Data);
			decoded.Metadata.ExifProfile.Should().BeNull();
			decoded.Metadata.DecodedImageFormat.Name.Should().Be("JPEG");
		}

		[Test]
		public void Non_images_pass_through_byte_for_byte()
		{
			var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 fake");

			var result = RecordAttachmentHygiene.Sanitize("roster.pdf", "application/pdf", pdf);

			result.IsImage.Should().BeFalse();
			result.MetadataStripped.Should().BeFalse();
			result.Data.Should().BeSameAs(pdf);
			result.FileName.Should().Be("roster.pdf");
		}

		[Test]
		public void Active_content_svg_and_undecodable_images_are_refused()
		{
			var bytes = new byte[] { 1, 2, 3, 4 };

			Assert.Throws<RecordAttachmentRejectedException>(() => RecordAttachmentHygiene.Sanitize("payload.exe", "application/octet-stream", bytes));
			Assert.Throws<RecordAttachmentRejectedException>(() => RecordAttachmentHygiene.Sanitize("logo.svg", "image/svg+xml", bytes));
			Assert.Throws<RecordAttachmentRejectedException>(() => RecordAttachmentHygiene.Sanitize("script.txt", "text/javascript", bytes));
			Assert.Throws<RecordAttachmentRejectedException>(() => RecordAttachmentHygiene.Sanitize("notreally.png", "image/png", bytes));
			Assert.Throws<RecordAttachmentRejectedException>(() => RecordAttachmentHygiene.Sanitize("x.pdf", "application/pdf", new byte[0]));
			Assert.Throws<RecordAttachmentRejectedException>(() => RecordAttachmentHygiene.Sanitize("../../etc/passwd.exe", "text/plain", bytes));
		}

		[Test]
		public void File_names_are_reduced_to_their_leaf()
		{
			// Both separators, on both hosts: Linux treats a backslash as an ordinary filename character, so the
			// Windows-shaped name is the one that used to survive whole on the containers this actually runs on.
			RecordAttachmentHygiene.Sanitize(@"C:\temp\..\report.pdf", "application/pdf", new byte[] { 1 }).FileName.Should().Be("report.pdf");
			RecordAttachmentHygiene.Sanitize("/var/tmp/../report.pdf", "application/pdf", new byte[] { 1 }).FileName.Should().Be("report.pdf");
		}

		[Test]
		public void Photo_location_is_stripped_by_default_and_kept_only_when_the_definition_asks_for_it()
		{
			var bytes = GeoTaggedJpeg();

			var stripped = RecordAttachmentHygiene.Sanitize("scene.jpg", "image/jpeg", bytes);

			stripped.MetadataStripped.Should().BeTrue();
			stripped.LocationRetained.Should().BeFalse();
			using (var decoded = Image.Load(stripped.Data))
			{
				decoded.Metadata.ExifProfile.Should().BeNull("a photo's location is a disclosure nobody asked for");
			}

			var kept = RecordAttachmentHygiene.Sanitize("scene.jpg", "image/jpeg", bytes, true);

			kept.LocationRetained.Should().BeTrue();
			using var withLocation = Image.Load(kept.Data);
			withLocation.Metadata.ExifProfile.Should().NotBeNull();
			withLocation.Metadata.ExifProfile.TryGetValue(ExifTag.GPSLatitudeRef, out var latitudeRef).Should().BeTrue();
			latitudeRef.Value.Should().Be("N");
			withLocation.Metadata.ExifProfile.TryGetValue(ExifTag.GPSLatitude, out var latitude).Should().BeTrue();
			latitude.Value.Should().NotBeNull();
		}

		[Test]
		public void Keeping_the_location_never_keeps_device_or_person_identity()
		{
			var kept = RecordAttachmentHygiene.Sanitize("scene.jpg", "image/jpeg", GeoTaggedJpeg(), true);

			using var decoded = Image.Load(kept.Data);
			var profile = decoded.Metadata.ExifProfile;
			profile.Should().NotBeNull();
			profile.TryGetValue(ExifTag.Make, out _).Should().BeFalse();
			profile.TryGetValue(ExifTag.Model, out _).Should().BeFalse();
			profile.TryGetValue(ExifTag.Software, out _).Should().BeFalse();
			profile.TryGetValue(ExifTag.Artist, out _).Should().BeFalse();
			profile.TryGetValue(ExifTag.ImageUniqueID, out _).Should().BeFalse();
			profile.TryGetValue(ExifTag.DateTimeOriginal, out _).Should().BeFalse();
		}

		[Test]
		public void An_image_with_no_location_reports_none_kept_even_when_the_definition_allows_it()
		{
			var kept = RecordAttachmentHygiene.Sanitize("photo.jpg", "image/jpeg", ImageWithMetadata(null, "jpeg"), true);

			kept.LocationRetained.Should().BeFalse();
			using var decoded = Image.Load(kept.Data);
			decoded.Metadata.ExifProfile.Should().BeNull();
		}

		/// <summary>A JPEG carrying both a GPS fix and the device/person tags that must never survive it.</summary>
		private static byte[] GeoTaggedJpeg()
		{
			using var image = new Image<Rgba32>(6, 4);
			var exif = new ExifProfile();
			exif.SetValue(ExifTag.Make, "TestCam");
			exif.SetValue(ExifTag.Model, "TC-1");
			exif.SetValue(ExifTag.Software, "hygiene-test");
			exif.SetValue(ExifTag.Artist, "A Responder");
			exif.SetValue(ExifTag.ImageUniqueID, "SERIAL-123");
			exif.SetValue(ExifTag.DateTimeOriginal, "2026:09:06 03:15:00");
			exif.SetValue(ExifTag.GPSLatitudeRef, "N");
			exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(39, 1), new Rational(45, 1), new Rational(0, 1) });
			exif.SetValue(ExifTag.GPSLongitudeRef, "W");
			exif.SetValue(ExifTag.GPSLongitude, new[] { new Rational(104, 1), new Rational(59, 1), new Rational(0, 1) });
			image.Metadata.ExifProfile = exif;

			using var stream = new MemoryStream();
			image.Save(stream, new JpegEncoder());
			return stream.ToArray();
		}

		private static byte[] ImageWithMetadata(Action<Image<Rgba32>> unused, string format)
		{
			using var image = new Image<Rgba32>(6, 4);
			image.Metadata.ExifProfile = new ExifProfile();
			image.Metadata.ExifProfile.SetValue(ExifTag.Make, "TestCam");
			image.Metadata.ExifProfile.SetValue(ExifTag.Software, "hygiene-test");
			if (format == "png")
				image.Metadata.GetPngMetadata().TextData.Add(new SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData("Author", "someone", string.Empty, string.Empty));

			using var stream = new MemoryStream();
			if (format == "png")
				image.Save(stream, new PngEncoder());
			else
				image.Save(stream, new JpegEncoder());
			return stream.ToArray();
		}
	}
}
