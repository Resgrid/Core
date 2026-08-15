using System.Threading;
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
	/// <summary>
	/// The department's map center drives every map in the web app and all five client apps, so the
	/// rule that matters is: an operator-supplied pin is never moved by the geocoder.
	/// </summary>
	[TestFixture]
	public class DepartmentSettingsServiceMapCenterTests
	{
		private const int DepartmentId = 42;

		private Mock<IDepartmentSettingsRepository> _departmentSettingsRepository;
		private Mock<IAddressService> _addressService;
		private Mock<IGeoLocationProvider> _geoLocationProvider;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentSettingsService _service;

		private static Address ZottegemAddress => new Address
		{
			Address1 = "Nieuwstraat 14",
			City = "Zottegem",
			State = "Oost-Vlaanderen",
			PostalCode = "9620",
			Country = "Belgium"
		};

		[SetUp]
		public void SetUp()
		{
			_departmentSettingsRepository = new Mock<IDepartmentSettingsRepository>();
			_addressService = new Mock<IAddressService>();
			_geoLocationProvider = new Mock<IGeoLocationProvider>();
			_cacheProvider = new Mock<ICacheProvider>();

			_departmentSettingsRepository
				.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentSetting setting, CancellationToken _, bool __) => setting);

			_service = new DepartmentSettingsService(
				_departmentSettingsRepository.Object,
				_addressService.Object,
				_geoLocationProvider.Object,
				_cacheProvider.Object);
		}

		private DepartmentSetting CapturedSetting()
		{
			DepartmentSetting captured = null;

			_departmentSettingsRepository.Verify(
				x => x.SaveOrUpdateAsync(It.Is<DepartmentSetting>(s => AssignAndPass(s, out captured)), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
				Times.AtLeastOnce);

			return captured;
		}

		private static bool AssignAndPass(DepartmentSetting setting, out DepartmentSetting captured)
		{
			captured = setting;
			return true;
		}

		[Test]
		public async Task Stores_supplied_coordinates_verbatim_without_geocoding()
		{
			var result = await _service.SaveMapCenterCoordinatesAsync(DepartmentId, "50.8698", "3.8102", ZottegemAddress);

			result.Should().NotBeNull();
			result.Latitude.Should().BeApproximately(50.8698, 0.00001);
			result.Longitude.Should().BeApproximately(3.8102, 0.00001);

			// The whole point: a hand-set pin must never trigger, or be replaced by, a geocode.
			_geoLocationProvider.Verify(x => x.GetLatLonFromAddress(It.IsAny<string>()), Times.Never);

			var saved = CapturedSetting();
			saved.SettingType.Should().Be((int)DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates);
			saved.Setting.Should().Be("50.8698,3.8102");
		}

		[Test]
		public async Task Geocodes_the_department_address_when_both_coordinates_are_blank()
		{
			_geoLocationProvider.Setup(x => x.GetLatLonFromAddress(It.IsAny<string>())).ReturnsAsync("50.8698,3.8102");

			var result = await _service.SaveMapCenterCoordinatesAsync(DepartmentId, "", "   ", ZottegemAddress);

			result.Should().NotBeNull();
			result.Latitude.Should().BeApproximately(50.8698, 0.00001);
			result.Longitude.Should().BeApproximately(3.8102, 0.00001);

			CapturedSetting().Setting.Should().Be("50.8698,3.8102");
		}

		[TestCase("50.8698", "")]
		[TestCase("", "3.8102")]
		public async Task Leaves_the_stored_value_alone_when_only_one_coordinate_is_filled_in(string latitude, string longitude)
		{
			// Half-filled is an operator mid-edit, not an instruction to geocode. Doing nothing is the
			// only reading that cannot lose their work.
			var result = await _service.SaveMapCenterCoordinatesAsync(DepartmentId, latitude, longitude, ZottegemAddress);

			result.Should().BeNull();
			_geoLocationProvider.Verify(x => x.GetLatLonFromAddress(It.IsAny<string>()), Times.Never);
			_departmentSettingsRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Does_nothing_when_there_is_no_address_to_geocode()
		{
			var result = await _service.SaveMapCenterCoordinatesAsync(DepartmentId, null, null, null);

			result.Should().BeNull();
			_geoLocationProvider.Verify(x => x.GetLatLonFromAddress(It.IsAny<string>()), Times.Never);
			_departmentSettingsRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Stores_nothing_when_the_geocoder_cannot_resolve_the_address()
		{
			_geoLocationProvider.Setup(x => x.GetLatLonFromAddress(It.IsAny<string>())).ReturnsAsync((string)null);

			var result = await _service.SaveMapCenterCoordinatesAsync(DepartmentId, null, null, ZottegemAddress);

			result.Should().BeNull();
			_departmentSettingsRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Survives_a_throwing_geocoder()
		{
			// A provider outage must not stop the rest of the department settings save.
			_geoLocationProvider.Setup(x => x.GetLatLonFromAddress(It.IsAny<string>())).ThrowsAsync(new System.Exception("provider down"));

			var result = await _service.SaveMapCenterCoordinatesAsync(DepartmentId, null, null, ZottegemAddress);

			result.Should().BeNull();
		}
	}
}
