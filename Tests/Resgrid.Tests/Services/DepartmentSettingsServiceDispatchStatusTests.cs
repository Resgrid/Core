using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DepartmentSettingsServiceDispatchStatusTests
	{
		private Mock<IDepartmentSettingsRepository> _departmentSettingsRepository;
		private Mock<IAddressService> _addressService;
		private Mock<IGeoLocationProvider> _geoLocationProvider;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentSettingsService _service;

		[SetUp]
		public void SetUp()
		{
			_departmentSettingsRepository = new Mock<IDepartmentSettingsRepository>();
			_addressService = new Mock<IAddressService>();
			_geoLocationProvider = new Mock<IGeoLocationProvider>();
			_cacheProvider = new Mock<ICacheProvider>();

			_service = new DepartmentSettingsService(
				_departmentSettingsRepository.Object,
				_addressService.Object,
				_geoLocationProvider.Object,
				_cacheProvider.Object);
		}

		[Test]
		public async Task GetUnitCallDispatchStatusToSetAsync_returns_minus_one_when_setting_is_missing()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.UnitCallDispatchStatusToSet))
				.ReturnsAsync((DepartmentSetting)null);

			var result = await _service.GetUnitCallDispatchStatusToSetAsync(7);

			result.Should().Be(-1);
		}

		[Test]
		public async Task GetUnitCallReleaseStatusToSetAsync_returns_saved_value()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.UnitCallReleaseStatusToSet))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.UnitCallReleaseStatusToSet,
					Setting = "10"
				});

			var result = await _service.GetUnitCallReleaseStatusToSetAsync(7);

			result.Should().Be(10);
		}

		[Test]
		public async Task GetUnitCallDispatchStatusToSetAsync_returns_minus_one_for_non_builtin_value()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.UnitCallDispatchStatusToSet))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.UnitCallDispatchStatusToSet,
					Setting = "99"
				});

			var result = await _service.GetUnitCallDispatchStatusToSetAsync(7);

			result.Should().Be(-1);
		}

		[Test]
		public async Task GetUnitCallStatusOverridesByUnitTypeAsync_returns_saved_overrides()
		{
			var setting = new UnitTypeCallStatusOverrideSetting
			{
				Overrides = new System.Collections.Generic.List<UnitTypeCallStatusOverride>
				{
					new UnitTypeCallStatusOverride { UnitTypeId = 12, DispatchStatus = 30, ReleaseStatus = 31 }
				}
			};

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.UnitCallStatusOverridesByUnitType))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					SettingType = (int)DepartmentSettingTypes.UnitCallStatusOverridesByUnitType,
					Setting = Resgrid.Framework.ObjectSerialization.Serialize(setting)
				});

			var result = await _service.GetUnitCallStatusOverridesByUnitTypeAsync(7);

			result.Should().HaveCount(1);
			result[0].UnitTypeId.Should().Be(12);
			result[0].DispatchStatus.Should().Be(30);
			result[0].ReleaseStatus.Should().Be(31);
		}
	}
}
