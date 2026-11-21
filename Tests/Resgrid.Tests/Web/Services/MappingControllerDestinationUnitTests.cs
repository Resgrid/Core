using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
	/// v4 Mapping/GetSuggestedDestinations and GetNearestDestinations (PR #587): the travelling unit shapes the destination rules
	/// and the routing profile, so a unit Security > View Units hides from the caller (or another department's) is ignored, and
	/// the origin coordinates must both be sent (a missing one would bind as 0, a real place).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class MappingControllerDestinationUnitTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "viewer-1";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = DepartmentId, Type = "Helicopter" });
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(8)).ReturnsAsync(new Unit { UnitId = 8, DepartmentId = DepartmentId, Type = "Water Tender" });
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { UnitId = 9, DepartmentId = 99, Type = "Helicopter" });
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitViaMatrixAsync(7, UserId, DepartmentId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitViaMatrixAsync(8, UserId, DepartmentId)).ReturnsAsync(false);
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitViaMatrixAsync(9, UserId, DepartmentId)).ReturnsAsync(true);

			M<INearestDestinationService>().Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DestinationSuggestions { Mode = DestinationSuggestionModes.Suggest });
			M<INearestDestinationService>().Setup(x => x.GetNearestDestinationsAsync(It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string>(),
					It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<NearestDestination>());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

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

		[TestCase(7, 7)]
		[TestCase(8, null)]
		[TestCase(9, null)]
		[TestCase(404, null)]
		public async Task Suggestions_use_the_unit_only_when_the_caller_can_view_it(int unitId, int? expected)
		{
			await Build().GetSuggestedDestinations(50.8, 3.8, "Burns", 1, null, unitId);

			M<INearestDestinationService>().Verify(x => x.SuggestDestinationsAsync(It.Is<DestinationSuggestionRequest>(r => r.UnitId == expected),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase(7, 7)]
		[TestCase(8, null)]
		public async Task Nearest_destinations_use_the_unit_only_when_the_caller_can_view_it(int unitId, int? expected)
		{
			await Build().GetNearestDestinations(50.8, 3.8, null, unitId);

			M<INearestDestinationService>().Verify(x => x.GetNearestDestinationsAsync(DepartmentId, 50.8, 3.8, null, expected, It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase(nameof(MappingController.GetSuggestedDestinations))]
		[TestCase(nameof(MappingController.GetNearestDestinations))]
		public void Both_origin_coordinates_are_required(string action)
		{
			var parameters = typeof(MappingController).GetMethod(action).GetParameters();

			parameters.Where(p => p.Name == "latitude" || p.Name == "longitude")
				.Should().HaveCount(2).And.OnlyContain(p => p.GetCustomAttribute<BindRequiredAttribute>() != null);
		}

		/// <summary>Omni Dispatch's Destinations screen shows the rules read-only: names resolved, deleted references counted.</summary>
		[Test]
		public async Task Destination_rules_come_with_names_and_the_suggestion_setting()
		{
			M<IDepartmentSettingsService>().Setup(x => x.GetDestinationSuggestionConfigAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DestinationSuggestionConfig { Mode = (int)DestinationSuggestionModes.SuggestAndFill, PreferredWithinMinutes = 5 });
			M<ICallsService>().Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 3, Type = "Burns" } });
			M<ICallsService>().Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new List<DepartmentCallPriority> { new DepartmentCallPriority { DepartmentCallPriorityId = 3, Name = "Emergency" } });
			M<IUnitsService>().Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitType> { new UnitType { UnitTypeId = 7, Type = "Helicopter" } });
			M<IMappingService>().Setup(x => x.GetPOITypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PoiType>
			{
				new PoiType { PoiTypeId = 10, Name = "Hospitals", IsDestination = true },
				new PoiType { PoiTypeId = 11, Name = "Parks", IsDestination = false }
			});
			M<IMappingService>().Setup(x => x.GetPoiCapabilitiesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = "burn", Name = "Burn Center" },
				new PoiCapability { PoiCapabilityId = "heli", Name = "Helipad" }
			});
			M<IDestinationRulesService>().Setup(x => x.GetRulesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DestinationRule>
			{
				new DestinationRule { DestinationRuleId = "burns", Name = "Burns", CallTypeId = 3, PoiTypeIds = "10", RequiredCapabilityIds = "BURN" },
				new DestinationRule { DestinationRuleId = "air", Name = "Helicopter", UnitTypeId = 7, CallPriority = 3, PreferredCapabilityIds = "heli" },
				new DestinationRule { DestinationRuleId = "stale", Name = "Old rule", CallTypeId = 99, PoiTypeIds = "10,11", RequiredCapabilityIds = "gone" }
			});

			var response = await Build().GetDestinationRules();

			var data = ((DestinationRulesResult)((OkObjectResult)response.Result).Value).Data;
			data.Mode.Should().Be((int)DestinationSuggestionModes.SuggestAndFill);
			data.PreferredWithinMinutes.Should().Be(5);
			data.Rules.Select(x => x.DestinationRuleId).Should().Equal("burns", "air", "stale");

			var burns = data.Rules[0];
			burns.CallTypeName.Should().Be("Burns");
			burns.PoiTypeNames.Should().Equal("Hospitals");
			burns.RequiredCapabilityNames.Should().Equal("Burn Center");
			burns.MissingReferenceCount.Should().Be(0);

			var air = data.Rules[1];
			air.UnitTypeName.Should().Be("Helicopter");
			air.CallPriorityName.Should().Be("Emergency");
			air.PoiTypeIds.Should().BeEmpty();
			air.PreferredCapabilityNames.Should().Equal("Helipad");

			// Call type 99 deleted, "Parks" is no destination type any more, capability "gone" deleted.
			var stale = data.Rules[2];
			stale.CallTypeId.Should().Be(99);
			stale.CallTypeName.Should().BeNull();
			stale.PoiTypeNames.Should().Equal("Hospitals");
			stale.RequiredCapabilityIds.Should().Equal("gone");
			stale.RequiredCapabilityNames.Should().BeEmpty();
			stale.MissingReferenceCount.Should().Be(3);
		}

		[Test]
		public async Task Destination_rules_are_empty_without_rules_or_setting()
		{
			var response = await Build().GetDestinationRules();

			var data = ((DestinationRulesResult)((OkObjectResult)response.Result).Value).Data;
			data.Mode.Should().Be((int)DestinationSuggestionModes.Off);
			data.Rules.Should().BeEmpty();
		}
	}
}
