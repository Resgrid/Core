using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class SmsContentHelperTests
	{
		private static readonly string[] Allowed = { "resgrid.com", "google.com", "maps.app.goo.gl", "bit.ly", "what3words.com" };

		[Test]
		public void StripDisallowedUrls_KeepsAllowListedLinks()
		{
			var input = "Map https://maps.app.goo.gl/abc123 also https://www.google.com/maps/place/x audio https://bit.ly/xyz";

			var result = SmsContentHelper.StripDisallowedUrls(input, Allowed);

			result.Should().Contain("https://maps.app.goo.gl/abc123");
			result.Should().Contain("https://www.google.com/maps/place/x");
			result.Should().Contain("https://bit.ly/xyz");
		}

		[Test]
		public void StripDisallowedUrls_RemovesUnknownAndShortenerLinks()
		{
			var input = "See https://tinyurl.com/abc and http://evil.example.com/phish now";

			var result = SmsContentHelper.StripDisallowedUrls(input, Allowed);

			result.Should().NotContain("tinyurl.com");
			result.Should().NotContain("evil.example.com");
			result.Should().Contain("See");
			result.Should().Contain("now");
		}

		[Test]
		public void StripDisallowedUrls_DoesNotMangleAbbreviationsOrAddresses()
		{
			// No scheme/www tokens -> nothing should be treated as a URL.
			var input = "Meet at 400 Olive St, Dallas, TX 75201, U.S. Call 9-1-1 for emergencies.";

			SmsContentHelper.StripDisallowedUrls(input, Allowed).Should().Be(input);
		}

		[Test]
		public void StripDisallowedUrls_AllowsSubdomainsOfAllowedDomain()
		{
			SmsContentHelper.StripDisallowedUrls("Loc https://maps.google.com/q", Allowed).Should().Contain("maps.google.com");
		}

		[Test]
		public void Truncate_CapsLongBodyWithSuffix()
		{
			var result = SmsContentHelper.Truncate(new string('a', 1000), 459);

			result.Length.Should().Be(459);
			result.Should().EndWith("the full message)");
		}

		[Test]
		public void Truncate_LeavesShortBodyUnchanged()
		{
			SmsContentHelper.Truncate("short body", 459).Should().Be("short body");
		}

		[Test]
		public void NormalizeForGsm_ReplacesSmartPunctuation()
		{
			var input = "It’s a “test” – really…";

			SmsContentHelper.NormalizeForGsm(input).Should().Be("It's a \"test\" - really...");
		}

		[Test]
		public void PrepareForSms_StripsDisallowedUrlThenTruncates()
		{
			var input = "Go to https://tinyurl.com/spam " + new string('b', 1000);

			var result = SmsContentHelper.PrepareForSms(input, 459, Allowed);

			result.Should().NotContain("tinyurl.com");
			(result.Length <= 459).Should().BeTrue();
		}
	}
}
