using System;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class TtsPlaybackUrlServiceTests
	{
		[Test]
		public void create_playback_url_should_normalize_and_escape_hash()
		{
			var service = new TtsPlaybackUrlService(Options.Create(new TtsOptions
			{
				PlaybackBaseUrl = "https://tts.example.com"
			}));

			var result = service.CreatePlaybackUrl(null, "  Ab C  ");

			result.Should().Be(new Uri("https://tts.example.com/tts/audio/ab%20c.wav"));
		}

		[TestCase("abc/123")]
		[TestCase("abc?123")]
		[TestCase("abc#123")]
		public void create_playback_url_should_reject_hashes_with_url_delimiters(string hash)
		{
			var service = new TtsPlaybackUrlService(Options.Create(new TtsOptions
			{
				PlaybackBaseUrl = "https://tts.example.com"
			}));

			FluentActions
				.Invoking(() => service.CreatePlaybackUrl(null, hash))
				.Should()
				.Throw<ArgumentException>()
				.WithParameterName("hash");
		}

		[Test]
		public void create_playback_url_should_use_request_base_when_configured_base_url_missing()
		{
			var service = new TtsPlaybackUrlService(Options.Create(new TtsOptions()));
			var httpContext = new DefaultHttpContext();
			httpContext.Request.Scheme = "https";
			httpContext.Request.Host = new HostString("api.example.com");
			httpContext.Request.PathBase = new PathString("/tts-service");

			var result = service.CreatePlaybackUrl(httpContext.Request, "ABC123");

			result.Should().Be(new Uri("https://api.example.com/tts-service/tts/audio/abc123.wav"));
		}
	}
}
