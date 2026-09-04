using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.Rms
{
	/// <summary>Department Profile elevation (RMS plan section 4.10.1): logo validation, renditions, media key, branding fallback.</summary>
	[TestFixture]
	public class DepartmentProfileMediaServiceTests
	{
		private const int Dept = 4;
		private Mock<IDepartmentProfileRepository> _profiles;
		private Mock<IDepartmentProfileMediaRepository> _media;
		private Mock<IDepartmentsService> _departments;
		private Mock<ICacheProvider> _cache;
		private List<DepartmentProfileMedia> _rows;
		private DepartmentProfile _profile;
		private DepartmentProfileMediaService _service;

		[SetUp]
		public void SetUp()
		{
			_rows = new List<DepartmentProfileMedia>();
			_profiles = new Mock<IDepartmentProfileRepository>();
			_profiles.Setup(p => p.SaveOrUpdateAsync(It.IsAny<DepartmentProfile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentProfile p, CancellationToken c, bool b) => { if (p.DepartmentProfileId == 0) p.DepartmentProfileId = 9; return p; });

			_media = new Mock<IDepartmentProfileMediaRepository>();
			_media.Setup(m => m.GetMetadataForDepartmentAsync(Dept)).ReturnsAsync(() => _rows.Select(r => new DepartmentProfileMedia { DepartmentProfileMediaId = r.DepartmentProfileMediaId, DepartmentId = r.DepartmentId, Kind = r.Kind, ContentType = r.ContentType, Width = r.Width, Height = r.Height, ByteSize = r.ByteSize, Checksum = r.Checksum, MediaKey = r.MediaKey, UploadedByUserId = r.UploadedByUserId }).ToList());
			_media.Setup(m => m.GetAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int k) => _rows.FirstOrDefault(r => r.Kind == k));
			_media.Setup(m => m.GetByMediaKeyAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((string key, int k) => _rows.FirstOrDefault(r => r.MediaKey == key && r.Kind == k));
			_media.Setup(m => m.DeleteForDepartmentAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(() => { var n = _rows.Count; _rows.Clear(); return n; });
			_media.Setup(m => m.InsertAsync(It.IsAny<DepartmentProfileMedia>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentProfileMedia r, CancellationToken c, bool b) => { _rows.Add(r); return r; });
			_media.Setup(m => m.UpdateMediaKeyAsync(Dept, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string key, CancellationToken c) => { foreach (var r in _rows) r.MediaKey = key; return _rows.Count; });

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department
			{
				DepartmentId = Dept, Name = "Springfield Fire", Code = "SFD1", Address = new Address { Address1 = "100 Main St", City = "Springfield", State = "IL", PostalCode = "62701" }
			});

			// Cache-aside passthrough: every read hits the fallback, every invalidation is observable.
			_cache = new Mock<ICacheProvider>();
			_cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentEmailBranding>>>(), It.IsAny<TimeSpan>()))
				.Returns((string key, Func<Task<DepartmentEmailBranding>> fallback, TimeSpan ttl) => fallback());
			_cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			var unitOfWork = new Mock<IUnitOfWork>();
			_service = new DepartmentProfileMediaService(_profiles.Object, _media.Object, _departments.Object, unitOfWork.Object, _cache.Object);
		}

		[Test]
		public async Task Upload_re_encodes_the_logo_and_generates_the_three_renditions_under_one_key()
		{
			var branding = await _service.UploadLogoAsync(Dept, "admin", "logo.png", "image/png", Png(900, 300, withExif: true));

			branding.HasLogo.Should().BeTrue();
			branding.Media.Select(m => (DepartmentProfileMediaKind)m.Kind).Should().BeEquivalentTo(new[]
			{
				DepartmentProfileMediaKind.PrimaryLogo, DepartmentProfileMediaKind.PrintHeader, DepartmentProfileMediaKind.EmailMasthead, DepartmentProfileMediaKind.Thumbnail
			});
			branding.Media.Select(m => m.MediaKey).Distinct().Should().HaveCount(1);
			branding.MediaKey.Should().HaveLength(48);

			var primary = _rows.Single(r => r.Kind == (int)DepartmentProfileMediaKind.PrimaryLogo);
			primary.ContentType.Should().Be("image/png");
			primary.Width.Should().Be(900);
			using (var decoded = Image.Load(primary.Data))
				decoded.Metadata.ExifProfile.Should().BeNull("metadata is stripped on upload");

			_rows.Single(r => r.Kind == (int)DepartmentProfileMediaKind.PrintHeader).Width.Should().Be(900, "already inside 1200x400, never upscaled");
			var masthead = _rows.Single(r => r.Kind == (int)DepartmentProfileMediaKind.EmailMasthead);
			masthead.Width.Should().Be(188);
			masthead.Height.Should().Be(63, "aspect ratio is kept");
			var thumb = _rows.Single(r => r.Kind == (int)DepartmentProfileMediaKind.Thumbnail);
			Math.Max(thumb.Width, thumb.Height).Should().Be(128);
			_rows.Should().OnlyContain(r => r.Checksum.Length == 64 && r.ByteSize == r.Data.LongLength);
		}

		[Test]
		public async Task Upload_keeps_the_existing_key_and_replaces_the_previous_logo()
		{
			await _service.UploadLogoAsync(Dept, "admin", "one.png", "image/png", Png(400, 400));
			var firstKey = _rows.First().MediaKey;

			await _service.UploadLogoAsync(Dept, "admin", "two.jpg", "image/jpeg", Jpeg(640, 200));

			_rows.Should().HaveCount(4);
			_rows.Should().OnlyContain(r => r.MediaKey == firstKey && r.ContentType == "image/jpeg");
		}

		[Test]
		public void Uploads_are_validated_server_side()
		{
			Assert.ThrowsAsync<DepartmentLogoRejectedException>(() => _service.UploadLogoAsync(Dept, "admin", "logo.svg", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>")));
			Assert.ThrowsAsync<DepartmentLogoRejectedException>(() => _service.UploadLogoAsync(Dept, "admin", "tiny.png", "image/png", Png(120, 120)));
			Assert.ThrowsAsync<DepartmentLogoRejectedException>(() => _service.UploadLogoAsync(Dept, "admin", "big.png", "image/png", new byte[DepartmentLogoRenditions.MaxBytes + 1]));
			Assert.ThrowsAsync<DepartmentLogoRejectedException>(() => _service.UploadLogoAsync(Dept, "admin", "x.gif", "image/gif", Gif(300, 300)));
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task Regenerating_the_key_invalidates_the_public_masthead_link()
		{
			await _service.UploadLogoAsync(Dept, "admin", "logo.png", "image/png", Png(400, 400));
			var oldKey = _rows.First().MediaKey;
			(await _service.GetPublicMastheadAsync(oldKey)).Should().NotBeNull();

			var newKey = await _service.RegenerateMediaKeyAsync(Dept, "admin");

			newKey.Should().NotBe(oldKey);
			(await _service.GetPublicMastheadAsync(oldKey)).Should().BeNull();
			(await _service.GetPublicMastheadAsync(newKey)).Kind.Should().Be((int)DepartmentProfileMediaKind.EmailMasthead);
			(await _service.GetPublicMastheadAsync("short")).Should().BeNull();
		}

		[Test]
		public async Task Branding_falls_back_to_the_department_row_and_a_legacy_logo_gets_its_renditions_on_first_read()
		{
			_profiles.Setup(p => p.GetByDepartmentIdAsync(Dept)).ReturnsAsync((DepartmentProfile)null);
			var empty = await _service.GetBrandingAsync(Dept);
			empty.DisplayName.Should().Be("Springfield Fire");
			empty.Code.Should().Be("SFD1");
			empty.AddressText.Should().Contain("100 Main St");
			empty.HasLogo.Should().BeFalse();

			// Migration M0172 leaves a PrimaryLogo row with unknown type and no renditions.
			_rows.Add(new DepartmentProfileMedia { DepartmentProfileMediaId = "legacy", DepartmentId = Dept, Kind = (int)DepartmentProfileMediaKind.PrimaryLogo, ContentType = "application/octet-stream", Data = Png(500, 250), MediaKey = "legacykey1234567890", UploadedByUserId = null });

			var branding = await _service.GetBrandingAsync(Dept);

			branding.HasLogo.Should().BeTrue();
			branding.Media.Should().HaveCount(4);
			branding.MediaKey.Should().Be("legacykey1234567890", "the migrated key is kept");
			branding.Rendition(DepartmentProfileMediaKind.PrintHeader).Width.Should().Be(500);
		}

		[Test]
		public async Task Profile_is_created_from_the_department_row_and_never_writes_the_legacy_logo_column()
		{
			_profiles.Setup(p => p.GetByDepartmentIdAsync(Dept)).ReturnsAsync((DepartmentProfile)null);

			var profile = await _service.GetOrCreateProfileAsync(Dept);
			profile.Name.Should().Be("Springfield Fire");
			profile.DepartmentProfileId.Should().Be(9);

			profile.Logo = new byte[] { 1 };
			var saved = await _service.SaveProfileAsync(profile);
			saved.Logo.Should().BeNull();
		}

		// ── Email branding (plan section 4.10.1, "Replacing the Resgrid logo in emails") ────────────────

		/// <summary>The profile mock remembers writes, so the opt-in default set by an upload is observable on the next read.</summary>
		private void UseStatefulProfile(DepartmentProfile initial)
		{
			_profile = initial;
			_profiles.Setup(p => p.GetByDepartmentIdAsync(Dept)).ReturnsAsync(() => _profile);
			_profiles.Setup(p => p.SaveOrUpdateAsync(It.IsAny<DepartmentProfile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentProfile p, CancellationToken c, bool b) => { if (p.DepartmentProfileId == 0) p.DepartmentProfileId = 9; _profile = p; return p; });
		}

		[Test]
		public async Task First_logo_upload_turns_email_branding_on_and_exposes_the_masthead_url()
		{
			UseStatefulProfile(null);

			await _service.UploadLogoAsync(Dept, "admin", "logo.png", "image/png", Png(900, 300));

			_profile.Should().NotBeNull("the profile row is created so the opt-in has somewhere to live");
			_profile.UseDepartmentBrandingInEmails.Should().BeTrue("plan 4.10.1: email branding defaults on when a logo exists");

			var branding = await _service.GetEmailBrandingAsync(Dept);
			branding.Enabled.Should().BeTrue();
			branding.LogoUrl.Should().Be($"{Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Department/PublicMasthead?key={_rows.First().MediaKey}");
			branding.DisplayName.Should().Be("Springfield Fire");
			branding.Website.Should().BeNull();
		}

		[Test]
		public async Task Replacing_the_logo_keeps_an_explicit_opt_out()
		{
			UseStatefulProfile(null);
			await _service.UploadLogoAsync(Dept, "admin", "logo.png", "image/png", Png(900, 300));
			_profile.UseDepartmentBrandingInEmails = false;

			await _service.UploadLogoAsync(Dept, "admin", "logo2.png", "image/png", Png(800, 400));

			_profile.UseDepartmentBrandingInEmails.Should().BeFalse("only the first logo flips the default");
			var branding = await _service.GetEmailBrandingAsync(Dept);
			branding.Enabled.Should().BeFalse("a logo without the opt-in is not a masthead");
			branding.LogoUrl.Should().BeNull();
			branding.DisplayName.Should().Be("Springfield Fire", "identity still flows to the workflow variables");
		}

		[Test]
		public async Task Email_branding_stays_off_without_a_logo_even_with_the_opt_in()
		{
			UseStatefulProfile(new DepartmentProfile { DepartmentProfileId = 9, DepartmentId = Dept, Name = "Springfield Fire Rescue", Website = "springfieldfire.example", UseDepartmentBrandingInEmails = true });

			var branding = await _service.GetEmailBrandingAsync(Dept);

			branding.Enabled.Should().BeFalse("a masthead needs a logo");
			branding.LogoUrl.Should().BeNull();
			branding.DisplayName.Should().Be("Springfield Fire Rescue");
			branding.Website.Should().Be("https://springfieldfire.example/", "the profile value is normalized to an absolute web URL");
		}

		[Test]
		public async Task Logo_changes_invalidate_the_cached_email_branding()
		{
			var originalCacheEnabled = Resgrid.Config.SystemBehaviorConfig.CacheEnabled;
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = true;
			try
			{
				UseStatefulProfile(null);
				await _service.UploadLogoAsync(Dept, "admin", "logo.png", "image/png", Png(900, 300));
				await _service.RegenerateMediaKeyAsync(Dept, "admin");
				await _service.RemoveLogoAsync(Dept, "admin");

				_cache.Verify(c => c.RemoveAsync(It.Is<string>(k => k.Contains("DepartmentEmailBranding_4"))), Times.AtLeast(3));
				(await _service.GetEmailBrandingAsync(Dept)).Enabled.Should().BeFalse("the logo is gone");
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.CacheEnabled = originalCacheEnabled;
			}
		}

		[Test]
		public async Task A_branding_lookup_failure_yields_resgrid_chrome_rather_than_an_exception()
		{
			_profiles.Setup(p => p.GetByDepartmentIdAsync(Dept)).ThrowsAsync(new InvalidOperationException("db down"));

			var branding = await _service.GetEmailBrandingAsync(Dept);

			branding.Enabled.Should().BeFalse();
			branding.DepartmentId.Should().Be(Dept);
		}

		[TestCase("www.example.org", "https://www.example.org/")]
		[TestCase("  example.org/path?x=1 ", "https://example.org/path?x=1")]
		[TestCase("http://example.org", "http://example.org/")]
		[TestCase("HTTPS://Example.org/Dept", "https://example.org/Dept")]
		[TestCase("javascript:alert(1)", null)]
		[TestCase("mailto:chief@example.org", null)]
		[TestCase("ftp://files.example.org", null)]
		[TestCase("//example.org", null)]
		[TestCase("user:pw@example.org", null)]
		[TestCase("localhost", null)]
		[TestCase("", null)]
		[TestCase(null, null)]
		public void Website_is_normalized_to_an_absolute_web_url_or_dropped(string input, string expected)
		{
			DepartmentEmailBranding.NormalizeWebsite(input).Should().Be(expected);
		}

		private static byte[] Png(int width, int height, bool withExif = false)
		{
			using var image = new Image<Rgba32>(width, height, new Rgba32(10, 20, 30, 128));
			if (withExif)
			{
				image.Metadata.ExifProfile = new ExifProfile();
				image.Metadata.ExifProfile.SetValue(ExifTag.Make, "cam");
			}
			using var stream = new MemoryStream();
			image.Save(stream, new PngEncoder());
			return stream.ToArray();
		}

		private static byte[] Jpeg(int width, int height)
		{
			using var image = new Image<Rgba32>(width, height, new Rgba32(200, 20, 30));
			using var stream = new MemoryStream();
			image.Save(stream, new JpegEncoder());
			return stream.ToArray();
		}

		private static byte[] Gif(int width, int height)
		{
			using var image = new Image<Rgba32>(width, height, new Rgba32(1, 2, 3));
			using var stream = new MemoryStream();
			image.SaveAsGif(stream);
			return stream.ToArray();
		}
	}
}
