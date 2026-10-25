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
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Mapping/GetMapDataAndMarkers labels a person's pin "Name (Unit)" while they hold an active role on a unit the
	/// caller may view. The web map, BigBoard, the mobile apps and the MCP personnel tool all read this title.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class MappingControllerPersonnelLabelTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "viewer-1";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			M<IDepartmentsService>().Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			M<IMapIconsService>().Setup(x => x.GetResolverAsync(DepartmentId, It.IsAny<IEnumerable<UnitState>>()))
				.ReturnsAsync(new MapIconResolver(DepartmentId, null, null, null, null, null, null));
			M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "Engine 1" },
				new Unit { UnitId = 2, DepartmentId = DepartmentId, Name = "Hidden Squad" }
			});
			M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>());
			M<IUnitsService>().Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId)).ReturnsAsync(new List<UnitsLocation>());
			M<IUnitsService>().Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { DepartmentId = DepartmentId, UnitId = 1, UserId = "shawn", Role = "Operator" },
				new UnitActiveRole { DepartmentId = DepartmentId, UnitId = 2, UserId = "hidden-rider", Role = "Driver" }
			});
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitViaMatrixAsync(1, UserId, DepartmentId)).ReturnsAsync(true);

			var now = DateTime.UtcNow;
			M<IUsersService>().Setup(x => x.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, false, false, false)).ReturnsAsync(new List<UserGroupRole>
			{
				new UserGroupRole { UserId = "shawn", FirstName = "Shawn", LastName = "Jackson" },
				new UserGroupRole { UserId = "hidden-rider", FirstName = "Hidden", LastName = "Rider" },
				new UserGroupRole { UserId = "walker", FirstName = "Pat", LastName = "Walker" }
			});
			M<IUsersService>().Setup(x => x.GetLatestLocationsForDepartmentPersonnelAsync(DepartmentId)).ReturnsAsync(new List<PersonnelLocation>
			{
				new PersonnelLocation { UserId = "shawn", Latitude = 39.5m, Longitude = -119.8m, Timestamp = now },
				new PersonnelLocation { UserId = "hidden-rider", Latitude = 39.6m, Longitude = -119.7m, Timestamp = now },
				new PersonnelLocation { UserId = "walker", Latitude = 39.7m, Longitude = -119.6m, Timestamp = now }
			});
			M<IActionLogsService>().Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>());
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

		private async Task<Dictionary<string, string>> PersonnelTitles()
		{
			var result = (GetMapDataResult)(await Build().GetMapDataAndMarkers()).Result.Should().BeOfType<OkObjectResult>().Which.Value;

			return result.Data.MapMakerInfos.Where(m => m.Type == 3).ToDictionary(m => m.Id, m => m.Title);
		}

		[Test]
		public async Task A_member_seated_on_a_unit_is_labelled_with_it_and_others_keep_their_name()
		{
			var titles = await PersonnelTitles();

			titles["pshawn"].Should().Be("Shawn Jackson (Engine 1)");
			titles["pwalker"].Should().Be("Pat Walker");
		}

		[Test]
		public async Task A_unit_the_viewer_may_not_see_is_not_named()
		{
			var titles = await PersonnelTitles();

			titles["phidden-rider"].Should().Be("Hidden Rider");
		}

		[Test]
		public async Task Clearing_the_seat_drops_the_unit_from_the_label()
		{
			M<IUnitsService>().Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole>());

			var titles = await PersonnelTitles();

			titles["pshawn"].Should().Be("Shawn Jackson");
		}
	}
}
