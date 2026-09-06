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
