using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Destination rules (Belgian EMS, 2026-11-16: suggest the nearest open emergency department; generalised so call types,
	/// priorities and the travelling unit's type say what the destination must offer, e.g. burns, helipads, drafting).
	/// </summary>
	[TestFixture]
	public class DestinationRuleResolverTests
	{
		private const int EdType = 10;
		private const int HospitalType = 11;
		private const int WaterSourceType = 20;
		private const int BurnsCallType = 3;
		private const int TraumaCallType = 4;
		private const int Helicopter = 7;
		private const int WaterTender = 8;
		private const int P1 = 101;

		private static readonly int[] DestinationTypes = { EdType, HospitalType, WaterSourceType };
		private static readonly string[] Capabilities = { "burn", "helipad", "trauma", "drafting", "cath" };

		private static DestinationRule Rule(string name, int? callType = null, int? priority = null, int? unitType = null, string types = null,
			string required = null, string preferred = null) => new DestinationRule
		{
			DestinationRuleId = name.ToLowerInvariant(),
			Name = name,
			CallTypeId = callType,
			CallPriority = priority,
			UnitTypeId = unitType,
			PoiTypeIds = types,
			RequiredCapabilityIds = required,
			PreferredCapabilityIds = preferred
		};

		private static readonly List<DestinationRule> Rules = new List<DestinationRule>
		{
			Rule("Default EMS", types: EdType.ToString()),
			Rule("Burns", callType: BurnsCallType, types: EdType + "," + HospitalType, required: "burn"),
			Rule("Major trauma", callType: TraumaCallType, priority: P1, required: "trauma", preferred: "cath"),
			Rule("Helicopter", unitType: Helicopter, types: EdType + "," + HospitalType, required: "helipad"),
			Rule("Water tender", unitType: WaterTender, types: WaterSourceType.ToString(), required: "drafting")
		};

		private static DestinationRequirements Resolve(int? callType = null, int? priority = null, int? unitType = null, IEnumerable<DestinationRule> rules = null) =>
			DestinationRuleResolver.Resolve(rules ?? Rules, new DestinationContext { CallTypeId = callType, CallPriority = priority, UnitTypeId = unitType },
				DestinationTypes, Capabilities);

		[Test]
		public void without_rules_any_destination_type_and_nothing_required()
		{
			var result = Resolve(BurnsCallType, rules: new List<DestinationRule>());

			result.PoiTypeIds.Should().BeNull();
			result.RequiredCapabilityIds.Should().BeEmpty();
			result.HasRequirements.Should().BeFalse();
		}

		[Test]
		public void a_rule_without_conditions_is_the_department_default()
		{
			var result = Resolve();

			result.AppliedRules.Select(x => x.Name).Should().Equal("Default EMS");
			result.PoiTypeIds.Should().Equal(EdType);
		}

		[Test]
		public void a_helicopter_on_a_burns_call_needs_a_burn_center_with_a_helipad()
		{
			var result = Resolve(BurnsCallType, unitType: Helicopter);

			result.RequiredCapabilityIds.Should().BeEquivalentTo("helipad", "burn");
			result.PoiTypeIds.Should().Equal(EdType);
			result.AppliedRules.Select(x => x.Name).Should().Equal("Helicopter", "Burns", "Default EMS");
			result.SetAsideRules.Should().BeEmpty();
		}

		[Test]
		public void a_water_tender_goes_to_a_water_source_and_the_call_rules_are_set_aside()
		{
			var result = Resolve(BurnsCallType, unitType: WaterTender);

			result.PoiTypeIds.Should().Equal(WaterSourceType);
			result.RequiredCapabilityIds.Should().Equal("drafting");
			result.SetAsideRules.Select(x => x.Rule.Name).Should().BeEquivalentTo("Burns", "Default EMS");
			result.SetAsideRules.Should().OnlyContain(x => x.Reason == DestinationRuleSetAsideReasons.TypeConflict);
		}

		[Test]
		public void unit_type_rules_do_not_apply_to_the_calls_own_destination()
		{
			Resolve(BurnsCallType).AppliedRules.Select(x => x.Name).Should().NotContain("Helicopter").And.NotContain("Water tender");
		}

		[Test]
		public void a_rule_with_two_conditions_needs_both()
		{
			Resolve(TraumaCallType, priority: P1).RequiredCapabilityIds.Should().Contain("trauma");
			Resolve(TraumaCallType, priority: P1 + 1).RequiredCapabilityIds.Should().NotContain("trauma");
			Resolve(priority: P1).RequiredCapabilityIds.Should().NotContain("trauma");
		}

		[Test]
		public void preferred_capabilities_never_repeat_required_ones()
		{
			var rules = new List<DestinationRule>
			{
				Rule("Cardiac", callType: TraumaCallType, preferred: "cath,helipad"),
				Rule("Helicopter", unitType: Helicopter, required: "helipad")
			};

			var result = Resolve(TraumaCallType, unitType: Helicopter, rules: rules);

			result.RequiredCapabilityIds.Should().Equal("helipad");
			result.PreferredCapabilityIds.Should().Equal("cath");
		}

		[Test]
		public void deleted_capabilities_are_ignored_and_a_rule_whose_types_were_all_deleted_is_set_aside()
		{
			var rules = new List<DestinationRule>
			{
				Rule("Old", callType: BurnsCallType, types: "999", required: "burn"),
				Rule("Gone capability", callType: BurnsCallType, required: "deleted,burn")
			};

			var result = Resolve(BurnsCallType, rules: rules);

			result.SetAsideRules.Should().ContainSingle(x => x.Rule.Name == "Old" && x.Reason == DestinationRuleSetAsideReasons.TypesRemoved);
			result.PoiTypeIds.Should().BeNull();
			result.RequiredCapabilityIds.Should().Equal("burn");
		}

		[Test]
		public void more_specific_rules_narrow_first()
		{
			var rules = new List<DestinationRule>
			{
				Rule("Any hospital", types: EdType + "," + HospitalType),
				Rule("Burns", callType: BurnsCallType, types: HospitalType.ToString(), required: "burn")
			};

			var result = Resolve(BurnsCallType, rules: rules);

			result.PoiTypeIds.Should().Equal(HospitalType);
			result.AppliedRules.Select(x => x.Name).Should().Equal("Burns", "Any hospital");
		}
	}

	/// <summary>Ranking suggested destinations: tiers, then drive time, with the preferred-capability near-tie window.</summary>
	[TestFixture]
	public class DestinationSuggestionRankingTests
	{
		private static NearestDestination Row(int id, double seconds, int tier = DestinationTiers.Ready, int preferred = 0, double meters = 1000) => new NearestDestination
		{
			Poi = new Poi { PoiId = id },
			TravelSeconds = seconds,
			DistanceMeters = meters,
			Tier = tier,
			PreferredCapabilityIds = Enumerable.Range(0, preferred).Select(x => "p" + x).ToList()
		};

		[Test]
		public void tiers_come_first_then_drive_time()
		{
			var ranked = NearestDestinationService.RankSuggestions(new[]
			{
				Row(1, 100, DestinationTiers.Closed),
				Row(2, 900),
				Row(3, 200, DestinationTiers.Diversion),
				Row(4, 300, DestinationTiers.Lacking),
				Row(5, 600)
			}, 0);

			ranked.Select(x => x.Poi.PoiId).Should().Equal(5, 2, 3, 4, 1);
		}

		[Test]
		public void a_preferred_destination_within_the_window_wins_over_a_slightly_closer_one()
		{
			var ranked = NearestDestinationService.RankSuggestions(new[] { Row(1, 600), Row(2, 700, preferred: 1), Row(3, 1500, preferred: 2) }, 180);

			ranked.Select(x => x.Poi.PoiId).Should().Equal(2, 1, 3);
		}

		[Test]
		public void a_preferred_destination_beyond_the_window_never_wins()
		{
			var ranked = NearestDestinationService.RankSuggestions(new[] { Row(1, 600), Row(2, 900, preferred: 1) }, 180);

			ranked.Select(x => x.Poi.PoiId).Should().Equal(1, 2);
		}

		[Test]
		public void unrouted_destinations_follow_the_routed_ones_by_distance()
		{
			var ranked = NearestDestinationService.RankSuggestions(new[] { Row(1, -1, meters: 500), Row(2, 2000), Row(3, -1, meters: 300) }, 180);

			ranked.Select(x => x.Poi.PoiId).Should().Equal(2, 3, 1);
		}
	}

	/// <summary>"The nearest open emergency department", from the department's rules and the incident location.</summary>
	[TestFixture]
	public class DestinationSuggestionServiceTests
	{
		private const int DeptId = 7;
		private const double OriginLat = 50.80;
		private const double OriginLon = 3.80;

		private Mock<IMappingService> _mapping;
		private Mock<IGeoService> _geo;
		private Mock<IUnitsService> _units;
		private Mock<IDestinationRulesService> _rules;
		private Mock<IDepartmentSettingsService> _settings;
		private NearestDestinationService _service;
		private PoiType _eds;
		private PoiType _waterSources;
		private DestinationSuggestionConfig _config;
		private List<DestinationRule> _ruleList;
		private DestinationContext _context;

		[SetUp]
		public void SetUp()
		{
			_mapping = new Mock<IMappingService>();
			_geo = new Mock<IGeoService>();
			_units = new Mock<IUnitsService>();
			_rules = new Mock<IDestinationRulesService>();
			_settings = new Mock<IDepartmentSettingsService>();

			_eds = new PoiType { PoiTypeId = 10, DepartmentId = DeptId, Name = "Erkende Spoedgevallendiensten", IsDestination = true };
			_waterSources = new PoiType { PoiTypeId = 20, DepartmentId = DeptId, Name = "Water sources", IsDestination = true };
			_eds.Pois = new List<Poi>
			{
				Poi(1, "AZ Alma Eeklo", 0.01, _eds),
				Poi(2, "AZ Delta Roeselare", 0.02, _eds, "burn"),
				Poi(3, "UZ Gent", 0.05, _eds, "burn", "helipad")
			};
			_waterSources.Pois = new List<Poi> { Poi(20, "Pond", 0.005, _waterSources, "drafting") };
			var shelters = new PoiType { PoiTypeId = 30, DepartmentId = DeptId, Name = "Shelters", IsDestination = false, Pois = new List<Poi> { Poi(30, "Shelter", 0.001, null) } };

			_config = new DestinationSuggestionConfig { Mode = (int)DestinationSuggestionModes.SuggestAndFill };
			_ruleList = new List<DestinationRule> { new DestinationRule { DestinationRuleId = "default", Name = "Default EMS", PoiTypeIds = "10" } };
			_context = new DestinationContext();

			_settings.Setup(x => x.GetDestinationSuggestionConfigAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(() => _config);
			_rules.Setup(x => x.GetRulesForDepartmentAsync(DeptId)).ReturnsAsync(() => _ruleList);
			_rules.Setup(x => x.BuildContextAsync(DeptId, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>())).ReturnsAsync(() => _context);
			_mapping.Setup(x => x.GetPOITypesForDepartmentAsync(DeptId)).ReturnsAsync(() => new List<PoiType> { _eds, _waterSources, shelters });
			_mapping.Setup(x => x.GetPoiCapabilitiesForDepartmentAsync(DeptId)).ReturnsAsync(new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = "burn", DepartmentId = DeptId, Name = "Burn Center" },
				new PoiCapability { PoiCapabilityId = "helipad", DepartmentId = DeptId, Name = "Helipad" },
				new PoiCapability { PoiCapabilityId = "drafting", DepartmentId = DeptId, Name = "Drafting" }
			});
			_mapping.Setup(x => x.PopulatePoiDetailsAsync(DeptId, It.IsAny<IEnumerable<Poi>>())).Returns(Task.CompletedTask);
			// Routed seconds and road metres grow with the latitude offset.
			_geo.Setup(x => x.GetTravelEstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TravelLookup>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string _, string destination, TravelLookup __, CancellationToken ___) =>
				{
					var lat = double.Parse(destination.Split(',')[0], CultureInfo.InvariantCulture);
					return new TravelEstimate { Seconds = Math.Round((lat - OriginLat) * 100000), DistanceMeters = Math.Round((lat - OriginLat) * 150000), Source = EtaSources.Road };
				});

			_service = new NearestDestinationService(_mapping.Object, _geo.Object, _units.Object, _rules.Object, _settings.Object);
		}

		private static Poi Poi(int id, string name, double latitudeOffset, PoiType type, params string[] capabilities) => new Poi
		{
			PoiId = id,
			PoiTypeId = type?.PoiTypeId ?? 30,
			Name = name,
			Latitude = OriginLat + latitudeOffset,
			Longitude = OriginLon,
			CapabilityIds = capabilities.ToList(),
			Capabilities = capabilities.ToList()
		};

		private Task<DestinationSuggestions> Suggest(int? unitId = null) =>
			_service.SuggestDestinationsAsync(new DestinationSuggestionRequest { DepartmentId = DeptId, Latitude = OriginLat, Longitude = OriginLon, CallType = "Burns", CallPriority = 1, UnitId = unitId });

		[Test]
		public async Task off_routes_nothing_and_returns_nothing()
		{
			_config = new DestinationSuggestionConfig();

			var result = await Suggest();

			result.Mode.Should().Be(DestinationSuggestionModes.Off);
			result.Destinations.Should().BeEmpty();
			result.SuggestedPoiId.Should().BeNull();
			_geo.Verify(x => x.GetTravelEstimateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TravelLookup>(), It.IsAny<CancellationToken>()), Times.Never);
			_rules.Verify(x => x.GetRulesForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task the_default_rule_suggests_the_nearest_emergency_department_with_drive_time_and_road_distance()
		{
			var result = await Suggest();

			result.SuggestedPoiId.Should().Be(1);
			result.Destinations.Select(x => x.Poi.PoiId).Should().Equal(1, 2, 3);
			result.Destinations[0].TravelSeconds.Should().Be(1000);
			result.Destinations[0].TravelDistanceMeters.Should().Be(1500);
			result.Destinations.Should().NotContain(x => x.Poi.PoiId == 20 || x.Poi.PoiId == 30, "only the rule's destination types, never a non-destination type");
		}

		[Test]
		public async Task a_required_capability_skips_closer_destinations_without_it()
		{
			_ruleList.Add(new DestinationRule { DestinationRuleId = "burns", Name = "Burns", CallTypeId = 3, RequiredCapabilityIds = "burn" });
			_context = new DestinationContext { CallTypeId = 3 };

			var result = await Suggest();

			result.SuggestedPoiId.Should().Be(2);
			result.Requirements.RequiredCapabilityIds.Should().Equal("burn");
			var alma = result.Destinations.Single(x => x.Poi.PoiId == 1);
			alma.MeetsRequirements.Should().BeFalse();
			alma.MissingCapabilityIds.Should().Equal("burn");
			alma.Tier.Should().Be(DestinationTiers.Lacking);
			result.Destinations.Last().Poi.PoiId.Should().Be(1);
		}

		[Test]
		public async Task a_closed_or_diverting_destination_gives_way()
		{
			_eds.Pois.ElementAt(0).Availability = new PoiAvailability { PoiId = 1, Status = (int)PoiAvailabilityStatuses.Closed, SetOn = DateTime.UtcNow };
			_eds.Pois.ElementAt(1).Availability = new PoiAvailability { PoiId = 2, Status = (int)PoiAvailabilityStatuses.Diversion, SetOn = DateTime.UtcNow };

			var result = await Suggest();

			result.SuggestedPoiId.Should().Be(3);
			result.Destinations.Select(x => x.Poi.PoiId).Should().Equal(3, 2, 1);
		}

		[Test]
		public async Task a_required_capability_reported_down_does_not_count()
		{
			_ruleList.Add(new DestinationRule { DestinationRuleId = "burns", Name = "Burns", CallTypeId = 3, RequiredCapabilityIds = "burn" });
			_context = new DestinationContext { CallTypeId = 3 };
			_eds.Pois.ElementAt(1).Availability = new PoiAvailability { PoiId = 2, Status = (int)PoiAvailabilityStatuses.Limited, UnavailableCapabilityIds = "burn", SetOn = DateTime.UtcNow };

			var result = await Suggest();

			result.SuggestedPoiId.Should().Be(3);
			result.Destinations.Single(x => x.Poi.PoiId == 2).CanProvide.Should().BeFalse();
		}

		[Test]
		public async Task when_nothing_meets_the_requirements_nothing_is_suggested_and_the_nearest_are_listed()
		{
			_ruleList.Add(new DestinationRule { DestinationRuleId = "trauma", Name = "Trauma", CallTypeId = 3, RequiredCapabilityIds = "helipad,drafting" });
			_context = new DestinationContext { CallTypeId = 3 };

			var result = await Suggest();

			result.SuggestedPoiId.Should().BeNull();
			result.Destinations.Should().HaveCount(3).And.OnlyContain(x => !x.MeetsRequirements);
		}

		[Test]
		public async Task a_water_tender_is_sent_to_a_water_source_with_drafting()
		{
			_ruleList.Add(new DestinationRule { DestinationRuleId = "tender", Name = "Water tender", UnitTypeId = 8, PoiTypeIds = "20", RequiredCapabilityIds = "drafting" });
			_context = new DestinationContext { UnitTypeId = 8 };

			var result = await Suggest(unitId: 44);

			result.SuggestedPoiId.Should().Be(20);
			result.Destinations.Select(x => x.Poi.PoiId).Should().Equal(20);
			result.Requirements.SetAsideRules.Select(x => x.Rule.Name).Should().Equal("Default EMS");
		}

		[Test]
		public async Task a_preferred_capability_wins_a_near_tie()
		{
			// Alma 1000 s (no burn unit), Delta 2000 s and UZ 5000 s (both have one).
			_ruleList[0].PreferredCapabilityIds = "burn";

			_config.PreferredWithinMinutes = 1;
			var result = await Suggest();
			result.SuggestedPoiId.Should().Be(1, "Delta is 1000 s slower than Alma, beyond a one-minute window");

			_config.PreferredWithinMinutes = 20;
			result = await Suggest();
			result.SuggestedPoiId.Should().Be(2, "Delta is within twenty minutes of Alma and offers the preferred burn unit");
			result.Destinations.Select(x => x.Poi.PoiId).Should().Equal(2, 1, 3);
			result.Destinations[0].PreferredCapabilityIds.Should().Equal("burn");
		}
	}

	/// <summary>Saving rules: every id is the department's own.</summary>
	[TestFixture]
	public class DestinationRulesServiceTests
	{
		private const int DeptId = 7;

		private Mock<IDestinationRulesRepository> _repository;
		private Mock<IMappingService> _mapping;
		private Mock<ICallsService> _calls;
		private Mock<IUnitsService> _units;
		private DestinationRulesService _service;
		private DestinationRule _saved;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IDestinationRulesRepository>();
			_mapping = new Mock<IMappingService>();
			_calls = new Mock<ICallsService>();
			_units = new Mock<IUnitsService>();

			_calls.Setup(x => x.GetCallTypesForDepartmentAsync(DeptId)).ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 3, DepartmentId = DeptId, Type = "Burns" } });
			_calls.Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority> { new DepartmentCallPriority { DepartmentCallPriorityId = 101, DepartmentId = DeptId, Name = "P1" } });
			_units.Setup(x => x.GetUnitTypesForDepartmentAsync(DeptId)).ReturnsAsync(new List<UnitType> { new UnitType { UnitTypeId = 7, DepartmentId = DeptId, Type = "Helicopter" } });
			_mapping.Setup(x => x.GetPOITypesForDepartmentAsync(DeptId)).ReturnsAsync(new List<PoiType>
			{
				new PoiType { PoiTypeId = 10, DepartmentId = DeptId, Name = "ED", IsDestination = true },
				new PoiType { PoiTypeId = 30, DepartmentId = DeptId, Name = "Hydrant", IsDestination = false }
			});
			_mapping.Setup(x => x.GetPoiCapabilitiesForDepartmentAsync(DeptId)).ReturnsAsync(new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = "burn", DepartmentId = DeptId, Name = "Burn Center" },
				new PoiCapability { PoiCapabilityId = "helipad", DepartmentId = DeptId, Name = "Helipad" }
			});
			_repository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DestinationRule>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DestinationRule rule, CancellationToken _, bool __) => _saved = rule);

			_service = new DestinationRulesService(_repository.Object, _mapping.Object, _calls.Object, _units.Object);
		}

		private Task<DestinationRuleSaveResult> Save(DestinationRule rule) => _service.SaveRuleAsync(DeptId, rule, "admin");

		[Test]
		public async Task a_valid_rule_is_stored_with_tidy_lists()
		{
			var result = await Save(new DestinationRule { Name = "  Burns  ", CallTypeId = 3, CallPriority = 101, UnitTypeId = 7, PoiTypeIds = "10,10", RequiredCapabilityIds = "burn, burn", PreferredCapabilityIds = "burn,helipad" });

			result.Success.Should().BeTrue();
			_saved.DepartmentId.Should().Be(DeptId);
			_saved.Name.Should().Be("Burns");
			_saved.PoiTypeIds.Should().Be("10");
			_saved.RequiredCapabilityIds.Should().Be("burn");
			_saved.PreferredCapabilityIds.Should().Be("helipad");
			_saved.CreatedByUserId.Should().Be("admin");
		}

		[TestCase(99, null, null, null, null, DestinationRuleSaveErrors.UnknownCallType)]
		[TestCase(null, 5, null, null, null, DestinationRuleSaveErrors.UnknownPriority)]
		[TestCase(null, null, 99, null, null, DestinationRuleSaveErrors.UnknownUnitType)]
		[TestCase(null, null, null, "30", null, DestinationRuleSaveErrors.UnknownDestinationType)]
		[TestCase(null, null, null, null, "other-dept", DestinationRuleSaveErrors.UnknownCapability)]
		[TestCase(null, null, null, null, null, DestinationRuleSaveErrors.NothingRequired)]
		public async Task ids_outside_the_department_are_refused(int? callType, int? priority, int? unitType, string types, string required, DestinationRuleSaveErrors expected)
		{
			var result = await Save(new DestinationRule { Name = "Rule", CallTypeId = callType, CallPriority = priority, UnitTypeId = unitType, PoiTypeIds = types, RequiredCapabilityIds = required });

			result.Error.Should().Be(expected);
			_repository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DestinationRule>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task another_departments_rule_is_not_found()
		{
			_repository.Setup(x => x.GetByIdAsync("theirs")).ReturnsAsync(new DestinationRule { DestinationRuleId = "theirs", DepartmentId = 99, Name = "Theirs" });

			(await Save(new DestinationRule { DestinationRuleId = "theirs", Name = "Mine now", RequiredCapabilityIds = "burn" })).Error.Should().Be(DestinationRuleSaveErrors.NotFound);
			(await _service.DeleteRuleAsync(DeptId, "theirs")).Should().BeFalse();
		}

		[Test]
		public async Task the_context_matches_the_call_type_by_name_and_the_unit_by_its_type()
		{
			_units.Setup(x => x.GetUnitByIdAsync(44)).ReturnsAsync(new Unit { UnitId = 44, DepartmentId = DeptId, Type = " helicopter " });
			_units.Setup(x => x.GetUnitByIdAsync(45)).ReturnsAsync(new Unit { UnitId = 45, DepartmentId = 99, Type = "Helicopter" });

			var context = await _service.BuildContextAsync(DeptId, null, "BURNS", 101, 44);
			context.CallTypeId.Should().Be(3);
			context.CallPriority.Should().Be(101);
			context.UnitTypeId.Should().Be(7);

			(await _service.BuildContextAsync(DeptId, null, "Unknown", null, 45)).Should().BeEquivalentTo(new DestinationContext());
		}
	}

	/// <summary>The v4 GetSuggestedDestinations requirements shape the apps read (Omni shows applied and set-aside rules).</summary>
	[TestFixture]
	public class DestinationRequirementsV4ConversionTests
	{
		[Test]
		public void Applied_and_set_aside_rules_keep_their_ids_names_and_reason()
		{
			var tender = new DestinationRule { DestinationRuleId = "tender", Name = "Water tender", UnitTypeId = 8, PoiTypeIds = "20", RequiredCapabilityIds = "drafting" };
			var burns = new DestinationRule { DestinationRuleId = "burns", Name = "Burns", CallTypeId = 3, PoiTypeIds = "10", RequiredCapabilityIds = "burn" };

			var requirements = DestinationRuleResolver.Resolve(new[] { burns, tender },
				new DestinationContext { CallTypeId = 3, UnitTypeId = 8 }, new[] { 10, 20 }, new[] { "burn", "drafting" });

			var data = Resgrid.Web.Services.Controllers.v4.MappingController.ConvertDestinationRequirements(requirements);

			data.PoiTypeIds.Should().Equal(20);
			data.RequiredCapabilityIds.Should().Equal("drafting");
			data.PreferredCapabilityIds.Should().BeEmpty();
			data.Rules.Select(x => x.DestinationRuleId + "|" + x.Name + "|" + x.Reason).Should().Equal("tender|Water tender|");
			data.SetAsideRules.Select(x => x.DestinationRuleId + "|" + x.Name + "|" + x.Reason).Should().Equal("burns|Burns|TypeConflict");
		}

		[Test]
		public void No_rules_means_any_destination_type_and_empty_lists()
		{
			var data = Resgrid.Web.Services.Controllers.v4.MappingController.ConvertDestinationRequirements(DestinationRequirements.None);

			data.PoiTypeIds.Should().BeEmpty();
			data.RequiredCapabilityIds.Should().BeEmpty();
			data.Rules.Should().BeEmpty();
			data.SetAsideRules.Should().BeEmpty();

			Resgrid.Web.Services.Controllers.v4.MappingController.ConvertDestinationRequirements(null).Rules.Should().BeEmpty();
		}
	}
}
