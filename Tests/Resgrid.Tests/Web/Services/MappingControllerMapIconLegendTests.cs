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
	/// Mapping/GetMapIconLegend: the apps' map legend, from the same department legend as the web page, with every icon as an
	/// absolute API URL in the marker fields (IconUrl, PinIconUrl, ImagePath) so a client draws it exactly like its markers.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class MappingControllerMapIconLegendTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "viewer-1";
		private const string Api = "https://api.example.test";

		private Dictionary<Type, Mock> _mocks;
		private string _apiBaseUrl;

		private static readonly CallType FireType = new CallType { CallTypeId = 11, DepartmentId = DepartmentId, Type = "Structure Fire", MapIconType = (int)MapIconTypes.Fire };
		private static readonly CallType MedicalType = new CallType { CallTypeId = 12, DepartmentId = DepartmentId, Type = "Medical", IconKey = "medical-own" };
		private static readonly UnitType EngineType = new UnitType { UnitTypeId = 21, DepartmentId = DepartmentId, Type = "Engine" };

		private static readonly List<DepartmentCallPriority> Priorities = new List<DepartmentCallPriority>
		{
			new DepartmentCallPriority { DepartmentCallPriorityId = 901, DepartmentId = DepartmentId, Name = "Hot", BaseType = (int)CallPriority.Emergency, Color = "#ff0000" },
			new DepartmentCallPriority { DepartmentCallPriorityId = 902, DepartmentId = DepartmentId, Name = "Zebra", BaseType = -1, Color = "red" }
		};

		private static readonly List<CustomStateDetail> UnitStatuses = new List<CustomStateDetail>
		{
			new CustomStateDetail { CustomStateDetailId = (int)UnitStateTypes.Available, ButtonText = "Available" }
		};

		private static readonly List<CustomStateDetail> PersonnelStatuses = new List<CustomStateDetail>
		{
			new CustomStateDetail { CustomStateDetailId = (int)ActionTypes.StandingBy, ButtonText = "Standing By" },
			new CustomStateDetail { CustomStateDetailId = (int)ActionTypes.Responding, ButtonText = "Responding" },
			new CustomStateDetail { CustomStateDetailId = (int)ActionTypes.OnUnit, ButtonText = "On Unit" }
		};

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_apiBaseUrl = Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = Api;

			var resolver = new MapIconResolver(DepartmentId, new[] { FireType, MedicalType }, Priorities, new[] { EngineType }, null, null,
				new[] { new TypeIconVariant { TypeKind = (int)TypeIconKinds.UnitType, TypeId = EngineType.UnitTypeId, Variant = "s0", IconKey = "engine-ready" } });
			var legend = MapIconLegendBuilder.Build(resolver, Priorities, null, UnitStatuses, PersonnelStatuses, null, "No call type", "No unit type", "No role icon");

			M<IMapIconsService>().Setup(x => x.GetLegendAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(legend);
			M<IMappingService>().Setup(x => x.GetPOITypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PoiType>
			{
				new PoiType { PoiTypeId = 3, DepartmentId = DepartmentId, Name = "Water tank", Color = "#0055ff", Image = "map-icon-fire-station", Marker = "MAP_PIN" }
			});
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

		private async Task<GetMapIconLegendResultData> Load() =>
			((GetMapIconLegendResult)(await Build().GetMapIconLegend()).Result.Should().BeOfType<OkObjectResult>().Which.Value).Data;

		[Test]
		public async Task Calls_carry_the_departments_priorities_and_the_icons_their_markers_show()
		{
			var data = await Load();

			data.Calls.Select(x => x.Name).Should().Equal("No call type", "Medical", "Structure Fire");
			data.Calls[0].Id.Should().BeNull();

			var fire = data.Calls[2];
			fire.Id.Should().Be(FireType.CallTypeId);
			fire.ImagePath.Should().Be("fire", "the built-in icon the type picked");
			fire.IconUrl.Should().BeNull();
			fire.States.Select(x => x.Id).Should().Equal(901, 902);
			fire.States.Select(x => x.Name).Should().Equal("Hot", "Zebra");
			fire.States.Select(x => x.Color).Should().Equal("#ff0000", null);
			fire.States.Select(x => x.IconStyle).Should().Equal(MapIconStyles.PriorityEmergency, MapIconStyles.Unknown);
			fire.States[0].PinIconUrl.Should().Be(TypeIcons.ApiPinUrl(Api, "fire-emergency"));
			fire.States[0].IconUrl.Should().BeNull();
			fire.States[0].ImagePath.Should().Be("fire");

			var medical = data.Calls[1];
			medical.IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "medical-own", null), "the type's own upload, no badge");
			medical.States[0].IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "medical-own", MapIconStyles.PriorityEmergency));
			medical.States[0].IsVariant.Should().BeFalse();
			medical.States[0].PinIconUrl.Should().Be(TypeIcons.ApiPinUrl(Api, "call-emergency"), "the pin stays the fallback when the upload fails to load");
			medical.States[1].IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "medical-own", null), "an unknown priority leaves the upload unbadged");
		}

		[Test]
		public async Task Units_and_personnel_carry_their_statuses_and_status_icons()
		{
			var data = await Load();

			data.Units.Select(x => x.Name).Should().Equal("No unit type", "Engine");
			var engine = data.Units[1];
			engine.StatusSetName.Should().BeNull();
			engine.States.Single().Name.Should().Be("Available");
			engine.States.Single().IconUrl.Should().Be(TypeIcons.ApiIconUrl(Api, "engine-ready", null));
			engine.States.Single().IsVariant.Should().BeTrue();
			data.Units[0].States.Single().PinIconUrl.Should().Be(TypeIcons.ApiPinUrl(Api, "engine-available"));

			data.Personnel.Select(x => x.Name).Should().Equal("No role icon");
			data.Personnel[0].States.Select(x => x.Name).Should().Equal("Standing By", "Responding", "On Unit");
			data.Personnel[0].States.Select(x => x.PinIconUrl).Should().Equal(TypeIcons.ApiPinUrl(Api, "person-available"), TypeIcons.ApiPinUrl(Api, "person-responding"),
				TypeIcons.ApiPinUrl(Api, "person-unknown"));
			data.Personnel[0].States.Select(x => x.FollowsUnitStatus).Should().Equal(false, false, true);
			data.StatusStyles.Last().PersonnelStatuses.Should().BeEmpty("On Unit follows its unit, it is not a status without meaning");
		}

		[Test]
		public async Task The_colour_key_names_the_departments_statuses_and_priorities_and_links_each_styles_pins()
		{
			var data = await Load();

			data.StatusStyles.Select(x => x.Key).Should().Equal(MapIconStyles.StatusStyles.Concat(new[] { MapIconStyles.Unknown }));
			var available = data.StatusStyles[0];
			available.Color.Should().Be(MapIconStyles.ColorOf(MapIconStyles.Available));
			available.UnitStatuses.Should().Equal("Available");
			available.PersonnelStatuses.Should().Equal("Standing By");
			available.Meanings.Should().Contain((int)ActionBaseTypes.Available);
			available.UnitPinIconUrl.Should().Be(TypeIcons.ApiPinUrl(Api, "engine-available"));
			available.PersonPinIconUrl.Should().Be(TypeIcons.ApiPinUrl(Api, "person-available"));
			data.StatusStyles.Last().UnitPinIconUrl.Should().Be(TypeIcons.ApiPinUrl(Api, "engine-unknown"));

			data.PriorityStyles.Select(x => x.Key).Should().Equal(MapIconStyles.PriorityStyles.Concat(new[] { MapIconStyles.Unknown }));
			data.PriorityStyles.Single(x => x.Key == MapIconStyles.PriorityEmergency).Priorities.Should().Equal("Hot");
			data.PriorityStyles.Last().Priorities.Should().Equal("Zebra");
			data.PriorityStyles.Last().CallPinIconUrl.Should().Be(TypeIcons.ApiPinUrl(Api, "call-unknown"));
		}

		[Test]
		public async Task Poi_types_carry_the_icon_their_markers_show()
		{
			var poi = (await Load()).PoiTypes.Single();

			poi.PoiTypeId.Should().Be(3);
			poi.Name.Should().Be("Water tank");
			poi.Color.Should().Be("#0055ff");
			poi.PoiImage.Should().Be("map-icon-fire-station");
			poi.Marker.Should().Be("MAP_PIN");
			poi.IconUrl.Should().BeNull();
		}
	}
}
