using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.UnitTracking;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingDevicesControllerTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;
		private const string UserId = "tracking-admin";

		private Mock<IUnitTrackingService> _trackingService;
		private Mock<IUnitTrackingCatalogService> _catalogService;
		private Mock<IUnitTrackingStatusService> _statusService;
		private Mock<IUnitTrackingIdentifierService> _identifierService;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorizationService;
		private UnitTrackingDevicesController _controller;
		private UnitTrackingDevice _device;
		private UnitTrackingCatalogProfile _profile;
		private string _originalPublicHttpsBaseUrl;

		[SetUp]
		public void SetUp()
		{
			_originalPublicHttpsBaseUrl = UnitTrackingConfig.PublicHttpsBaseUrl;
			UnitTrackingConfig.PublicHttpsBaseUrl = "https://tracking.example";

			_trackingService = new Mock<IUnitTrackingService>();
			_catalogService = new Mock<IUnitTrackingCatalogService>();
			_statusService = new Mock<IUnitTrackingStatusService>();
			_identifierService = new Mock<IUnitTrackingIdentifierService>();
			_authorizationService = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_device = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-1",
				DepartmentId = DepartmentId,
				UnitId = UnitId,
				DisplayName = "Engine tracker",
				ManufacturerKey = "generic",
				ModelKey = "generic-https",
				TransportType = (int)UnitTrackingTransportType.NativeHttps,
				ProtocolKey = "resgrid-json",
				PayloadAdapterKey = "resgrid-json-v1",
				DeviceIdentifier = "DEVICE-1234",
				IsEnabled = true,
				SourcePriority = 100,
				CreatedOn = DateTime.UtcNow
			};
			_profile = new UnitTrackingCatalogProfile
			{
				Key = "generic-https",
				ManufacturerKey = "generic",
				ManufacturerName = "Generic",
				Model = "Resgrid JSON",
				TransportType = UnitTrackingTransportType.NativeHttps,
				ProtocolKey = "resgrid-json",
				PayloadAdapterKey = "resgrid-json-v1",
				CertificationStatus = UnitTrackingCertificationStatus.Certified,
				IsSelectable = true,
				SupportedAuthModes = new[]
				{
					UnitTrackingAuthMode.Bearer,
					UnitTrackingAuthMode.CapabilityPath
				}
			};

			_trackingService
				.Setup(service => service.GetDeviceByIdAsync("device-1", DepartmentId))
				.ReturnsAsync(_device);
			_trackingService
				.Setup(service => service.GetDevicesForUnitAsync(DepartmentId, UnitId))
				.ReturnsAsync(new List<UnitTrackingDevice> { _device });
			_trackingService
				.Setup(service => service.GetCredentialsForDeviceAsync("device-1", DepartmentId))
				.ReturnsAsync(new List<UnitTrackingCredential>());
			_catalogService
				.Setup(service => service.GetProfileAsync(
					"generic-https",
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(_profile);
			_catalogService
				.Setup(service => service.GetProfilesAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(new[] { _profile });
			_statusService
				.Setup(service => service.GetEffectiveStatusAsync(
					It.IsAny<UnitTrackingDevice>(),
					It.IsAny<DateTime?>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(UnitTrackingDeviceStatus.Online);
			_identifierService
				.Setup(service => service.Mask("DEVICE-1234"))
				.Returns("********1234");
			_authorizationService
				.Setup(service => service.CanUserViewUnitAsync(UserId, UnitId))
				.ReturnsAsync(true);
			_authorizationService
				.Setup(service => service.CanUserModifyUnitAsync(UserId, UnitId))
				.ReturnsAsync(true);

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			_controller = new UnitTrackingDevicesController(
				_trackingService.Object,
				_catalogService.Object,
				_statusService.Object,
				_identifierService.Object,
				_authorizationService.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			UnitTrackingConfig.PublicHttpsBaseUrl = _originalPublicHttpsBaseUrl;
			ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public void AdministrativeActions_UseExpectedUnitPolicies()
		{
			// Arrange
			var viewActions = new[]
			{
				nameof(UnitTrackingDevicesController.GetCatalog),
				nameof(UnitTrackingDevicesController.GetUnitTrackers),
				nameof(UnitTrackingDevicesController.GetTracker),
				nameof(UnitTrackingDevicesController.GetStatus)
			};
			var updateActions = new[]
			{
				nameof(UnitTrackingDevicesController.CreateTracker),
				nameof(UnitTrackingDevicesController.UpdateTracker),
				nameof(UnitTrackingDevicesController.CreateCredential),
				nameof(UnitTrackingDevicesController.RotateCredential),
				nameof(UnitTrackingDevicesController.RevokeCredential),
				nameof(UnitTrackingDevicesController.DisableTracker),
				nameof(UnitTrackingDevicesController.DeleteTracker),
				nameof(UnitTrackingDevicesController.RebindTracker)
			};

			// Act
			var controllerType = typeof(UnitTrackingDevicesController);

			// Assert
			foreach (var actionName in viewActions)
				PolicyFor(controllerType, actionName).Should().Be(ResgridResources.Unit_View);
			foreach (var actionName in updateActions)
				PolicyFor(controllerType, actionName).Should().Be(ResgridResources.Unit_Update);
		}

		[Test]
		public async Task GetTracker_ViewOnlyUser_ReturnsMaskedIdentifierAndSanitizedCredentials()
		{
			// Arrange
			_authorizationService
				.Setup(service => service.CanUserModifyUnitAsync(UserId, UnitId))
				.ReturnsAsync(false);
			_trackingService
				.Setup(service => service.GetCredentialsForDeviceAsync("device-1", DepartmentId))
				.ReturnsAsync(new List<UnitTrackingCredential>
				{
					new()
					{
						UnitTrackingCredentialId = "credential-1",
						UnitTrackingDeviceId = "device-1",
						AuthMode = (int)UnitTrackingAuthMode.Bearer,
						KeyPrefix = "prefix12",
						SecretHash = "must-not-be-returned",
						ValidFrom = DateTime.UtcNow,
						CreatedOn = DateTime.UtcNow
					}
				});

			// Act
			var result = await _controller.GetTracker("device-1", CancellationToken.None);

			// Assert
			var response = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<UnitTrackingDeviceData>().Subject;
			response.DeviceIdentifier.Should().Be("********1234");
			response.IdentifierMasked.Should().BeTrue();
			response.AllowedSourceCidrs.Should().BeNull();
			response.Credentials.Should().ContainSingle()
				.Which.KeyPrefix.Should().Be("prefix12");
			typeof(UnitTrackingCredentialData).GetProperty(nameof(UnitTrackingCredential.SecretHash))
				.Should().BeNull();
		}

		[Test]
		public async Task CreateTracker_ValidProfile_BindsRouteUnitAndCallerDepartment()
		{
			// Arrange
			UnitTrackingDevice captured = null;
			_trackingService
				.Setup(service => service.CreateDeviceAsync(
					It.IsAny<UnitTrackingDevice>(),
					DepartmentId,
					UserId,
					It.IsAny<CancellationToken>()))
				.Callback<UnitTrackingDevice, int, string, CancellationToken>(
					(device, departmentId, userId, cancellationToken) => captured = device)
				.ReturnsAsync(_device);

			// Act
			var result = await _controller.CreateTracker(
				UnitId,
				new CreateUnitTrackingDeviceInput
				{
					ProfileKey = "generic-https",
					DisplayName = "Engine tracker",
					DeviceIdentifier = "device-1234"
				},
				CancellationToken.None);

			// Assert
			result.Result.Should().BeOfType<CreatedAtActionResult>();
			captured.Should().NotBeNull();
			captured.UnitId.Should().Be(UnitId);
			captured.PayloadAdapterKey.Should().Be("resgrid-json-v1");
			captured.TransportType.Should().Be((int)UnitTrackingTransportType.NativeHttps);
		}

		[Test]
		public async Task CreateTracker_UnitOutsideCallerScope_ReturnsForbiddenWithoutWrite()
		{
			// Arrange
			_authorizationService
				.Setup(service => service.CanUserModifyUnitAsync(UserId, 99))
				.ReturnsAsync(false);

			// Act
			var result = await _controller.CreateTracker(
				99,
				new CreateUnitTrackingDeviceInput { ProfileKey = "generic-https" },
				CancellationToken.None);

			// Assert
			result.Result.Should().BeOfType<ForbidResult>();
			_trackingService.Verify(service => service.CreateDeviceAsync(
				It.IsAny<UnitTrackingDevice>(),
				It.IsAny<int>(),
				It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task CreateCredential_Bearer_ReturnsTokenAndEndpointOnlyInProvisioningResponse()
		{
			// Arrange
			_trackingService
				.Setup(service => service.CreateCredentialAsync(
					"device-1",
					DepartmentId,
					UnitTrackingAuthMode.Bearer,
					UserId,
					null,
					null,
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new UnitTrackingCredentialProvisionResult
				{
					Token = "one-time-token",
					EndpointUrl = "https://tracking.example/api/v4/unit-trackers/device-1/positions",
					HeaderName = "Authorization",
					HeaderValue = "Bearer one-time-token",
					Credential = new UnitTrackingCredential
					{
						UnitTrackingCredentialId = "credential-1",
						UnitTrackingDeviceId = "device-1",
						AuthMode = (int)UnitTrackingAuthMode.Bearer,
						KeyPrefix = "prefix12",
						ValidFrom = DateTime.UtcNow,
						CreatedOn = DateTime.UtcNow
					}
				});

			// Act
			var result = await _controller.CreateCredential(
				"device-1",
				new CreateUnitTrackingCredentialInput
				{
					AuthMode = (int)UnitTrackingAuthMode.Bearer
				},
				CancellationToken.None);

			// Assert
			var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
				.Should().BeOfType<UnitTrackingCredentialProvisionData>().Subject;
			response.Token.Should().Be("one-time-token");
			response.EndpointUrl.Should().Be(
				"https://tracking.example/api/v4/unit-trackers/device-1/positions");
			response.HeaderName.Should().Be("Authorization");
			response.HeaderValue.Should().Be("Bearer one-time-token");
			_controller.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
		}

		[Test]
		public async Task GetStatus_TrackingStatusServiceResult_IsExposedWithoutCredentialData()
		{
			// Arrange
			_statusService
				.Setup(service => service.GetEffectiveStatusAsync(
					_device,
					It.IsAny<DateTime?>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(UnitTrackingDeviceStatus.Stale);

			// Act
			var result = await _controller.GetStatus("device-1", CancellationToken.None);

			// Assert
			var response = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
				.Should().BeOfType<UnitTrackingDeviceData>().Subject;
			response.Status.Should().Be((int)UnitTrackingDeviceStatus.Stale);
			response.StatusName.Should().Be(nameof(UnitTrackingDeviceStatus.Stale));
			response.Credentials.Should().BeEmpty();
		}

		private static string PolicyFor(Type controllerType, string actionName)
		{
			return controllerType
				.GetMethod(actionName, BindingFlags.Instance | BindingFlags.Public)
				.GetCustomAttributes<AuthorizeAttribute>()
				.Single()
				.Policy;
		}
	}
}
