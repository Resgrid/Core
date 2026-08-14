using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class DepartmentSettingsServiceUnitTrackingTests
	{
		private Mock<IDepartmentSettingsRepository> _repository;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentSettingsService _service;
		private bool _originalCacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_originalCacheEnabled = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = false;
			_repository = new Mock<IDepartmentSettingsRepository>();
			_cacheProvider = new Mock<ICacheProvider>();
			_service = new DepartmentSettingsService(
				_repository.Object,
				Mock.Of<IAddressService>(),
				Mock.Of<IGeoLocationProvider>(),
				_cacheProvider.Object);
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.CacheEnabled = _originalCacheEnabled;
		}

		[Test]
		public async Task TrackingSettings_MissingValues_ReturnDesignDefaults()
		{
			var staleSeconds = await _service.GetHardwareTrackingStaleAfterSecondsAsync(7);
			var fallbackEnabled = await _service.GetHardwareTrackingMobileFallbackEnabledAsync(7);
			var retentionDays = await _service.GetHardwareTrackingLocationRetentionDaysAsync(7);

			staleSeconds.Should().Be(180);
			fallbackEnabled.Should().BeTrue();
			retentionDays.Should().Be(UnitTrackingConfig.DefaultLocationRetentionDays);
		}

		[Test]
		public async Task GetHardwareTrackingLocationRetentionDaysAsync_OutOfRangeValue_IsClamped()
		{
			_repository
				.Setup(repository => repository.GetDepartmentSettingByIdTypeAsync(
					7,
					DepartmentSettingTypes.HardwareTrackingLocationRetentionDays))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.HardwareTrackingLocationRetentionDays,
					Setting = "99999"
				});

			var retentionDays = await _service.GetHardwareTrackingLocationRetentionDaysAsync(7);

			retentionDays.Should().Be(UnitTrackingConfig.MaximumLocationRetentionDays);
		}

		[Test]
		public async Task SaveOrUpdateSettingAsync_FirstTrackingWrite_InvalidatesFallbackCache()
		{
			_repository
				.Setup(repository => repository.SaveOrUpdateAsync(
					It.IsAny<DepartmentSetting>(),
					It.IsAny<CancellationToken>(),
					false))
				.ReturnsAsync((DepartmentSetting setting, CancellationToken cancellationToken, bool firstLevelOnly) => setting);

			await _service.SaveOrUpdateSettingAsync(
				7,
				"240",
				DepartmentSettingTypes.HardwareTrackingStaleAfterSeconds);

			_cacheProvider.Verify(
				provider => provider.RemoveAsync("DSetHardwareTrackingStale_7"),
				Times.Once);
		}

		[Test]
		public async Task GetDispatchRecommendationConfigAsync_ClampsOutOfRangeTuningValues()
		{
			var stored = new DispatchRecommendationConfig
			{
				MaxLocationAgeSeconds = 999999,
				PersonnelMaxLocationAgeSeconds = 999999,
				MaxRadiusMeters = 99999999,
				RestPeriodMinutes = 100000,
				// Each shortlisted candidate is one routed-ETA call to the mapping provider.
				EtaShortlistSize = 10000
			};

			_repository
				.Setup(repository => repository.GetDepartmentSettingByIdTypeAsync(
					7,
					DepartmentSettingTypes.DispatchRecommendationConfig))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.DispatchRecommendationConfig,
					Setting = ObjectSerialization.Serialize(stored)
				});

			var config = await _service.GetDispatchRecommendationConfigAsync(7);

			config.MaxLocationAgeSeconds.Should().Be(DispatchRecommendationConfig.MaximumLocationAgeSeconds);
			config.PersonnelMaxLocationAgeSeconds.Should().Be(DispatchRecommendationConfig.MaximumLocationAgeSeconds);
			config.MaxRadiusMeters.Should().Be(DispatchRecommendationConfig.MaximumRadiusMeters);
			config.RestPeriodMinutes.Should().Be(DispatchRecommendationConfig.MaximumRestPeriodMinutes);
			config.EtaShortlistSize.Should().Be(DispatchRecommendationConfig.MaximumEtaShortlistSize);
		}

		[Test]
		public async Task GetDispatchRecommendationConfigAsync_PreservesInRangeValuesAndZeroMeansNoLimit()
		{
			var stored = new DispatchRecommendationConfig
			{
				MaxLocationAgeSeconds = 600,
				MaxRadiusMeters = 0,
				RestPeriodMinutes = 0,
				EtaShortlistSize = 3
			};

			_repository
				.Setup(repository => repository.GetDepartmentSettingByIdTypeAsync(
					7,
					DepartmentSettingTypes.DispatchRecommendationConfig))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.DispatchRecommendationConfig,
					Setting = ObjectSerialization.Serialize(stored)
				});

			var config = await _service.GetDispatchRecommendationConfigAsync(7);

			config.MaxLocationAgeSeconds.Should().Be(600);
			config.MaxRadiusMeters.Should().Be(0);
			config.RestPeriodMinutes.Should().Be(0);
			config.EtaShortlistSize.Should().Be(3);
		}

		[Test]
		public async Task SaveOrUpdateSettingAsync_InvalidatesCacheAfterTheWriteCommits()
		{
			// Invalidating before the write lets a concurrent reader repopulate the key with
			// the pre-write value, which then survives for the full cache TTL.
			var sequence = new List<string>();

			_repository
				.Setup(repository => repository.SaveOrUpdateAsync(
					It.IsAny<DepartmentSetting>(),
					It.IsAny<CancellationToken>(),
					false))
				.Callback(() => sequence.Add("write"))
				.ReturnsAsync((DepartmentSetting setting, CancellationToken cancellationToken, bool firstLevelOnly) => setting);

			_cacheProvider
				.Setup(provider => provider.RemoveAsync(It.IsAny<string>()))
				.Callback(() => sequence.Add("invalidate"))
				.ReturnsAsync(true);

			await _service.SaveOrUpdateSettingAsync(
				7,
				"240",
				DepartmentSettingTypes.HardwareTrackingStaleAfterSeconds);

			sequence.Should().Equal("write", "invalidate");
		}

		[Test]
		public void SaveOrUpdateSettingAsync_FailedWrite_DoesNotInvalidateCache()
		{
			_repository
				.Setup(repository => repository.SaveOrUpdateAsync(
					It.IsAny<DepartmentSetting>(),
					It.IsAny<CancellationToken>(),
					false))
				.ThrowsAsync(new InvalidOperationException("write failed"));

			Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await _service.SaveOrUpdateSettingAsync(
					7,
					"240",
					DepartmentSettingTypes.HardwareTrackingStaleAfterSeconds));

			// The cached value still matches the database, so dropping it would be a pointless miss.
			_cacheProvider.Verify(
				provider => provider.RemoveAsync(It.IsAny<string>()),
				Times.Never);
		}
	}
}
