using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
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
	}
}
