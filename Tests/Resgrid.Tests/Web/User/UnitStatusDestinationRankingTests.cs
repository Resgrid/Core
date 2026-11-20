using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Units page's Set Status modal for one unit: with the department's destination suggestions on, the destinations come
	/// ranked for the unit by the destination rules (its type, its working call's type and priority), the suggestion and what
	/// each lacks named, and a transport status pre-selects the destination the apps would. Without a unit (the multi-unit
	/// modal) or with suggestions off, the list is today's, grouped by type alphabetically.
	/// </summary>
	[TestFixture]
	public class UnitStatusDestinationRankingTests
	{
		private const int Dept = 7;
		private const string Me = "dispatcher-1";
		private const int UnitId = 3;
		private const int WorkingCallId = 101;
		private const int TransportingDetailId = 70;
		private const int ToWaterSourceDetailId = 71;
		private const int RespondingDetailId = 72;

		private const int AlphaClinic = 1;
		private const int BurnCenter = 2;
		private const int ZuluGeneral = 3;
		private const int Lake = 4;

		private Mock<ICallsService> _calls;
		private Mock<IDispatchScopeService> _scope;
		private Mock<IUnitsService> _units;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<INearestDestinationService> _nearest;
		private Mock<ICallStatusAttributionService> _attribution;
		private Call _workingCall;
		private DefaultHttpContext _http;
		private UnitsController _controller;

		private static readonly Dictionary<string, string> RulesText = new Dictionary<string, string>
		{
			{ "BestForUnit", "Best for this unit" },
			{ "SuggestedBadge", "Suggested" },
			{ "MissingCapabilities", "Missing: {0}" },
			{ "MinutesShort", "{0} min" },
			{ "StatusDiversion", "Diversion" }
		};

		[SetUp]
		public void SetUp()
		{
			_workingCall = new Call { CallId = WorkingCallId, DepartmentId = Dept, Name = "Kitchen fire, burns", Type = "Burns", CallTypeId = 31, Priority = 2, GeoLocationData = "51.05,3.72" };

			_calls = new Mock<ICallsService>();
			_calls.Setup(x => x.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(() => new List<Call> { _workingCall });

			_scope = new Mock<IDispatchScopeService>();
			_scope.Setup(x => x.FilterCallsForUserAsync(Dept, Me, It.IsAny<List<Call>>())).ReturnsAsync((int d, string u, List<Call> calls) => calls);

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetDefaultUnitStatuses()).Returns(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = TransportingDetailId, ButtonText = "Transporting", DetailType = (int)CustomStateDetailTypes.CallsAndPois, BaseType = (int)ActionBaseTypes.Transporting },
				new CustomStateDetail { CustomStateDetailId = ToWaterSourceDetailId, ButtonText = "To Water Source", DetailType = (int)CustomStateDetailTypes.Pois },
				new CustomStateDetail { CustomStateDetailId = RespondingDetailId, ButtonText = "Responding", DetailType = (int)CustomStateDetailTypes.Calls }
			});

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllStationGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>());

			var hospital = new PoiType { PoiTypeId = 10, DepartmentId = Dept, Name = "Hospital", IsDestination = true };
			var water = new PoiType { PoiTypeId = 11, DepartmentId = Dept, Name = "Water Source", IsDestination = true };
			var mapping = new Mock<IMappingService>();
			mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(Dept)).ReturnsAsync(() => new List<Poi>
			{
				new Poi { PoiId = ZuluGeneral, PoiTypeId = 10, Type = hospital, Name = "Zulu General" },
				new Poi { PoiId = AlphaClinic, PoiTypeId = 10, Type = hospital, Name = "Alpha Clinic" },
				new Poi { PoiId = BurnCenter, PoiTypeId = 10, Type = hospital, Name = "Burn Center" },
				new Poi { PoiId = Lake, PoiTypeId = 11, Type = water, Name = "Lake" }
			});
			mapping.Setup(x => x.GetPoiCapabilitiesForDepartmentAsync(Dept)).ReturnsAsync(new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = "helipad", DepartmentId = Dept, Name = "Helipad" }
			});

			_settings = new Mock<IDepartmentSettingsService>();
			SetMode(DestinationSuggestionModes.Suggest);

			_units = new Mock<IUnitsService>();
			_units.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = Dept, Name = "Medic 1", Type = "Ambulance" });

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(UnitId, Me, Dept)).ReturnsAsync(true);

			_attribution = new Mock<ICallStatusAttributionService>();
			_attribution.Setup(x => x.GetWorkingCallIdForUnitAsync(Dept, UnitId, It.IsAny<UnitState>())).ReturnsAsync(WorkingCallId);

			_nearest = new Mock<INearestDestinationService>();
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DestinationSuggestionRequest r, CancellationToken _) => Suggestions());

			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			var common = new Mock<IStringLocalizer<Resgrid.Localization.Common>>();
			common.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var rules = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.DestinationRules>>();
			rules.Setup(l => l[It.IsAny<string>()]).Returns((string key) =>
				RulesText.TryGetValue(key, out var value) ? new LocalizedString(key, value) : new LocalizedString(key, key, true));

			_controller = new UnitsController(Mock.Of<IDepartmentsService>(), Mock.Of<IUsersService>(), _units.Object, _authorization.Object,
				Mock.Of<ILimitsService>(), groups.Object, _calls.Object, Mock.Of<IEventAggregator>(), customStates.Object, Mock.Of<IGeoService>(),
				_settings.Object, Mock.Of<IGeoLocationProvider>(), Mock.Of<INovuProvider>(), mapping.Object,
				Mock.Of<IUserDefinedFieldsService>(), Mock.Of<IUdfRenderingService>(), common.Object,
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IProtectedReadService>(), Mock.Of<IRecordsCutoverService>(), _scope.Object,
				_nearest.Object, _attribution.Object, rules.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private void SetMode(DestinationSuggestionModes mode) =>
			_settings.Setup(x => x.GetDestinationSuggestionConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new DestinationSuggestionConfig { Mode = (int)mode });

		private static DestinationSuggestions Suggestions(DestinationSuggestionModes mode = DestinationSuggestionModes.Suggest)
		{
			var requirements = new DestinationRequirements { PoiTypeIds = new List<int> { 10 }, RequiredCapabilityIds = new List<string> { "helipad" } };
			requirements.AppliedRules.Add(new DestinationRule { DestinationRuleId = "r1", Name = "Burns" });

			return new DestinationSuggestions
			{
				Mode = mode,
				Requirements = requirements,
				SuggestedPoiId = BurnCenter,
				Destinations = new List<NearestDestination>
				{
					new NearestDestination { Poi = new Poi { PoiId = BurnCenter, Name = "Burn Center" }, TravelSeconds = 600, Tier = DestinationTiers.Ready },
					new NearestDestination { Poi = new Poi { PoiId = ZuluGeneral, Name = "Zulu General" }, TravelSeconds = 420, Tier = DestinationTiers.Lacking,
						MeetsRequirements = false, MissingCapabilityIds = new List<string> { "helipad" } },
					new NearestDestination { Poi = new Poi { PoiId = AlphaClinic, Name = "Alpha Clinic" }, Status = PoiAvailabilityStatuses.Diversion, Tier = DestinationTiers.Lacking }
				}
			};
		}

		private async Task<string> HtmlAsync(int detailId, int? unitId = UnitId)
		{
			var result = await _controller.GetUnitStatusDestinationHtmlForDropdown(0, detailId, unitId, CancellationToken.None);
			return result.Should().BeOfType<ContentResult>().Which.Content;
		}

		private static string PoiValue(int poiId) => $"value='{(int)DestinationEntityTypes.Poi}:{poiId}'";

		private static int PositionOf(string html, int poiId) => html.IndexOf(PoiValue(poiId), StringComparison.Ordinal);

		private static int? SelectedPoiId(string html)
		{
			var match = Regex.Match(html, $"value='{(int)DestinationEntityTypes.Poi}:(\\d+)' selected='selected'");
			return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
		}

		private void VerifyNoLookup() =>
			_nearest.Verify(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);

		private static void ShouldBeTodaysList(string html)
		{
			html.Should().NotContain("Best for this unit");
			html.Should().NotContain("Suggested");
			PositionOf(html, AlphaClinic).Should().BeLessThan(PositionOf(html, BurnCenter));
			PositionOf(html, BurnCenter).Should().BeLessThan(PositionOf(html, ZuluGeneral));
			SelectedPoiId(html).Should().BeNull();
		}

		[Test]
		public async Task Without_a_unit_the_list_is_grouped_alphabetically_as_before()
		{
			ShouldBeTodaysList(await HtmlAsync(TransportingDetailId, unitId: null));
			VerifyNoLookup();
		}

		[Test]
		public async Task With_suggestions_off_one_unit_gets_the_list_as_before()
		{
			SetMode(DestinationSuggestionModes.Off);

			ShouldBeTodaysList(await HtmlAsync(TransportingDetailId));
			VerifyNoLookup();
		}

		[Test]
		public async Task A_status_without_destinations_never_looks_up()
		{
			var html = await HtmlAsync(RespondingDetailId);

			html.Should().NotContain(PoiValue(BurnCenter));
			VerifyNoLookup();
		}

		[Test]
		public async Task One_unit_gets_the_destinations_ranked_by_its_rules_and_its_working_call()
		{
			var html = await HtmlAsync(ToWaterSourceDetailId);

			_nearest.Verify(x => x.SuggestDestinationsAsync(It.Is<DestinationSuggestionRequest>(r =>
				r.DepartmentId == Dept && r.UnitId == UnitId && r.CallTypeId == 31 && r.CallType == "Burns" && r.CallPriority == 2 &&
				r.Latitude == 51.05 && r.Longitude == 3.72), It.IsAny<CancellationToken>()), Times.Once);

			var group = html.IndexOf("<optgroup label=\"Best for this unit\">", StringComparison.Ordinal);
			group.Should().BeGreaterThan(-1);
			group.Should().BeLessThan(html.IndexOf("<optgroup label=\"Water Source\">", StringComparison.Ordinal));
			PositionOf(html, BurnCenter).Should().BeLessThan(PositionOf(html, ZuluGeneral));
			PositionOf(html, ZuluGeneral).Should().BeLessThan(PositionOf(html, AlphaClinic));

			// The option text as the browser shows it (the markup encodes the separator).
			var text = WebUtility.HtmlDecode(html);
			text.Should().Contain("Burn Center (Suggested · 10 min)");
			text.Should().Contain("Zulu General (Missing: Helipad · 7 min)");
			text.Should().Contain("Alpha Clinic (Diversion)");

			// Each destination is offered once: the ranked hospitals leave their own group, the unranked water source stays below.
			Regex.Matches(html, Regex.Escape(PoiValue(BurnCenter))).Count.Should().Be(1);
			html.Should().NotContain("<optgroup label=\"Hospital\">");
			html.Should().Contain(PoiValue(Lake));
			PositionOf(html, Lake).Should().BeGreaterThan(PositionOf(html, AlphaClinic));

			// Not a transport status: nothing is chosen for the dispatcher.
			SelectedPoiId(html).Should().BeNull();
		}

		[TestCase(DestinationSuggestionModes.Suggest)]
		[TestCase(DestinationSuggestionModes.SuggestAndFill)]
		public async Task A_transport_status_preselects_the_working_calls_destination(DestinationSuggestionModes mode)
		{
			SetMode(mode);
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Suggestions(mode));
			_workingCall.DestinationPoiId = ZuluGeneral;

			SelectedPoiId(await HtmlAsync(TransportingDetailId)).Should().Be(ZuluGeneral);
		}

		[Test]
		public async Task A_transport_status_preselects_the_suggestion_only_in_suggest_and_fill()
		{
			SetMode(DestinationSuggestionModes.SuggestAndFill);
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Suggestions(DestinationSuggestionModes.SuggestAndFill));

			SelectedPoiId(await HtmlAsync(TransportingDetailId)).Should().Be(BurnCenter);
		}

		[Test]
		public async Task In_suggest_mode_a_transport_status_with_no_call_destination_preselects_nothing()
		{
			SelectedPoiId(await HtmlAsync(TransportingDetailId)).Should().BeNull();
		}

		[Test]
		public async Task A_transport_status_keeps_the_destination_the_unit_is_already_transporting_to()
		{
			SetMode(DestinationSuggestionModes.SuggestAndFill);
			_workingCall.DestinationPoiId = ZuluGeneral;
			_units.Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitId)).ReturnsAsync(new UnitState
			{
				UnitId = UnitId, State = TransportingDetailId, DestinationType = (int)DestinationEntityTypes.Poi, DestinationId = AlphaClinic, Timestamp = DateTime.UtcNow
			});

			SelectedPoiId(await HtmlAsync(TransportingDetailId)).Should().Be(AlphaClinic);
		}

		[Test]
		public async Task A_unit_the_member_cannot_see_gets_the_list_as_before()
		{
			_authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(UnitId, Me, Dept)).ReturnsAsync(false);

			ShouldBeTodaysList(await HtmlAsync(TransportingDetailId));
			VerifyNoLookup();
		}

		[Test]
		public async Task A_unit_of_another_department_gets_the_list_as_before()
		{
			_units.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = 99, Name = "Theirs" });

			ShouldBeTodaysList(await HtmlAsync(TransportingDetailId));
			VerifyNoLookup();
		}

		[Test]
		public async Task A_failed_lookup_gets_the_list_as_before()
		{
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("routing down"));

			ShouldBeTodaysList(await HtmlAsync(ToWaterSourceDetailId));
		}

		[Test]
		public async Task A_working_call_outside_the_members_scope_shapes_nothing()
		{
			_scope.Setup(x => x.FilterCallsForUserAsync(Dept, Me, It.IsAny<List<Call>>())).ReturnsAsync(new List<Call>());

			ShouldBeTodaysList(await HtmlAsync(TransportingDetailId));
			VerifyNoLookup();
		}

		[Test]
		public async Task The_units_recent_position_is_the_origin_when_the_member_may_see_it()
		{
			_authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, Me, Dept)).ReturnsAsync(true);
			_units.Setup(x => x.GetLatestUnitLocationForDepartmentAsync(Dept, UnitId))
				.ReturnsAsync(new UnitsLocation { UnitId = UnitId, DepartmentId = Dept, Latitude = 50.5m, Longitude = 4.5m, Timestamp = DateTime.UtcNow.AddMinutes(-2) });

			await HtmlAsync(ToWaterSourceDetailId);

			_nearest.Verify(x => x.SuggestDestinationsAsync(It.Is<DestinationSuggestionRequest>(r => r.Latitude == 50.5 && r.Longitude == 4.5),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_stale_unit_position_gives_way_to_the_working_calls_location()
		{
			_authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(UnitId, Me, Dept)).ReturnsAsync(true);
			_units.Setup(x => x.GetLatestUnitLocationForDepartmentAsync(Dept, UnitId))
				.ReturnsAsync(new UnitsLocation { UnitId = UnitId, DepartmentId = Dept, Latitude = 50.5m, Longitude = 4.5m, Timestamp = DateTime.UtcNow.AddHours(-3) });

			await HtmlAsync(ToWaterSourceDetailId);

			_nearest.Verify(x => x.SuggestDestinationsAsync(It.Is<DestinationSuggestionRequest>(r => r.Latitude == 51.05 && r.Longitude == 3.72),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void The_unit_id_is_optional_so_the_multi_unit_modal_keeps_working()
		{
			var parameter = typeof(UnitsController).GetMethod(nameof(UnitsController.GetUnitStatusDestinationHtmlForDropdown)).GetParameters()
				.Single(p => p.Name == "unitId");

			parameter.IsOptional.Should().BeTrue();
		}
	}
}
