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
	public class DepartmentSettingsServicePasswordResetTests
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
		public async Task GetRequirePasswordResetViaEmailAsync_MissingSetting_PreservesDirectResetDefault()
		{
			var enabled = await _service.GetRequirePasswordResetViaEmailAsync(7);

			enabled.Should().BeFalse();
		}

		[Test]
		public async Task GetRequirePasswordResetViaEmailAsync_EnabledSetting_ReturnsTrue()
		{
			_repository
				.Setup(repository => repository.GetDepartmentSettingByIdTypeAsync(
					7,
					DepartmentSettingTypes.RequirePasswordResetViaEmail))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.RequirePasswordResetViaEmail,
					Setting = "true"
				});

			var enabled = await _service.GetRequirePasswordResetViaEmailAsync(7);

			enabled.Should().BeTrue();
		}

		[Test]
		public async Task SaveOrUpdateSettingAsync_ResetModeWrite_InvalidatesItsCache()
		{
			_repository
				.Setup(repository => repository.SaveOrUpdateAsync(
					It.IsAny<DepartmentSetting>(),
					It.IsAny<CancellationToken>(),
					false))
				.ReturnsAsync((DepartmentSetting setting, CancellationToken cancellationToken, bool firstLevelOnly) => setting);
			_cacheProvider
				.Setup(provider => provider.RemoveAsync("DSetRequirePasswordResetViaEmail_7"))
				.ReturnsAsync(true);

			await _service.SaveOrUpdateSettingAsync(
				7,
				"true",
				DepartmentSettingTypes.RequirePasswordResetViaEmail);

			_cacheProvider.Verify(
				provider => provider.RemoveAsync("DSetRequirePasswordResetViaEmail_7"),
				Times.Once);
		}
	}
}
