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
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.UnitTracking;
using UnitsResource = Resgrid.Localization.Areas.User.Units.Units;

namespace Resgrid.Tests.Web.User
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingControllerTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;
		private const string UserId = "tracking-admin";

		private Mock<IUnitTrackingService> _trackingService;
		private Mock<IUnitTrackingCatalogService> _catalogService;
		private Mock<IUnitTrackingStatusService> _statusService;
		private Mock<IUnitTrackingIdentifierService> _identifierService;
		private Mock<IUnitsService> _unitsService;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorizationService;
		private Mock<IStringLocalizer<UnitsResource>> _localizer;
		private UnitTrackingController _controller;
		private Unit _unit;
		private UnitTrackingDevice _device;
		private UnitTrackingCatalogProfile _profile;
		private SystemEnvironment _originalEnvironment;

		[SetUp]
		public void SetUp()
		{
			_originalEnvironment = SystemBehaviorConfig.Environment;
			SystemBehaviorConfig.Environment = SystemEnvironment.Dev;

			_trackingService = new Mock<IUnitTrackingService>();
			_catalogService = new Mock<IUnitTrackingCatalogService>();
			_statusService = new Mock<IUnitTrackingStatusService>();
			_identifierService = new Mock<IUnitTrackingIdentifierService>();
			_unitsService = new Mock<IUnitsService>();
			_authorizationService = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_localizer = new Mock<IStringLocalizer<UnitsResource>>();

			_unit = new Unit
			{
				UnitId = UnitId,
				DepartmentId = DepartmentId,
				Name = "Engine 42"
			};
			_device = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-1",
				DepartmentId = DepartmentId,
				UnitId = UnitId,
				ModelKey = "generic-https",
				DeviceIdentifier = "DEVICE-1234",
				IsEnabled = true
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

			_unitsService
				.Setup(service => service.GetUnitByIdAsync(UnitId))
				.ReturnsAsync(_unit);
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
			_localizer
				.Setup(localizer => localizer[It.IsAny<string>()])
				.Returns((string key) => new LocalizedString(key, key));
			_localizer
				.Setup(localizer => localizer[It.IsAny<string>(), It.IsAny<object[]>()])
				.Returns((string key, object[] arguments) =>
					new LocalizedString(key, string.Format(key, arguments)));

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(
						ResgridClaimTypes.Resources.Department,
						ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			_controller = new UnitTrackingController(
				_trackingService.Object,
				_catalogService.Object,
				_statusService.Object,
				_identifierService.Object,
				_unitsService.Object,
				_authorizationService.Object,
				_localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
			_controller.TempData = new TempDataDictionary(
				httpContext,
				Mock.Of<ITempDataProvider>());
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.Environment = _originalEnvironment;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public void AdministrativeActions_UseExpectedUnitPolicies()
		{
			// Arrange
			var expected = new Dictionary<string, string>
			{
				[nameof(UnitTrackingController.Index)] = ResgridResources.Unit_View,
				[nameof(UnitTrackingController.Details)] = ResgridResources.Unit_View,
				[nameof(UnitTrackingController.New)] = ResgridResources.Unit_Update,
				[nameof(UnitTrackingController.Edit)] = ResgridResources.Unit_Update,
				[nameof(UnitTrackingController.CreateCredential)] = ResgridResources.Unit_Update,
				[nameof(UnitTrackingController.RotateCredential)] = ResgridResources.Unit_Update,
				[nameof(UnitTrackingController.RevokeCredential)] = ResgridResources.Unit_Update,
				[nameof(UnitTrackingController.Disable)] = ResgridResources.Unit_Update,
				[nameof(UnitTrackingController.Delete)] = ResgridResources.Unit_Update,
				[nameof(UnitTrackingController.PreviewJson)] = ResgridResources.Unit_Update
			};

			// Act
			var actions = typeof(UnitTrackingController)
				.GetMethods(BindingFlags.Instance | BindingFlags.Public)
				.Where(method => expected.ContainsKey(method.Name));

			// Assert
			foreach (var action in actions)
			{
				action.GetCustomAttribute<AuthorizeAttribute>()
					.Should().NotBeNull()
					.And.Match<AuthorizeAttribute>(attribute =>
						attribute.Policy == expected[action.Name]);
			}
		}

		[Test]
		public async Task Index_ViewOnlyUser_MasksDeviceIdentifier()
		{
			// Arrange
			_authorizationService
				.Setup(service => service.CanUserModifyUnitAsync(UserId, UnitId))
				.ReturnsAsync(false);

			// Act
			var result = await _controller.Index(UnitId, CancellationToken.None);

			// Assert
			var model = result.Should().BeOfType<ViewResult>().Subject.Model
				.Should().BeOfType<UnitTrackingIndexView>().Subject;
			model.CanManage.Should().BeFalse();
			model.Devices.Should().ContainSingle()
				.Which.DisplayIdentifier.Should().Be("********1234");
		}

		[Test]
		public async Task Index_UnitOutsideDepartment_RedirectsToUnauthorized()
		{
			// Arrange
			_unit.DepartmentId = DepartmentId + 1;

			// Act
			var result = await _controller.Index(UnitId, CancellationToken.None);

			// Assert
			result.Should().BeOfType<RedirectResult>()
				.Which.Url.Should().Be("/Public/Unauthorized");
			_trackingService.Verify(service => service.GetDevicesForUnitAsync(
				It.IsAny<int>(),
				It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Edit_DisableTransition_UsesCredentialRevocationNotification()
		{
			// Arrange
			_trackingService
				.Setup(service => service.UpdateDeviceAsync(
					It.IsAny<UnitTrackingDevice>(),
					DepartmentId,
					UserId,
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((UnitTrackingDevice device, int departmentId, string userId,
					CancellationToken cancellationToken) => device);

			// Act
			var result = await _controller.Edit(
				_device.UnitTrackingDeviceId,
				new UnitTrackingEditorView
				{
					ProfileKey = _profile.Key,
					DisplayName = "Updated Tracker",
					DeviceIdentifier = _device.DeviceIdentifier,
					IsEnabled = false,
					SourcePriority = 25
				},
				CancellationToken.None);

			// Assert
			result.Should().BeOfType<RedirectToActionResult>()
				.Which.ActionName.Should().Be(nameof(UnitTrackingController.Details));
			_controller.TempData["UnitTrackingSuccess"]
				.Should().Be("TrackingBindingDisabledMessage");
		}

		[Test]
		public async Task CreateCredential_ValidBearer_DisplaysSecretOnceAndDisablesCaching()
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
						KeyPrefix = "prefix12"
					}
				});

			// Act
			var result = await _controller.CreateCredential(
				new CreateUnitTrackingCredentialView
				{
					UnitTrackingDeviceId = "device-1",
					AuthMode = (int)UnitTrackingAuthMode.Bearer
				},
				CancellationToken.None);

			// Assert
			var view = result.Should().BeOfType<ViewResult>().Subject;
			view.ViewName.Should().Be("Credential");
			view.Model.Should().BeOfType<UnitTrackingCredentialDisplayView>()
				.Which.Provisioning.Token.Should().Be("one-time-token");
			_controller.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
			_controller.Response.Headers.Pragma.ToString().Should().Be("no-cache");
		}

		[Test]
		public async Task PreviewJson_ProductionEnvironment_ReturnsNotFoundWithoutReadingDevice()
		{
			// Arrange
			SystemBehaviorConfig.Environment = SystemEnvironment.Prod;

			// Act
			var result = await _controller.PreviewJson(
				new PreviewUnitTrackingJsonView
				{
					UnitTrackingDeviceId = "device-1",
					JsonPayload = "{\"eventId\":\"preview\",\"latitude\":1,\"longitude\":2}"
				},
				CancellationToken.None);

			// Assert
			result.Should().BeOfType<NotFoundResult>();
			_trackingService.Verify(service => service.GetDeviceByIdAsync(
				It.IsAny<string>(),
				It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task PreviewJson_NonAdministrator_ReturnsNotFoundWithoutReadingDevice()
		{
			// Arrange
			_controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}, "test"));

			// Act
			var result = await _controller.PreviewJson(
				new PreviewUnitTrackingJsonView
				{
					UnitTrackingDeviceId = "device-1",
					JsonPayload = "{\"eventId\":\"preview\",\"latitude\":1,\"longitude\":2}"
				},
				CancellationToken.None);

			// Assert
			result.Should().BeOfType<NotFoundResult>();
			_trackingService.Verify(service => service.GetDeviceByIdAsync(
				It.IsAny<string>(),
				It.IsAny<int>()), Times.Never);
		}
	}
}
