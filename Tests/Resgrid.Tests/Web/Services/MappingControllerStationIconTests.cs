using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Mapping;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Station markers (Type 2) on Mapping/GetMapDataAndMarkers and the Stations rows of Mapping/GetMapIconLegend carry the
	/// station's own uploaded icon, else the department's station icon, in IconUrl; ImagePath stays "Station" for older apps.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class MappingControllerStationIconTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "viewer-1";
		private const string Api = "https://api.example.test";

		private Dictionary<Type, Mock> _mocks;
		private string _apiBaseUrl;

		private static readonly List<DepartmentGroup> Stations = new List<DepartmentGroup>
		{
			new DepartmentGroup { DepartmentGroupId = 41, DepartmentId = DepartmentId, Name = "Station 1", Type = (int)DepartmentGroupTypes.Station, Latitude = "39.5", Longitude = "-119.8" },
			new DepartmentGroup { DepartmentGroupId = 42, DepartmentId = DepartmentId, Name = "Ambulance post", Type = (int)DepartmentGroupTypes.Station, Latitude = "39.6", Longitude = "-119.7" }
		};

		private static MapIconResolver Resolver(params TypeIconVariant[] stationIcons) =>
			new MapIconResolver(DepartmentId, null, null, null, null, null, null, stationIcons: stationIcons);

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_apiBaseUrl = Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = Api;

			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			M<IDepartmentGroupsService>().Setup(x => x.GetAllStationGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(Stations);
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>());
			M<IUnitsService>().Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId)).ReturnsAsync(new List<UnitsLocation>());
			M<IUnitsService>().Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole>());
			M<IUsersService>().Setup(x => x.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, false, false, false)).ReturnsAsync(new List<UserGroupRole>());
			M<IUsersService>().Setup(x => x.GetLatestLocationsForDepartmentPersonnelAsync(DepartmentId)).ReturnsAsync(new List<PersonnelLocation>());
			M<IActionLogsService>().Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>());
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = _apiBaseUrl;
			ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		private MappingController Build(string client = null, string appVersion = null)
		{
			var constructor = typeof(MappingController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			if (client != null)
				http.Request.Headers["X-Resgrid-Client"] = client;
			if (appVersion != null)
				http.Request.Headers["X-Resgrid-App-Version"] = appVersion;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (MappingController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private async Task<Dictionary<string, MapMakerInfoData>> StationMarkers(MapIconResolver resolver, string client = null, string appVersion = null, bool fullSizeIcons = false)
		{
			M<IMapIconsService>().Setup(x => x.GetResolverAsync(DepartmentId, It.IsAny<IEnumerable<UnitState>>())).ReturnsAsync(resolver);

			var result = (GetMapDataResult)(await Build(client, appVersion).GetMapDataAndMarkers(fullSizeIcons)).Result.Should().BeOfType<OkObjectResult>().Which.Value;
			return result.Data.MapMakerInfos.Where(m => m.Type == 2).ToDictionary(m => m.Id);
		}

		// TEMP (2026-11-11, LegacyOmniPinIcons): Omni store builds that draw a marker's image at its pixel size get the 32 px icon.
		[TestCase("unit", "8.25", false, true, TestName = "Omni Unit store build gets the 32 px icon")]
		[TestCase("unit", "8.30", true, false, TestName = "Fixed Omni build asking for full size icons keeps 128 px")]
		[TestCase("unit", "7.655", false, false, TestName = "Legacy Unit keeps 128 px")]
		[TestCase(null, null, false, false, TestName = "Client without headers keeps 128 px")]
		public async Task Station_icons_follow_the_clients_pin_drawing(string client, string appVersion, bool fullSizeIcons, bool mapSize)
		{
			var markers = await StationMarkers(Resolver(
				new TypeIconVariant { TypeKind = (int)TypeIconKinds.Station, TypeId = TypeIcons.DepartmentStationTypeId, IconKey = "all-stations" }), client, appVersion, fullSizeIcons);

			var full = TypeIcons.ApiIconUrl(Api, "all-stations");
			markers.Values.Should().OnlyContain(m => m.IconUrl == (mapSize ? TypeIcons.AtMapSize(full) : full));
		}

		[Test]
		public async Task Station_markers_carry_their_own_icon_else_the_departments()
		{
			var markers = await StationMarkers(Resolver(
				new TypeIconVariant { TypeKind = (int)TypeIconKinds.Station, TypeId = 42, IconKey = "ambulance-post" },
				new TypeIconVariant { TypeKind = (int)TypeIconKinds.Station, TypeId = TypeIcons.DepartmentStationTypeId, IconKey = "all-stations" }));

			markers["s42"].IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "ambulance-post"));
			markers["s41"].IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "all-stations"));
			markers.Values.Should().OnlyContain(m => m.ImagePath == "Station", "older apps keep drawing the bundled station pin");
			markers.Values.Should().OnlyContain(m => m.PinIconUrl == null);
		}

		[Test]
		public async Task Without_uploads_station_markers_keep_the_bundled_pin()
		{
			var markers = await StationMarkers(Resolver());

			markers.Keys.Should().BeEquivalentTo("s41", "s42");
			markers.Values.Should().OnlyContain(m => m.IconUrl == null && m.ImagePath == "Station");
		}

		[Test]
		public async Task The_app_legend_lists_the_station_icons()
		{
			var resolver = Resolver(
				new TypeIconVariant { TypeKind = (int)TypeIconKinds.Station, TypeId = 42, IconKey = "ambulance-post" },
				new TypeIconVariant { TypeKind = (int)TypeIconKinds.Station, TypeId = TypeIcons.DepartmentStationTypeId, IconKey = "all-stations" });
			var legend = MapIconLegendBuilder.Build(resolver, null, null, null, null, null, "c", "u", "r", Stations);
			M<IMapIconsService>().Setup(x => x.GetLegendAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(legend);

			var data = ((GetMapIconLegendResult)(await Build().GetMapIconLegend()).Result.Should().BeOfType<OkObjectResult>().Which.Value).Data;

			data.Stations.Select(x => x.Id).Should().Equal(null, 42);
			data.Stations[0].IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "all-stations"));
			data.Stations[0].ImagePath.Should().Be("Station");
			data.Stations[1].Name.Should().Be("Ambulance post");
			data.Stations[1].IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "ambulance-post"));
			data.Stations.Should().OnlyContain(x => x.States.Count == 0 && x.PinIconUrl == null);
		}
	}
}
