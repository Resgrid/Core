using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Health;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Health;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class TtsFullHealthCheckTests
	{
		private static TtsFullHealthCheck CreateCheck(
			Mock<IDistributedCache> distributedCache,
			Mock<IStorageService> storageService,
			Mock<IAudioProcessingService> audioProcessingService)
		{
			return new TtsFullHealthCheck(
				distributedCache.Object,
				storageService.Object,
				audioProcessingService.Object,
				Options.Create(new TtsOptions()));
		}

		private static (Mock<IDistributedCache> Cache, Mock<IStorageService> Storage, Mock<IAudioProcessingService> Audio) CreateHealthyMocks()
		{
			var distributedCache = new Mock<IDistributedCache>();
			distributedCache
				.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new byte[] { 1 });

			var storageService = new Mock<IStorageService>();
			storageService
				.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);

			var audioProcessingService = new Mock<IAudioProcessingService>();
			audioProcessingService
				.Setup(x => x.GenerateNormalizedWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new byte[] { 82, 73, 70, 70 });

			return (distributedCache, storageService, audioProcessingService);
		}

		[Test]
		public async Task check_health_should_report_healthy_when_all_probes_pass()
		{
			var (cache, storage, audio) = CreateHealthyMocks();
			var check = CreateCheck(cache, storage, audio);

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Healthy);
			result.Data.Keys.Should().Contain(new[] { "redis", "s3", "synthesis" });
		}

		[Test]
		public async Task check_health_should_report_unhealthy_when_synthesis_fails()
		{
			var (cache, storage, audio) = CreateHealthyMocks();
			audio
				.Setup(x => x.GenerateNormalizedWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Piper exited with code 1."));

			var check = CreateCheck(cache, storage, audio);

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Contain("synthesis");
		}

		[Test]
		public async Task check_health_should_report_unhealthy_when_redis_round_trip_returns_nothing()
		{
			var (cache, storage, audio) = CreateHealthyMocks();
			cache
				.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((byte[])null);

			var check = CreateCheck(cache, storage, audio);

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Contain("redis");
		}

		[Test]
		public async Task check_health_should_memoize_the_result_between_close_polls()
		{
			var (cache, storage, audio) = CreateHealthyMocks();
			var check = CreateCheck(cache, storage, audio);

			await check.CheckHealthAsync(new HealthCheckContext());
			await check.CheckHealthAsync(new HealthCheckContext());

			audio.Verify(
				x => x.GenerateNormalizedWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
				Times.Once);
		}
	}

	[TestFixture]
	public class RedisHealthCheckTests
	{
		[Test]
		public async Task check_health_should_report_healthy_when_round_trip_succeeds()
		{
			var cacheProvider = new Mock<ICacheProvider>();
			cacheProvider.Setup(x => x.SetStringAsync(It.IsAny<string>(), "ok", It.IsAny<TimeSpan>())).ReturnsAsync(true);
			cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync("ok");

			var check = new RedisHealthCheck(cacheProvider.Object);

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Healthy);
		}

		[Test]
		public async Task check_health_should_report_unhealthy_when_write_fails()
		{
			var cacheProvider = new Mock<ICacheProvider>();
			cacheProvider.Setup(x => x.SetStringAsync(It.IsAny<string>(), "ok", It.IsAny<TimeSpan>())).ReturnsAsync(false);

			var check = new RedisHealthCheck(cacheProvider.Object);

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Unhealthy);
		}
	}

	[TestFixture]
	public class TtsServiceHealthCheckTests
	{
		private string _originalServiceBaseUrl;

		[SetUp]
		public void SetUp()
		{
			_originalServiceBaseUrl = Resgrid.Config.TtsConfig.ServiceBaseUrl;
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.TtsConfig.ServiceBaseUrl = _originalServiceBaseUrl;
		}

		[Test]
		public async Task check_health_should_report_degraded_when_tts_is_not_configured()
		{
			Resgrid.Config.TtsConfig.ServiceBaseUrl = "";

			var check = new TtsServiceHealthCheck(new StubHttpClientFactory(_ => throw new InvalidOperationException("Should not be called.")));

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Degraded);
		}

		[Test]
		public async Task check_health_should_report_healthy_when_tts_health_endpoint_returns_success()
		{
			Resgrid.Config.TtsConfig.ServiceBaseUrl = "https://tts.example.com";

			string requestedUrl = null;
			var check = new TtsServiceHealthCheck(new StubHttpClientFactory(request =>
			{
				requestedUrl = request.RequestUri.ToString();
				return new HttpResponseMessage(HttpStatusCode.OK);
			}));

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Healthy);
			requestedUrl.Should().Be("https://tts.example.com/health");
		}

		[Test]
		public async Task check_health_should_report_unhealthy_when_tts_health_endpoint_errors()
		{
			Resgrid.Config.TtsConfig.ServiceBaseUrl = "https://tts.example.com";

			var check = new TtsServiceHealthCheck(new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

			var result = await check.CheckHealthAsync(new HealthCheckContext());

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Contain("500");
		}

		private sealed class StubHttpClientFactory : IHttpClientFactory
		{
			private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

			public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
			{
				_responder = responder;
			}

			public HttpClient CreateClient(string name)
			{
				return new HttpClient(new StubHandler(_responder));
			}

			private sealed class StubHandler : HttpMessageHandler
			{
				private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

				public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
				{
					_responder = responder;
				}

				protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
				{
					return Task.FromResult(_responder(request));
				}
			}
		}
	}
}
