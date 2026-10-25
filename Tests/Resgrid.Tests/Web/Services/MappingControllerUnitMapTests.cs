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
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Mapping;
using Resgrid.Web.ServicesCore.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Mapping/GetUnitMapData: one unit's position plus the positions of the members seated in its active roles. A unit
	/// position is optional (a team whose members carry the app but whose unit never reports one), and every position
	/// goes through the same TTL, status fallback and location visibility matrix as the department map.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class MappingControllerUnitMapTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 7;
		private const string UserId = "viewer-1";

		private Dictionary<Type, Mock> _mocks;
		private DateTime _now;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_now = DateTime.UtcNow;

			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			M<IMapIconsService>().Setup(x => x.GetResolverAsync(DepartmentId, It.IsAny<IEnumerable<UnitState>>()))
				.ReturnsAsync(new MapIconResolver(DepartmentId, null, null, null, null, null, null));
			M<IDepartmentSettingsService>().Setup(x => x.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId)).ReturnsAsync("39.5,-119.8");
			M<IDepartmentSettingsService>().Setup(x => x.GetBigBoardMapZoomLevelForDepartmentAsync(DepartmentId)).ReturnsAsync(11);
			M<IDepartmentSettingsService>().Setup(x => x.GetMappingUnitLocationTTLAsync(DepartmentId)).ReturnsAsync(60);
			M<IDepartmentSettingsService>().Setup(x => x.GetMappingPersonnelLocationTTLAsync(DepartmentId)).ReturnsAsync(60);

			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 7", Type = "Engine" });
			M<IUnitsService>().Setup(x => x.GetLatestUnitLocationAsync(UnitId, null))
				.ReturnsAsync(new UnitsLocation { UnitId = UnitId, Latitude = 39.51m, Longitude = -119.81m, Timestamp = _now.AddMinutes(-2) });
			M<IUnitsService>().Setup(x => x.GetRolesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitRole>
			{
				new UnitRole { UnitRoleId = 1, UnitId = UnitId, Name = "Officer" },
				new UnitRole { UnitRoleId = 2, UnitId = UnitId, Name = "Driver" },
				new UnitRole { UnitRoleId = 3, UnitId = UnitId, Name = "Firefighter" }
			});
			M<IUnitsService>().Setup(x => x.GetActiveRolesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = UnitId, UserId = "driver-1", Role = "Driver" },
				new UnitActiveRole { UnitId = UnitId, UserId = "officer-1", Role = "Officer" }
			});

			M<IUsersService>().Setup(x => x.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, false, false, false)).ReturnsAsync(new List<UserGroupRole>
			{
				new UserGroupRole { UserId = "officer-1", FirstName = "Olive", LastName = "Officer" },
				new UserGroupRole { UserId = "driver-1", FirstName = "Dana", LastName = "Driver" },
				new UserGroupRole { UserId = "bystander", FirstName = "Pat", LastName = "Walker" }
			});
			M<IUsersService>().Setup(x => x.GetLatestLocationsForDepartmentPersonnelAsync(DepartmentId)).ReturnsAsync(new List<PersonnelLocation>
			{
				new PersonnelLocation { UserId = "officer-1", Latitude = 39.52m, Longitude = -119.82m, Timestamp = _now.AddMinutes(-1) },
				new PersonnelLocation { UserId = "driver-1", Latitude = 39.53m, Longitude = -119.83m, Timestamp = _now.AddMinutes(-3) },
				new PersonnelLocation { UserId = "bystander", Latitude = 39.7m, Longitude = -119.6m, Timestamp = _now }
			});
			M<IActionLogsService>().Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>());

			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserViewPersonLocationViaMatrixAsync(It.IsAny<string>(), UserId, DepartmentId)).ReturnsAsync(true);
		}

		[TearDown]
		public void TearDown()
		{
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

		private MappingController Build()
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
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (MappingController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private async Task<GetUnitMapDataResult> Load(int unitId = UnitId) =>
			(GetUnitMapDataResult)(await Build().GetUnitMapData(unitId)).Result.Should().BeOfType<OkObjectResult>().Which.Value;

		[Test]
		public async Task The_unit_and_its_seated_crew_are_mapped_in_the_units_role_order()
		{
			var result = await Load();

			result.Status.Should().Be(ResponseHelper.Success);
			result.Data.Name.Should().Be("Engine 7");
			result.Data.UnitMarker.Should().NotBeNull();
			result.Data.UnitMarker.Id.Should().Be("u7");
			result.Data.UnitMarker.Latitude.Should().Be(39.51);
			result.Data.UnitLocationTimestamp.Should().NotBeNull();
			result.Data.UnitLocationTimestamp.Value.Kind.Should().Be(DateTimeKind.Utc);

			result.Data.Crew.Select(c => c.UserId).Should().Equal("officer-1", "driver-1");
			result.Data.Crew.Select(c => c.Marker.Id).Should().Equal("pofficer-1", "pdriver-1");
			result.Data.Crew.Select(c => c.Marker.Title).Should().Equal("Olive Officer (Officer)", "Dana Driver (Driver)");
			result.Data.Crew.Select(c => c.Marker.Type).Should().AllBeEquivalentTo(3);
		}

		[Test]
		public async Task A_unit_without_a_position_still_maps_its_crew_and_the_department_view()
		{
			M<IUnitsService>().Setup(x => x.GetLatestUnitLocationAsync(UnitId, null)).ReturnsAsync((UnitsLocation)null);

			var result = await Load();

			result.Data.UnitMarker.Should().BeNull();
			result.Data.UnitLocationTimestamp.Should().BeNull();
			result.Data.Crew.Should().OnlyContain(c => c.Marker != null);
			result.Data.CenterLat.Should().Be(39.5);
			result.Data.CenterLon.Should().Be(-119.8);
			result.Data.ZoomLevel.Should().Be(11);
		}

		[Test]
		public async Task A_ping_past_the_departments_ttl_is_not_a_position()
		{
			M<IUnitsService>().Setup(x => x.GetLatestUnitLocationAsync(UnitId, null))
				.ReturnsAsync(new UnitsLocation { UnitId = UnitId, Latitude = 39.51m, Longitude = -119.81m, Timestamp = _now.AddHours(-3) });

			(await Load()).Data.UnitMarker.Should().BeNull();
		}

		[Test]
		public async Task A_status_with_a_location_stands_in_for_a_missing_ping()
		{
			M<IUnitsService>().Setup(x => x.GetLatestUnitLocationAsync(UnitId, null)).ReturnsAsync((UnitsLocation)null);
			M<IUnitsService>().Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitId))
				.ReturnsAsync(new UnitState { UnitId = UnitId, Latitude = 39.6m, Longitude = -119.9m, Timestamp = _now.AddMinutes(-5) });

			var result = await Load();

			result.Data.UnitMarker.Should().NotBeNull();
			result.Data.UnitMarker.Latitude.Should().Be(39.6);
		}

		[Test]
		public async Task A_hidden_location_is_withheld_but_the_member_is_still_listed()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(false);
			M<IAuthorizationService>().Setup(x => x.CanUserViewPersonLocationViaMatrixAsync("driver-1", UserId, DepartmentId)).ReturnsAsync(false);

			var result = await Load();

			result.Data.UnitMarker.Should().BeNull();
			var driver = result.Data.Crew.Single(c => c.UserId == "driver-1");
			driver.Marker.Should().BeNull();
			driver.LocationTimestamp.Should().BeNull();
			result.Data.Crew.Single(c => c.UserId == "officer-1").Marker.Should().NotBeNull();
			M<IUnitsService>().Verify(x => x.GetLatestUnitLocationAsync(It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never);
		}

		[Test]
		public async Task A_seat_held_by_someone_no_longer_a_current_member_shows_no_one()
		{
			M<IUnitsService>().Setup(x => x.GetActiveRolesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = UnitId, UserId = "removed-9", Role = "Firefighter" },
				new UnitActiveRole { UnitId = UnitId, UserId = "officer-1", Role = "Officer" }
			});

			(await Load()).Data.Crew.Select(c => c.UserId).Should().Equal("officer-1");
		}

		[Test]
		public async Task A_member_in_two_seats_is_one_person_with_both_roles()
		{
			M<IUnitsService>().Setup(x => x.GetActiveRolesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = UnitId, UserId = "officer-1", Role = "Officer" },
				new UnitActiveRole { UnitId = UnitId, UserId = "OFFICER-1", Role = "Driver" }
			});

			var crew = (await Load()).Data.Crew;

			crew.Should().ContainSingle();
			crew[0].RoleName.Should().Be("Officer, Driver");
			crew[0].Marker.Title.Should().Be("Olive Officer (Officer, Driver)");
		}

		[Test]
		public async Task A_seat_whose_role_the_unit_no_longer_defines_is_not_crew()
		{
			M<IUnitsService>().Setup(x => x.GetActiveRolesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = UnitId, UserId = "driver-1", Role = "Renamed Seat" }
			});

			var result = await Load();

			result.Data.Crew.Should().BeEmpty();
			M<IUsersService>().Verify(x => x.GetLatestLocationsForDepartmentPersonnelAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Another_departments_unit_reads_as_missing()
		{
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = 99, Name = "Elsewhere" });

			var result = await Load();

			result.Status.Should().Be(ResponseHelper.NotFound);
			result.Data.Crew.Should().BeEmpty();
			result.Data.UnitMarker.Should().BeNull();
		}

		[Test]
		public async Task A_unit_hidden_by_View_Units_reads_as_missing()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitViaMatrixAsync(UnitId, UserId, DepartmentId)).ReturnsAsync(false);

			(await Load()).Status.Should().Be(ResponseHelper.NotFound);
			M<IUnitsService>().Verify(x => x.GetActiveRolesForUnitAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_deleted_unit_reads_as_missing()
		{
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Gone", IsDeleted = true });

			(await Load()).Status.Should().Be(ResponseHelper.NotFound);
		}

		[Test]
		public async Task Markers_carry_the_status_styled_pin_of_their_type_and_status()
		{
			var engineType = new UnitType { UnitTypeId = 3, DepartmentId = DepartmentId, Type = "Engine", MapIconType = (int)MapIconTypes.Truck };
			var responding = new UnitState { UnitStateId = 90, UnitId = UnitId, State = (int)UnitStateTypes.Responding, Timestamp = _now.AddMinutes(-5) };
			M<IUnitsService>().Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitId)).ReturnsAsync(responding);
			M<IActionLogsService>().Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { UserId = "officer-1", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = _now.AddMinutes(-4) }
			});
			M<IMapIconsService>().Setup(x => x.GetResolverAsync(DepartmentId, It.IsAny<IEnumerable<UnitState>>()))
				.ReturnsAsync(new MapIconResolver(DepartmentId, null, null, new[] { engineType }, null, null, null));

			var result = await Load();

			result.Data.UnitMarker.ImagePath.Should().Be("truck", "older clients keep the plain bundled icon");
			result.Data.UnitMarker.IconStyle.Should().Be(MapIconStyles.Responding);
			result.Data.UnitMarker.PinIconUrl.Should().EndWith("/api/v4/TypeIcons/Pins/truck-responding?v=" + MapIconStyles.Version);
			result.Data.UnitMarker.IconUrl.Should().BeNull();

			var officer = result.Data.Crew.Single(x => x.UserId == "officer-1").Marker;
			officer.ImagePath.Should().Be(TypeIcons.LegacyPersonImage);
			officer.IconStyle.Should().Be(MapIconStyles.OnScene);
			officer.PinIconUrl.Should().EndWith("/api/v4/TypeIcons/Pins/person-onscene?v=" + MapIconStyles.Version);
		}

		[Test]
		public void The_unit_map_reads_with_the_View_Units_policy()
		{
			typeof(MappingController).GetMethod(nameof(MappingController.GetUnitMapData))!
				.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
				.Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single().Policy
				.Should().Be(Resgrid.Providers.Claims.ResgridResources.Unit_View);
		}
	}
}
