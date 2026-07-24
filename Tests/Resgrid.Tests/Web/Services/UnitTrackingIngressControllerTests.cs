using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Web.Services.ApplicationCore.UnitTracking;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Middleware;
using Resgrid.Web.Services.Models.v4.UnitTracking;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingIngressControllerTests
	{
		private Mock<IUnitTrackingAuthenticationService> _authenticationService;
		private Mock<IUnitTrackingIngressService> _ingressService;
		private UnitTrackingIngressController _controller;
		private MemoryCache _cache;
		private UnitTrackingDevice _device;
		private UnitTrackingCredential _credential;
		private bool _originalEnabled;
		private bool _originalHttpsEnabled;
		private int _originalRequestsPerMinute;

		[SetUp]
		public void SetUp()
		{
			_originalEnabled = UnitTrackingConfig.Enabled;
			_originalHttpsEnabled = UnitTrackingConfig.HttpsIngressEnabled;
			_originalRequestsPerMinute = UnitTrackingConfig.PerDeviceRequestsPerMinute;
			UnitTrackingConfig.Enabled = true;
			UnitTrackingConfig.HttpsIngressEnabled = true;
			UnitTrackingConfig.PerDeviceRequestsPerMinute = 120;

			_authenticationService = new Mock<IUnitTrackingAuthenticationService>();
			_ingressService = new Mock<IUnitTrackingIngressService>();
			_cache = new MemoryCache(new MemoryCacheOptions());
			_device = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-1",
				DepartmentId = 10,
				UnitId = 42,
				IsEnabled = true,
				PayloadAdapterKey = "resgrid-json-v1"
			};
			_credential = new UnitTrackingCredential
			{
				UnitTrackingCredentialId = "credential-1",
				UnitTrackingDeviceId = "device-1",
				AuthMode = (int)UnitTrackingAuthMode.Bearer
			};
			_authenticationService
				.Setup(service => service.GetEnabledDeviceByEndpointIdAsync(
					"device-1",
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(_device);
			_authenticationService
				.Setup(service => service.AuthenticateAsync(
					"secret-token",
					It.IsAny<DateTime?>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new UnitTrackingAuthenticationResult
				{
					Device = _device,
					Credential = _credential
				});
			_ingressService
				.Setup(service => service.AcceptAsync(
					It.IsAny<AuthenticatedTrackingSource>(),
					It.IsAny<System.Collections.Generic.IReadOnlyCollection<CanonicalTrackingPosition>>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrackingIngressResult
				{
					Status = TrackingIngressStatus.Accepted,
					Accepted = 1,
					ReceivedOn = DateTime.UtcNow
				});

			var httpContext = Context("""
				{
				  "eventId": "record-1",
				  "latitude": 39.7392,
				  "longitude": -104.9903
				}
				""");
			httpContext.Request.Headers.Authorization = "Bearer secret-token";
			_controller = new UnitTrackingIngressController(
				new UnitTrackingHttpAuthenticationService(_authenticationService.Object),
				new UnitTrackingJsonPayloadParser(),
				new UnitTrackingRateLimiter(_cache),
				_ingressService.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			_cache.Dispose();
			UnitTrackingConfig.Enabled = _originalEnabled;
			UnitTrackingConfig.HttpsIngressEnabled = _originalHttpsEnabled;
			UnitTrackingConfig.PerDeviceRequestsPerMinute = _originalRequestsPerMinute;
		}

		[Test]
		public async Task PostPositions_ValidBearerPayload_ReturnsAcceptedWithoutTenantData()
		{
			var result = await _controller.PostPositions("device-1");

			var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
			var response = accepted.Value.Should()
				.BeOfType<UnitTrackingIngressResponse>().Subject;
			response.Accepted.Should().Be(1);
			_ingressService.Verify(service => service.AcceptAsync(
				It.Is<AuthenticatedTrackingSource>(source =>
					source.Device == _device &&
					source.Credential == _credential),
				It.IsAny<System.Collections.Generic.IReadOnlyCollection<CanonicalTrackingPosition>>(),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task PostPositions_KnownEndpointWithInvalidCredential_ReturnsUnauthorized()
		{
			_controller.Request.Headers.Authorization = "Bearer wrong-token";

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<UnauthorizedResult>();
			_ingressService.Verify(service => service.AcceptAsync(
				It.IsAny<AuthenticatedTrackingSource>(),
				It.IsAny<System.Collections.Generic.IReadOnlyCollection<CanonicalTrackingPosition>>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task PostPositions_UnknownEndpoint_ReturnsNotFound()
		{
			_authenticationService
				.Setup(service => service.GetEnabledDeviceByEndpointIdAsync(
					"missing-device",
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((UnitTrackingDevice)null);

			var result = await _controller.PostPositions("missing-device");

			result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task PostPositions_OneInvalidBatchRecord_ReturnsUnprocessableWithoutIngressCall()
		{
			SetBody("""
				{
				  "positions": [
				    { "eventId": "1", "latitude": 1, "longitude": 2 },
				    { "eventId": "2", "latitude": 1 }
				  ]
				}
				""");

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<UnprocessableEntityObjectResult>();
			_ingressService.Verify(service => service.AcceptAsync(
				It.IsAny<AuthenticatedTrackingSource>(),
				It.IsAny<System.Collections.Generic.IReadOnlyCollection<CanonicalTrackingPosition>>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task PostPositions_MalformedJson_ReturnsBadRequest()
		{
			SetBody("{");

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<BadRequestResult>();
		}

		[Test]
		public async Task PostPositions_UnsupportedContentType_ReturnsUnsupportedMediaType()
		{
			_controller.Request.ContentType = "text/plain";

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
		}

		[Test]
		public async Task PostPositions_DeclaredBodyOverLimit_ReturnsPayloadTooLarge()
		{
			_controller.Request.ContentLength = UnitTrackingConfig.MaxRequestBytes + 1L;

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
		}

		[Test]
		public async Task PostPositions_RequestLimitExceeded_ReturnsRetryAfter()
		{
			UnitTrackingConfig.PerDeviceRequestsPerMinute = 1;

			(await _controller.PostPositions("device-1"))
				.Should().BeOfType<AcceptedResult>();
			SetBody(
				"{\"eventId\":\"record-2\",\"latitude\":39.7392,\"longitude\":-104.9903}");

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
			_controller.Response.Headers.RetryAfter.ToString().Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task PostPositions_QueueUnavailable_ReturnsServiceUnavailable()
		{
			_ingressService
				.Setup(service => service.AcceptAsync(
					It.IsAny<AuthenticatedTrackingSource>(),
					It.IsAny<System.Collections.Generic.IReadOnlyCollection<CanonicalTrackingPosition>>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrackingIngressResult
				{
					Status = TrackingIngressStatus.Unavailable,
					ReceivedOn = DateTime.UtcNow
				});

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}

		[Test]
		public async Task PostPositions_AuthenticationDependencyFailure_ReturnsServiceUnavailable()
		{
			_authenticationService
				.Setup(service => service.GetEnabledDeviceByEndpointIdAsync(
					"device-1",
					It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("credential store unavailable"));

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}

		[Test]
		public async Task PostPositions_IngressDependencyFailure_ReturnsServiceUnavailable()
		{
			_ingressService
				.Setup(service => service.AcceptAsync(
					It.IsAny<AuthenticatedTrackingSource>(),
					It.IsAny<System.Collections.Generic.IReadOnlyCollection<CanonicalTrackingPosition>>(),
					It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("settings store unavailable"));

			var result = await _controller.PostPositions("device-1");

			result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}

		[Test]
		public async Task PostCapability_RedactedRouteUsesMiddlewareTokenAndReturnsAccepted()
		{
			_credential.AuthMode = (int)UnitTrackingAuthMode.CapabilityPath;
			_controller.HttpContext.Items[
				CapabilityPathRedactionMiddleware.CapabilityTokenItemKey] = "secret-token";
			_controller.Request.Headers.Remove("Authorization");

			var result = await _controller.PostCapability("[REDACTED]");

			result.Should().BeOfType<AcceptedResult>();
		}

		private void SetBody(string json)
		{
			var bytes = Encoding.UTF8.GetBytes(json);
			_controller.Request.Body = new MemoryStream(bytes);
			_controller.Request.ContentLength = bytes.Length;
			_controller.Request.ContentType = "application/json";
		}

		private static DefaultHttpContext Context(string json)
		{
			var context = new DefaultHttpContext();
			var bytes = Encoding.UTF8.GetBytes(json);
			context.Request.Body = new MemoryStream(bytes);
			context.Request.ContentLength = bytes.Length;
			context.Request.ContentType = "application/json";
			context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
			return context;
		}
	}
}
