using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Services;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// Round 10: the signed LMS media links a bearer client's video player loads without the token. The HMAC covers the kind, the department,
	/// the enrollment, the lesson, the caption locale and the expiry; links expire, cannot be pushed further out than the server issues, are
	/// not interchangeable between kinds or with map URLs, and none is issued or accepted without the passphrase.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsMediaLinksTests
	{
		private string _previousPassphrase, _previousApiBase;
		private int _previousMinutes;

		[SetUp]
		public void SetUp()
		{
			_previousPassphrase = SystemBehaviorConfig.ExternalLinkUrlParamPassphrase;
			_previousApiBase = SystemBehaviorConfig.ResgridApiBaseUrl;
			_previousMinutes = LmsConfig.SignedUrlMinutes;
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "links-test";
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.example.test/";
			LmsConfig.SignedUrlMinutes = 30;
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = _previousPassphrase;
			SystemBehaviorConfig.ResgridApiBaseUrl = _previousApiBase;
			LmsConfig.SignedUrlMinutes = _previousMinutes;
		}

		private static Dictionary<string, string> Query(string url) =>
			new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

		private static bool Validate(string kind, Dictionary<string, string> q, DateTime now, out int department, out int enrollment, out int lesson, out string locale) =>
			LmsMediaLinks.TryValidate(kind, q["d"], q["en"], q["ls"], q.TryGetValue("loc", out var loc) ? loc : null, q["e"], q["sig"], now, out department, out enrollment, out lesson, out locale);

		[Test]
		public void A_playlist_link_round_trips_and_names_no_one()
		{
			var now = DateTime.UtcNow;
			var url = LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, 31, 700, 9001, null, now.Add(LmsMediaLinks.Lifetime));

			url.Should().StartWith("https://api.example.test/api/v4/Lms/HostedPlaylist.m3u8?d=31&en=700&ls=9001&e=");
			Validate(LmsMediaLinks.PlaylistKind, Query(url), now, out var department, out var enrollment, out var lesson, out var locale).Should().BeTrue();
			(department, enrollment, lesson, locale).Should().Be((31, 700, 9001, (string)null));
		}

		[Test]
		public void A_caption_link_binds_its_locale()
		{
			var now = DateTime.UtcNow;
			var url = LmsMediaLinks.Build(LmsMediaLinks.CaptionsKind, 31, 700, 9001, "EN-gb", now.AddMinutes(10));
			url.Should().Contain("/api/v4/Lms/HostedCaptions.vtt?").And.Contain("loc=en-gb");

			var q = Query(url);
			Validate(LmsMediaLinks.CaptionsKind, q, now, out _, out _, out _, out var locale).Should().BeTrue();
			locale.Should().Be("en-gb");

			q["loc"] = "fr";
			Validate(LmsMediaLinks.CaptionsKind, q, now, out _, out _, out _, out _).Should().BeFalse();
			LmsMediaLinks.Build(LmsMediaLinks.CaptionsKind, 31, 700, 9001, "en/../x", now.AddMinutes(10)).Should().BeNull("not a caption locale");
			LmsMediaLinks.Build(LmsMediaLinks.CaptionsKind, 31, 700, 9001, null, now.AddMinutes(10)).Should().BeNull();
		}

		[TestCase("d", "32")]
		[TestCase("en", "701")]
		[TestCase("ls", "9002")]
		[TestCase("sig", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
		public void Any_changed_part_breaks_the_signature(string parameter, string value)
		{
			var now = DateTime.UtcNow;
			var q = Query(LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, 31, 700, 9001, null, now.AddMinutes(10)));
			q[parameter] = value;

			Validate(LmsMediaLinks.PlaylistKind, q, now, out var department, out _, out _, out _).Should().BeFalse();
			department.Should().Be(0);
		}

		[Test]
		public void Links_expire_cannot_be_pushed_out_and_are_not_interchangeable_between_kinds()
		{
			var now = DateTime.UtcNow;
			var q = Query(LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, 31, 700, 9001, null, now.AddMinutes(10)));

			Validate(LmsMediaLinks.PlaylistKind, q, now.AddMinutes(11), out _, out _, out _, out _).Should().BeFalse("expired");
			Validate(LmsMediaLinks.CaptionsKind, q, now, out _, out _, out _, out _).Should().BeFalse("a playlist link is not a caption link");

			var far = Query(LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, 31, 700, 9001, null, now.AddDays(30)));
			Validate(LmsMediaLinks.PlaylistKind, far, now, out _, out _, out _, out _).Should().BeFalse("further out than any link the server issues");

			q["e"] = "-5";
			Validate(LmsMediaLinks.PlaylistKind, q, now, out _, out _, out _, out _).Should().BeFalse();
		}

		[Test]
		public void A_map_url_signature_never_opens_a_media_link()
		{
			var now = DateTime.UtcNow;
			var q = Query(LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, 31, 700, 9001, null, now.AddMinutes(10)));
			var map = MapResourceUrls.SignQuery(31, "playlist|31|700|9001||" + q["e"], TimeSpan.FromMinutes(10));
			q["sig"] = map.Split('&').Single(p => p.StartsWith("sig=")).Substring(4);

			Validate(LmsMediaLinks.PlaylistKind, q, now, out _, out _, out _, out _).Should().BeFalse();
		}

		[Test]
		public void Without_the_passphrase_no_link_is_issued_or_accepted()
		{
			var now = DateTime.UtcNow;
			var q = Query(LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, 31, 700, 9001, null, now.AddMinutes(10)));

			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = " ";
			LmsMediaLinks.IsConfigured.Should().BeFalse();
			LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, 31, 700, 9001, null, now.AddMinutes(10)).Should().BeNull();
			Validate(LmsMediaLinks.PlaylistKind, q, now, out _, out _, out _, out _).Should().BeFalse();
		}

		[Test]
		public void The_lifetime_follows_the_lms_signed_url_minutes_with_a_floor()
		{
			LmsConfig.SignedUrlMinutes = 1;
			LmsMediaLinks.Lifetime.Should().Be(TimeSpan.FromMinutes(5));
			LmsConfig.SignedUrlMinutes = 45;
			LmsMediaLinks.Lifetime.Should().Be(TimeSpan.FromMinutes(45));
		}

		[TestCase("en", "en")]
		[TestCase(" PT-br ", "pt-br")]
		[TestCase("", null)]
		[TestCase("e n", null)]
		[TestCase("ënglish", null)]
		[TestCase("averyveryverylonglocale", null)]
		public void Caption_locales_are_normalized_like_the_media_service_stores_them(string input, string expected)
		{
			LmsMediaLinks.NormalizeLocale(input).Should().Be(expected);
		}
	}
}
