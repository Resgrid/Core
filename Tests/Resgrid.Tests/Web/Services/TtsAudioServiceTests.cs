using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Services;
using RestSharp;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class TtsAudioServiceTests
	{
		[Test]
		public void constructor_should_not_resolve_rest_client()
		{
			// Regression: constructing TtsAudioService (and therefore any controller that transitively
			// depends on it, e.g. the Twilio controller that serves incoming SMS) must never build the
			// TTS RestClient. Previously the RestClient was injected directly and resolved at activation,
			// so an unconfigured TtsConfig.ServiceBaseUrl 500'd unrelated endpoints such as incoming SMS.
			var factoryInvoked = false;
			Func<RestClient> factory = () =>
			{
				factoryInvoked = true;
				throw new InvalidOperationException("RestClient must not be created during construction.");
			};

			var service = new TtsAudioService(factory);

			service.Should().NotBeNull();
			factoryInvoked.Should().BeFalse();
		}

		[Test]
		public async Task generate_speech_url_should_defer_rest_client_resolution_until_invoked()
		{
			// The "ServiceBaseUrl must be configured" failure should still surface — but only when TTS
			// audio is actually generated, not at construction time.
			Func<RestClient> factory = () =>
				throw new InvalidOperationException("TtsConfig.ServiceBaseUrl must be configured before using the TTS service.");

			var service = new TtsAudioService(factory);

			await FluentActions
				.Awaiting(() => service.GenerateSpeechUrlAsync("Hello from Resgrid"))
				.Should()
				.ThrowAsync<InvalidOperationException>()
				.WithMessage("*ServiceBaseUrl must be configured*");
		}

		[Test]
		public void constructor_should_reject_null_factory()
		{
			Action act = () => new TtsAudioService(null);

			act.Should().Throw<ArgumentNullException>();
		}
	}
}
