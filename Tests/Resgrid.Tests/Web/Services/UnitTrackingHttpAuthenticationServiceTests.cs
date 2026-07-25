using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.ApplicationCore.UnitTracking;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class UnitTrackingHttpAuthenticationServiceTests
	{
		private Mock<IUnitTrackingAuthenticationService> _authenticationService;
		private UnitTrackingHttpAuthenticationService _service;
		private UnitTrackingDevice _device;

		[SetUp]
		public void SetUp()
		{
			_authenticationService = new Mock<IUnitTrackingAuthenticationService>();
			_service = new UnitTrackingHttpAuthenticationService(_authenticationService.Object);
			_device = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-1",
				IsEnabled = true
			};
			_authenticationService
				.Setup(service => service.GetEnabledDeviceByEndpointIdAsync(
					"device-1",
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(_device);
		}

		[Test]
		public async Task AuthenticateEndpointAsync_BearerTokenForRequestedDevice_Succeeds()
		{
			var request = Request();
			request.Headers.Authorization = "Bearer secret-token";
			var credential = Credential(UnitTrackingAuthMode.Bearer);
			SetupToken("secret-token", _device, credential);

			var result = await _service.AuthenticateEndpointAsync(request, "device-1");

			result.Status.Should().Be(UnitTrackingHttpAuthenticationStatus.Authenticated);
			result.Source.Device.Should().BeSameAs(_device);
			result.Source.Credential.Should().BeSameAs(credential);
		}

		[Test]
		public async Task AuthenticateEndpointAsync_BasicUsernameMismatch_IsUnauthorized()
		{
			var request = Request();
			request.Headers.Authorization =
				"Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("wrong-user:secret-token"));
			var credential = Credential(UnitTrackingAuthMode.Basic);
			credential.BasicUsername = "configured-user";
			SetupToken("secret-token", _device, credential);

			var result = await _service.AuthenticateEndpointAsync(request, "device-1");

			result.Status.Should().Be(UnitTrackingHttpAuthenticationStatus.Unauthorized);
		}

		[Test]
		public async Task AuthenticateEndpointAsync_ConfiguredCustomHeader_Succeeds()
		{
			var request = Request();
			request.Headers["X-Vendor-Tracker-Key"] = "secret-token";
			var credential = Credential(UnitTrackingAuthMode.CustomHeader);
			credential.HeaderName = "X-Vendor-Tracker-Key";
			_authenticationService
				.Setup(service => service.GetActiveCredentialsForDeviceAsync(
					"device-1",
					It.IsAny<DateTime?>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<UnitTrackingCredential> { credential });
			SetupToken("secret-token", _device, credential);

			var result = await _service.AuthenticateEndpointAsync(request, "device-1");

			result.Status.Should().Be(UnitTrackingHttpAuthenticationStatus.Authenticated);
		}

		[Test]
		public async Task AuthenticateEndpointAsync_TokenBoundToDifferentEndpoint_IsUnauthorized()
		{
			var request = Request();
			request.Headers.Authorization = "Bearer secret-token";
			var otherDevice = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-2",
				IsEnabled = true
			};
			SetupToken("secret-token", otherDevice, Credential(UnitTrackingAuthMode.Bearer));

			var result = await _service.AuthenticateEndpointAsync(request, "device-1");

			result.Status.Should().Be(UnitTrackingHttpAuthenticationStatus.Unauthorized);
		}

		[Test]
		public async Task AuthenticateCapabilityAsync_NonCapabilityCredential_ReturnsNotFound()
		{
			SetupToken("capability-token", _device, Credential(UnitTrackingAuthMode.Bearer));

			var result = await _service.AuthenticateCapabilityAsync("capability-token");

			result.Status.Should().Be(UnitTrackingHttpAuthenticationStatus.NotFound);
		}

		private void SetupToken(
			string token,
			UnitTrackingDevice device,
			UnitTrackingCredential credential)
		{
			_authenticationService
				.Setup(service => service.AuthenticateAsync(
					token,
					It.IsAny<DateTime?>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new UnitTrackingAuthenticationResult
				{
					Device = device,
					Credential = credential
				});
		}

		private static UnitTrackingCredential Credential(UnitTrackingAuthMode mode) =>
			new()
			{
				UnitTrackingCredentialId = "credential-1",
				UnitTrackingDeviceId = "device-1",
				AuthMode = (int)mode
			};

		private static HttpRequest Request() => new DefaultHttpContext().Request;
	}
}
