using System;
using System.Collections.Generic;
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
	public class UnitLocationSourceResolverTests
	{
		private const int DepartmentId = 10;
		private Mock<IDepartmentSettingsService> _settingsService;
		private UnitLocationSourceResolver _resolver;
		private DateTime _now;

		[SetUp]
		public void SetUp()
		{
			_now = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);
			_settingsService = new Mock<IDepartmentSettingsService>();
			_settingsService
				.Setup(service => service.GetHardwareTrackingStaleAfterSecondsAsync(DepartmentId, false))
				.ReturnsAsync(180);
			_settingsService
				.Setup(service => service.GetHardwareTrackingMobileFallbackEnabledAsync(DepartmentId, false))
				.ReturnsAsync(true);
			_resolver = new UnitLocationSourceResolver(_settingsService.Object);
		}

		[Test]
		public async Task ResolveAsync_FreshHardwareAndMobile_SelectsHigherPriorityHardware()
		{
			var hardware = Location(
				"hardware",
				UnitLocationSourceType.HardwareTracker,
				100,
				_now.AddSeconds(-60),
				_now.AddSeconds(-30));
			var mobile = Location(
				"mobile",
				UnitLocationSourceType.UnitApp,
				0,
				_now.AddSeconds(-5),
				_now.AddSeconds(-5));

			var result = await _resolver.ResolveAsync(
				DepartmentId,
				new List<UnitsLocation> { mobile, hardware },
				_now);

			result.Location.Should().BeSameAs(hardware);
			result.IsStale.Should().BeFalse();
		}

		[Test]
		public async Task ResolveAsync_StaleHardwareAndFreshMobileWithFallbackDisabled_ReturnsNull()
		{
			_settingsService
				.Setup(service => service.GetHardwareTrackingMobileFallbackEnabledAsync(DepartmentId, false))
				.ReturnsAsync(false);
			var hardware = Location(
				"hardware",
				UnitLocationSourceType.HardwareTracker,
				100,
				_now.AddMinutes(-10),
				_now.AddMinutes(-10));
			var mobile = Location(
				"mobile",
				UnitLocationSourceType.UnitApp,
				0,
				_now.AddSeconds(-5),
				_now.AddSeconds(-5));

			var result = await _resolver.ResolveAsync(
				DepartmentId,
				new List<UnitsLocation> { mobile, hardware },
				_now);

			result.Should().BeNull();
		}

		[Test]
		public async Task ResolveAsync_NoFreshSourcesAndFallbackEnabled_ReturnsNewestPointAsStale()
		{
			var older = Location(
				"hardware",
				UnitLocationSourceType.HardwareTracker,
				100,
				_now.AddMinutes(-20),
				_now.AddMinutes(-19));
			var newer = Location(
				"mobile",
				UnitLocationSourceType.UnitApp,
				0,
				_now.AddMinutes(-10),
				_now.AddMinutes(-9));

			var result = await _resolver.ResolveAsync(
				DepartmentId,
				new List<UnitsLocation> { older, newer },
				_now);

			result.Location.Should().BeSameAs(newer);
			result.IsStale.Should().BeTrue();
		}

		private static UnitsLocation Location(
			string sourceId,
			UnitLocationSourceType sourceType,
			int priority,
			DateTime timestamp,
			DateTime receivedOn)
		{
			return new UnitsLocation
			{
				DepartmentId = DepartmentId,
				UnitId = 42,
				SourceId = sourceId,
				SourceType = (int)sourceType,
				SourcePriority = priority,
				IsValidFix = true,
				Timestamp = timestamp,
				ReceivedOn = receivedOn,
				Latitude = 47.6062m,
				Longitude = -122.3321m
			};
		}
	}
}
