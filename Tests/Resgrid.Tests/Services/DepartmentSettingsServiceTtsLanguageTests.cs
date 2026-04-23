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
	public class DepartmentSettingsServiceTtsLanguageTests
	{
		private Mock<IDepartmentSettingsRepository> _departmentSettingsRepository;
		private Mock<IAddressService> _addressService;
		private Mock<IGeoLocationProvider> _geoLocationProvider;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentSettingsService _service;
		private bool _originalCacheEnabled;
		private string _originalDefaultVoice;

		[SetUp]
		public void SetUp()
		{
			_originalCacheEnabled = global::Resgrid.Config.SystemBehaviorConfig.CacheEnabled;
			_originalDefaultVoice = TtsConfig.DefaultVoice;

			_departmentSettingsRepository = new Mock<IDepartmentSettingsRepository>();
			_addressService = new Mock<IAddressService>();
			_geoLocationProvider = new Mock<IGeoLocationProvider>();
			_cacheProvider = new Mock<ICacheProvider>();

			_service = new DepartmentSettingsService(
				_departmentSettingsRepository.Object,
				_addressService.Object,
				_geoLocationProvider.Object,
				_cacheProvider.Object);

			global::Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			TtsConfig.DefaultVoice = "en-us";
		}

		[TearDown]
		public void TearDown()
		{
			global::Resgrid.Config.SystemBehaviorConfig.CacheEnabled = _originalCacheEnabled;
			TtsConfig.DefaultVoice = _originalDefaultVoice;
		}

		[Test]
		public async Task should_return_department_tts_language_override_when_supported()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.TtsLanguage))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					Setting = "es-419",
					SettingType = (int)DepartmentSettingTypes.TtsLanguage
				});

			var result = await _service.GetTtsLanguageForDepartmentAsync(7);

			result.Should().Be("es-419");
		}

		[Test]
		public async Task should_fall_back_to_default_tts_language_when_setting_missing()
		{
			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.TtsLanguage))
				.ReturnsAsync((DepartmentSetting)null);

			var result = await _service.GetTtsLanguageForDepartmentAsync(7);

			result.Should().Be("en-us");
		}

		[Test]
		public async Task should_fall_back_to_default_tts_language_when_setting_is_invalid()
		{
			TtsConfig.DefaultVoice = "fr";

			_departmentSettingsRepository
				.Setup(x => x.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.TtsLanguage))
				.ReturnsAsync(new DepartmentSetting
				{
					DepartmentId = 7,
					Setting = "not-a-real-voice",
					SettingType = (int)DepartmentSettingTypes.TtsLanguage
				});

			var result = await _service.GetTtsLanguageForDepartmentAsync(7);

			result.Should().Be("fr");
		}
	}
}
