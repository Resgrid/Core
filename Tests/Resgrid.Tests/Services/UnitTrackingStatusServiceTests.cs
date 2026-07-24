using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class UnitTrackingStatusServiceTests
	{
		private readonly DateTime _now =
			new(2026, 7, 24, 20, 0, 0, DateTimeKind.Utc);

		private Mock<IDepartmentSettingsService> _settingsService;
		private UnitTrackingStatusService _service;

		[SetUp]
		public void SetUp()
		{
			_settingsService = new Mock<IDepartmentSettingsService>();
			_settingsService
				.Setup(service => service.GetHardwareTrackingStaleAfterSecondsAsync(10, false))
				.ReturnsAsync(300);
			_service = new UnitTrackingStatusService(_settingsService.Object);
		}

		[TestCase(false, false, UnitTrackingDeviceStatus.Disabled)]
		[TestCase(true, true, UnitTrackingDeviceStatus.Disabled)]
		public async Task GetEffectiveStatusAsync_DisabledOrDeleted_ReturnsDisabled(
			bool enabled,
			bool deleted,
			UnitTrackingDeviceStatus expected)
		{
			// Arrange
			var device = Device();
			device.IsEnabled = enabled;
			device.IsDeleted = deleted;

			// Act
			var status = await _service.GetEffectiveStatusAsync(device, _now);

			// Assert
			status.Should().Be(expected);
		}

		[Test]
		public async Task GetEffectiveStatusAsync_NeverSeen_ReturnsNeverSeen()
		{
			// Arrange
			var device = Device();

			// Act
			var status = await _service.GetEffectiveStatusAsync(device, _now);

			// Assert
			status.Should().Be(UnitTrackingDeviceStatus.NeverSeen);
		}

		[TestCase(-299, UnitTrackingDeviceStatus.Online)]
		[TestCase(-301, UnitTrackingDeviceStatus.Stale)]
		public async Task GetEffectiveStatusAsync_ReceiveAge_UsesDepartmentStaleThreshold(
			int ageSeconds,
			UnitTrackingDeviceStatus expected)
		{
			// Arrange
			var device = Device();
			device.LastReceivedOn = _now.AddSeconds(ageSeconds);

			// Act
			var status = await _service.GetEffectiveStatusAsync(device, _now);

			// Assert
			status.Should().Be(expected);
		}

		[Test]
		public async Task GetEffectiveStatusAsync_ErrorState_RemainsError()
		{
			// Arrange
			var device = Device();
			device.LastStatus = (int)UnitTrackingDeviceStatus.Error;
			device.LastReceivedOn = _now;

			// Act
			var status = await _service.GetEffectiveStatusAsync(device, _now);

			// Assert
			status.Should().Be(UnitTrackingDeviceStatus.Error);
		}

		[Test]
		public async Task GetEffectiveStatusAsync_NewerHeartbeatThanPosition_UsesLatestConnectivity()
		{
			// Arrange
			var device = Device();
			device.LastReceivedOn = _now.AddMinutes(-10);
			device.LastSeenOn = _now.AddMinutes(-1);

			// Act
			var status = await _service.GetEffectiveStatusAsync(device, _now);

			// Assert
			status.Should().Be(UnitTrackingDeviceStatus.Online);
		}

		private static UnitTrackingDevice Device() =>
			new()
			{
				UnitTrackingDeviceId = "device-1",
				DepartmentId = 10,
				UnitId = 42,
				IsEnabled = true
			};
	}
}
