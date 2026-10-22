using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Map visualizations (Mapping → Visualizations, M0288): definition normalization and windows, the built-in library,
	/// the pure layer builder (categories, bands, folding, summary, tracks), the service's save rules and visibility, and
	/// the MVC surface (policy, anti-forgery, localized labels for every key).
	/// </summary>
	[TestFixture]
	public class MapVisualizationTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "user-1";
		private const string OtherUserId = "user-2";

		#region Validator

		[Test]
		public void Normalize_replaces_unknown_keys_and_keeps_known_ones()
		{
			var definition = new MapVisualizationDefinition
			{
				Layers =
				{
					new MapVisualizationLayer
					{
						Id = "a", Source = "bogus", Display = "pie", ColorBy = "zodiac", Metric = "vibes", Aggregate = "mode",
						Style = new MapVisualizationLayerStyle { Palette = "rainbow" }
					},
					new MapVisualizationLayer
					{
						Id = "b", Source = MapVisualizationKeys.SourceUnitResponses, Display = MapVisualizationKeys.DisplayPoints,
						ColorBy = MapVisualizationKeys.ColorByUnitType, Metric = MapVisualizationKeys.MetricResponseMinutes,
						Aggregate = MapVisualizationKeys.AggregateP90
					}
				}
			};

			var result = MapVisualizationDefinitionValidator.Normalize(definition);

			result.Layers[0].Source.Should().Be(MapVisualizationKeys.SourceCalls);
			result.Layers[0].Display.Should().Be(MapVisualizationKeys.DisplayHeat);
			result.Layers[0].ColorBy.Should().Be(MapVisualizationKeys.ColorByNone);
			result.Layers[0].Metric.Should().Be(MapVisualizationKeys.MetricCount);
			result.Layers[0].Aggregate.Should().Be(MapVisualizationKeys.AggregateCount);
			result.Layers[0].Style.Palette.Should().Be(MapVisualizationKeys.PaletteHeat);

			result.Layers[1].ColorBy.Should().Be(MapVisualizationKeys.ColorByUnitType);
			result.Layers[1].Metric.Should().Be(MapVisualizationKeys.MetricResponseMinutes);
			result.Layers[1].Aggregate.Should().Be(MapVisualizationKeys.AggregateP90);
		}

		[Test]
		public void Normalize_drops_a_category_on_a_heat_layer_and_a_color_the_source_does_not_have()
		{
			var definition = new MapVisualizationDefinition
			{
				Layers =
				{
					new MapVisualizationLayer { Id = "a", Source = MapVisualizationKeys.SourceCalls, Display = MapVisualizationKeys.DisplayHeat, ColorBy = MapVisualizationKeys.ColorByCallType },
					new MapVisualizationLayer { Id = "b", Source = MapVisualizationKeys.SourceUnitTracks, Display = MapVisualizationKeys.DisplayPoints, ColorBy = MapVisualizationKeys.ColorByCallType }
				}
			};

			var result = MapVisualizationDefinitionValidator.Normalize(definition);

			result.Layers[0].ColorBy.Should().Be(MapVisualizationKeys.ColorByNone, "a heat surface has one color ramp");
			result.Layers[1].Display.Should().Be(MapVisualizationKeys.DisplayTracks, "track sources draw as paths, heat or grid");
			result.Layers[1].ColorBy.Should().Be(MapVisualizationKeys.ColorByNone, "a track has no call type");
		}

		[Test]
		public void Normalize_turns_a_count_aggregate_on_a_real_metric_into_a_median()
		{
			var definition = new MapVisualizationDefinition
			{
				Layers = { new MapVisualizationLayer { Id = "a", Display = MapVisualizationKeys.DisplayHexbins, Metric = MapVisualizationKeys.MetricDurationMinutes, Aggregate = MapVisualizationKeys.AggregateCount } }
			};

			MapVisualizationDefinitionValidator.Normalize(definition).Layers[0].Aggregate.Should().Be(MapVisualizationKeys.AggregateMedian);
		}

		[Test]
		public void Normalize_clamps_numbers_caps_lists_and_makes_layer_ids_unique()
		{
			var definition = new MapVisualizationDefinition
			{
				Filters = new MapVisualizationFilters
				{
					LastDays = 99999,
					Hours = new List<int> { -1, 5, 5, 24, 23 },
					DaysOfWeek = new List<int> { 0, 7, 6 },
					Outcomes = new List<int> { 1, 99 },
					CallId = -4,
					MinCallsAtLocation = -2
				},
				Options = new MapVisualizationOptions
				{
					DistanceUnit = "furlongs",
					StationRings = new List<double> { 5, 1, double.NaN, 2, 3, 4, 0 }
				}
			};

			for (var i = 0; i < 9; i++)
				definition.Layers.Add(new MapVisualizationLayer { Id = "same", Style = new MapVisualizationLayerStyle { Opacity = 7, Radius = 1, CellSizeMeters = 5 } });

			var result = MapVisualizationDefinitionValidator.Normalize(definition);

			result.Filters.LastDays.Should().Be(MapVisualizationLimits.MaxWindowDays);
			result.Filters.Hours.Should().Equal(5, 23);
			result.Filters.DaysOfWeek.Should().Equal(0, 6);
			result.Filters.Outcomes.Should().Equal(1);
			result.Filters.CallId.Should().BeNull();
			result.Filters.MinCallsAtLocation.Should().Be(0);
			result.Options.DistanceUnit.Should().Be(MapVisualizationKeys.DistanceKilometers);
			result.Options.StationRings.Should().Equal(1, 2, 3, 4);
			result.Layers.Should().HaveCount(MapVisualizationLimits.MaxLayers);
			result.Layers.Select(l => l.Id).Should().OnlyHaveUniqueItems();
			result.Layers[0].Style.Opacity.Should().Be(1);
			result.Layers[0].Style.Radius.Should().Be(5);
			result.Layers[0].Style.CellSizeMeters.Should().Be(100);
		}

		[Test]
		public void Normalize_of_nothing_is_a_usable_definition()
		{
			var result = MapVisualizationDefinitionValidator.Normalize(null);

			result.Layers.Should().ContainSingle();
			result.Filters.LastDays.Should().Be(MapVisualizationLimits.DefaultWindowDays);
		}

		[Test]
		public void ResolveWindow_rolls_a_relative_window_and_covers_whole_local_days_for_a_fixed_one()
		{
			var now = new DateTime(2026, 10, 22, 15, 0, 0, DateTimeKind.Utc);

			var rolling = MapVisualizationDefinitionValidator.ResolveWindow(new MapVisualizationFilters { LastDays = 30 }, now, d => d);
			rolling.StartUtc.Should().Be(now.AddDays(-30));
			rolling.EndUtc.Should().Be(now);

			var fixedWindow = MapVisualizationDefinitionValidator.ResolveWindow(
				new MapVisualizationFilters { LastDays = null, Start = new DateTime(2026, 1, 1, 13, 0, 0), End = new DateTime(2026, 1, 31) },
				now, local => local.AddHours(5));
			fixedWindow.StartUtc.Should().Be(new DateTime(2026, 1, 1, 5, 0, 0), "the start is the local midnight of the first day");
			fixedWindow.EndUtc.Should().Be(new DateTime(2026, 2, 1, 5, 0, 0), "the end day is included whole");
		}

		[Test]
		public void A_saved_definition_round_trips_and_a_corrupt_one_still_opens()
		{
			var visualization = new MapVisualization
			{
				Definition = MapVisualizationLibrary.Find("response-time-by-area").CreateDefinition()
			};

			visualization.Definition.Layers[0].Aggregate.Should().Be(MapVisualizationKeys.AggregateP90);

			visualization.DefinitionJson = "{not json";
			visualization.Definition.Layers.Should().ContainSingle("a broken row must not take the page down");
		}

		#endregion Validator

		#region Library

		[Test]
		public void Library_keys_are_unique_and_every_template_is_already_normalized()
		{
			MapVisualizationLibrary.All.Select(t => t.Key).Should().OnlyHaveUniqueItems();

			foreach (var template in MapVisualizationLibrary.All)
			{
				var definition = template.CreateDefinition();
				var before = JsonConvert.SerializeObject(definition);
				var after = JsonConvert.SerializeObject(MapVisualizationDefinitionValidator.Normalize(template.CreateDefinition()));

				after.Should().Be(before, $"template '{template.Key}' should only use keys and values the validator keeps");
				template.CreateDefinition().Should().NotBeSameAs(definition, "each call hands out a fresh copy");
			}
		}

		[Test]
		public void Library_lists_the_operating_profiles_recommendations_first_and_never_hides_one()
		{
			var ems = MapVisualizationLibrary.ForOperatingProfile(new Resgrid.Model.AdminAssist.DepartmentOperatingProfile { Archetypes = new List<string> { "ems" } }).ToList();
			var firstGeneral = ems.FindIndex(t => t.RecommendedFor.Count == 0 && !t.RecommendedForVolunteers);
			var lastRecommended = ems.FindLastIndex(t => t.RecommendedFor.Contains("ems"));

			lastRecommended.Should().BeLessThan(firstGeneral);
			ems.Should().HaveCount(MapVisualizationLibrary.All.Count);
			MapVisualizationLibrary.ForOperatingProfile(null).Should().HaveCount(MapVisualizationLibrary.All.Count);

			var volunteers = new Resgrid.Model.AdminAssist.DepartmentOperatingProfile { WorkforceMix = "volunteer" };
			MapVisualizationLibrary.Find("responder-origins").IsRecommendedFor(volunteers).Should().BeTrue();
			MapVisualizationLibrary.Find("call-hot-zones").IsRecommendedFor(volunteers).Should().BeFalse("general templates are not flagged as recommended");
		}

		[Test]
		public void Library_archetypes_are_operating_profile_codes()
		{
			MapVisualizationLibrary.All.SelectMany(t => t.RecommendedFor)
				.Should().OnlyContain(code => Resgrid.Model.AdminAssist.DepartmentOperatingProfile.ArchetypeCodes.Contains(code));
		}

		[Test]
		public void Every_template_category_and_key_has_english_text()
		{
			var english = EnglishStrings();

			foreach (var template in MapVisualizationLibrary.All)
			{
				var key = template.Key.Replace('-', '_');
				english.Should().ContainKey($"Library_{key}_Name");
				english.Should().ContainKey($"Library_{key}_Description");
				english.Should().ContainKey($"Category_{template.Category}");
			}

			foreach (var source in MapVisualizationKeys.Sources)
			{
				english.Should().ContainKey($"Source_{source}");
				english.Should().ContainKey($"SourceHint_{source}");
			}

			foreach (var key in MapVisualizationKeys.Displays) english.Should().ContainKey($"Display_{key}");
			foreach (var key in MapVisualizationKeys.ColorBys) english.Should().ContainKey($"ColorBy_{key}");
			foreach (var key in MapVisualizationKeys.Metrics) english.Should().ContainKey($"Metric_{key}");
			foreach (var key in MapVisualizationKeys.Aggregates) english.Should().ContainKey($"Aggregate_{key}");
			foreach (var key in MapVisualizationKeys.Palettes) english.Should().ContainKey($"Palette_{key}");
			foreach (var state in Enum.GetValues(typeof(CallStates)).Cast<int>()) english.Should().ContainKey($"Outcome_{state}");
			foreach (var state in Enum.GetValues(typeof(UnitStateTypes)).Cast<int>()) english.Should().ContainKey($"UnitStatus_{state}");
			foreach (var state in Enum.GetValues(typeof(ActionTypes)).Cast<int>()) english.Should().ContainKey($"PersonnelStatus_{state}");
			foreach (var notice in typeof(MapVisualizationNotices).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)))
				english.Should().ContainKey($"Notice_{notice}");
			foreach (var error in Enum.GetNames(typeof(MapVisualizationSaveErrors)).Where(e => e != "None"))
				english.Should().ContainKey($"SaveError{error}");
		}

		private static Dictionary<string, string> EnglishStrings()
		{
			var manager = new ResourceManager(typeof(Resgrid.Localization.Areas.User.Mapping.MapVisualizations));
			return manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true)
				.Cast<DictionaryEntry>()
				.ToDictionary(e => (string)e.Key, e => e.Value as string);
		}

		#endregion Library

		#region Builder

		private static MapVisualizationContext Context(MapVisualizationDefinition definition, params MapVisualizationCallFact[] calls) => new MapVisualizationContext
		{
			Definition = MapVisualizationDefinitionValidator.Normalize(definition),
			Calls = calls.ToList()
		};

		private static MapVisualizationDefinition OneLayer(string source, string display, string colorBy = MapVisualizationKeys.ColorByNone, string metric = MapVisualizationKeys.MetricCount) =>
			new MapVisualizationDefinition { Layers = { new MapVisualizationLayer { Id = "l", Source = source, Display = display, ColorBy = colorBy, Metric = metric } } };

		private static MapVisualizationCallFact Call(int id, string type = "Fire", double? lat = 40, double? lon = -105, DateTime? local = null, double? response = null) =>
			new MapVisualizationCallFact
			{
				CallId = id, Type = type, Latitude = lat, Longitude = lon, LoggedOnLocal = local ?? new DateTime(2026, 10, 19, 14, 0, 0),
				ResponseMinutes = response, State = (int)CallStates.Closed
			};

		[Test]
		public void Calls_without_a_location_are_left_off_the_map_but_counted_in_the_summary()
		{
			var context = Context(OneLayer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints), Call(1), Call(2, lat: null, lon: null));

			var result = MapVisualizationBuilder.Build(context);

			result.Layers[0].Points.Should().ContainSingle();
			result.Layers[0].Points[0][4].Should().Be(1);
			result.Layers[0].Points[0][2].Should().Be(-1, "the layer is not colored by a category");
			result.Summary.Calls.Should().Be(2);
			result.Summary.CallsWithLocation.Should().Be(1);
		}

		[Test]
		public void Named_categories_beyond_twelve_fold_into_other()
		{
			var calls = Enumerable.Range(1, 20).Select(i => Call(i, type: $"Type {i:00}")).ToList();
			calls.Add(Call(100, type: "Type 01"));
			var context = Context(OneLayer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByCallType), calls.ToArray());

			var layer = MapVisualizationBuilder.Build(context).Layers[0];

			layer.Categories.Should().HaveCount(MapVisualizationBuilder.MaxCategories);
			layer.Categories[0].Label.Should().Be("Type 01", "the busiest type comes first");
			layer.Categories[0].Count.Should().Be(2);
			layer.Categories.Last().Key.Should().Be(MapVisualizationBuilder.OtherKey);
			layer.Categories.Last().Count.Should().Be(9, "20 types, the 11 busiest kept");
			layer.Points.Should().OnlyContain(p => p[2] >= 0 && p[2] < layer.Categories.Count);
			layer.Categories.Select(c => c.Color).Should().OnlyHaveUniqueItems();
		}

		[Test]
		public void Response_bands_follow_the_definitions_bounds_and_put_missing_times_last()
		{
			var definition = OneLayer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByResponseBand);
			definition.Options.MinuteBands = new List<double> { 4, 8 };
			var context = Context(definition, Call(1, response: 3), Call(2, response: 4), Call(3, response: 9), Call(4));

			var layer = MapVisualizationBuilder.Build(context).Layers[0];

			layer.Categories.Select(c => c.Key).Should().Equal("rb:0", "rb:1", "rb:2", "rb:none");
			layer.Categories.Select(c => c.Label).Should().Equal("< 4", "4–8", "≥ 8", "—");
		}

		[Test]
		public void Time_buckets_use_the_local_hour_and_keep_their_natural_order()
		{
			var context = Context(OneLayer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByTimeOfDay),
				Call(1, local: new DateTime(2026, 10, 19, 23, 0, 0)), Call(2, local: new DateTime(2026, 10, 19, 2, 0, 0)), Call(3, local: new DateTime(2026, 10, 19, 2, 30, 0)));

			var layer = MapVisualizationBuilder.Build(context).Layers[0];

			layer.Categories.Select(c => c.Key).Should().Equal("tod:0", "tod:3");
			layer.Categories[0].Count.Should().Be(2);
		}

		[Test]
		public void Unit_responses_are_one_point_per_unit_at_the_call_with_the_units_own_time()
		{
			var call = Call(1, response: 5);
			call.Units.Add(new MapVisualizationUnitResponseFact { UnitId = 7, ResponseMinutes = 5 });
			call.Units.Add(new MapVisualizationUnitResponseFact { UnitId = 8, ResponseMinutes = 11 });
			var context = Context(OneLayer(MapVisualizationKeys.SourceUnitResponses, MapVisualizationKeys.DisplayPoints, MapVisualizationKeys.ColorByUnit, MapVisualizationKeys.MetricResponseMinutes), call);
			context.Lookups.Units[7] = ("Engine 7", "Engine", null);
			context.Lookups.Units[8] = ("Medic 8", "Ambulance", null);

			var layer = MapVisualizationBuilder.Build(context).Layers[0];

			layer.Points.Should().HaveCount(2);
			layer.Points.Select(p => p[3]).Should().BeEquivalentTo(new double?[] { 5, 11 });
			layer.Categories.Select(c => c.Label).Should().BeEquivalentTo("Engine 7", "Medic 8");
		}

		[Test]
		public void Summary_has_a_weekday_hour_grid_and_response_percentiles_only_when_times_were_read()
		{
			var monday = new DateTime(2026, 10, 19, 14, 10, 0);
			var context = Context(OneLayer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHeat),
				Call(1, local: monday, response: 4), Call(2, local: monday, response: 6), Call(3, local: monday, response: 20));

			MapVisualizationBuilder.Build(context).Summary.MedianResponseMinutes.Should().BeNull();

			context.UnitTimesLoaded = true;
			var summary = MapVisualizationBuilder.Build(context).Summary;

			summary.ByWeekdayHour.Should().HaveCount(7);
			summary.ByWeekdayHour[(int)DayOfWeek.Monday][14].Should().Be(3);
			summary.MedianResponseMinutes.Should().Be(6);
			summary.P90ResponseMinutes.Should().Be(17.2);
			summary.TopCallTypes.Should().ContainSingle(c => c.Label == "Fire" && c.Count == 3);
		}

		[Test]
		public void Tracks_split_at_long_gaps_and_skip_jitter()
		{
			var start = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc);
			var points = new List<(double, double, DateTime)>
			{
				(40.0, -105.0, start),
				(40.00001, -105.0, start.AddMinutes(1)),
				(40.01, -105.0, start.AddMinutes(2)),
				(40.02, -105.0, start.AddMinutes(90)),
				(40.03, -105.0, start.AddMinutes(91))
			};

			var parts = MapVisualizationBuilder.SplitAtGaps(points).ToList();

			parts.Should().HaveCount(2);
			parts[0].Should().HaveCount(2, "the 1 m move is jitter");
			parts[1].Should().HaveCount(2);
		}

		[Test]
		public void A_layer_reports_its_source_notices()
		{
			var context = Context(OneLayer(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationKeys.DisplayHeat));
			context.AddNotice(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationNotices.PersonnelLocationsHidden);
			context.AddNotice(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationNotices.PersonnelLocationsHidden);

			MapVisualizationBuilder.Build(context).Layers[0].Notices.Should().Equal(MapVisualizationNotices.PersonnelLocationsHidden);
		}

		[Test]
		public void NormalizeColor_accepts_department_colors_with_or_without_a_hash()
		{
			MapVisualizationBuilder.NormalizeColor("#ABC").Should().Be("#aabbcc");
			MapVisualizationBuilder.NormalizeColor("ff0000").Should().Be("#ff0000");
			MapVisualizationBuilder.NormalizeColor("red").Should().BeNull();
			MapVisualizationBuilder.NormalizeColor(null).Should().BeNull();
		}

		#endregion Builder

		#region Service

		private sealed class ServiceHarness
		{
			public readonly Mock<IMapVisualizationsRepository> Repository = new Mock<IMapVisualizationsRepository>();
			public readonly Mock<IDispatchScopeService> DispatchScope = new Mock<IDispatchScopeService>();
			public readonly Mock<IAuthorizationService> Authorization = new Mock<IAuthorizationService>();
			public readonly Mock<IUserProfileService> Profiles = new Mock<IUserProfileService>();
			public readonly Mock<ICallDispatchesRepository> Dispatches = new Mock<ICallDispatchesRepository>();
			public readonly Mock<ICallDispatchGroupRepository> GroupDispatches = new Mock<ICallDispatchGroupRepository>();
			public readonly Mock<ICallDispatchUnitRepository> UnitDispatches = new Mock<ICallDispatchUnitRepository>();
			public readonly MapVisualizationService Service;

			public ServiceHarness()
			{
				DispatchScope.Setup(x => x.GetScopeForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
					.ReturnsAsync(new DispatchScope { DepartmentId = DepartmentId, IsDepartmentWide = true });
				DispatchScope.Setup(x => x.FilterCallsAsync(It.IsAny<DispatchScope>(), It.IsAny<List<Call>>()))
					.ReturnsAsync((DispatchScope _, List<Call> calls) => calls);
				Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
				Authorization.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
				Authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
				Authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);

				var departments = new Mock<IDepartmentsService>();
				departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "UTC" });

				Service = new MapVisualizationService(Repository.Object, departments.Object, Mock.Of<IDepartmentSettingsService>(),
					Mock.Of<ICallsService>(), DispatchScope.Object, Mock.Of<ICallGroupAssignmentService>(), Mock.Of<ICallStatusAttributionService>(),
					Mock.Of<IUnitsService>(), Dispatches.Object, GroupDispatches.Object, UnitDispatches.Object, Mock.Of<ICustomStateService>(),
					Mock.Of<IDepartmentGroupsService>(), Profiles.Object, Authorization.Object, Mock.Of<IGeoLocationProvider>(),
					Mock.Of<IUnitLocationsDocRepository>(), Mock.Of<IPersonnelLocationsDocRepository>(),
					new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
					new Lazy<IMongoRepository<PersonnelLocation>>(() => Mock.Of<IMongoRepository<PersonnelLocation>>()));
			}
		}

		private static MapVisualization Saved(string id, string owner, bool shared) => new MapVisualization
		{
			MapVisualizationId = id, DepartmentId = DepartmentId, Name = id, CreatedByUserId = owner, IsShared = shared,
			CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow
		};

		[Test]
		public async Task A_member_sees_shared_visualizations_and_their_own_only()
		{
			var harness = new ServiceHarness();
			harness.Repository.Setup(x => x.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<MapVisualization>
			{
				Saved("mine", UserId, false), Saved("theirs", OtherUserId, false), Saved("shared", OtherUserId, true)
			});

			var visible = await harness.Service.GetVisibleForUserAsync(DepartmentId, UserId.ToUpperInvariant());

			visible.Select(v => v.MapVisualizationId).Should().BeEquivalentTo("mine", "shared");
		}

		[Test]
		public async Task Only_department_admins_may_share_or_change_a_shared_visualization()
		{
			var harness = new ServiceHarness();
			harness.Repository.Setup(x => x.GetByIdForDepartmentAsync("shared", DepartmentId)).ReturnsAsync(Saved("shared", UserId, true));
			harness.Repository.Setup(x => x.GetByIdForDepartmentAsync("theirs", DepartmentId)).ReturnsAsync(Saved("theirs", OtherUserId, false));

			(await harness.Service.SaveAsync(DepartmentId, UserId, false, new MapVisualizationSaveRequest { Name = "x", IsShared = true }))
				.Error.Should().Be(MapVisualizationSaveErrors.NotAllowed, "sharing is department configuration");

			(await harness.Service.SaveAsync(DepartmentId, UserId, false, new MapVisualizationSaveRequest { MapVisualizationId = "shared", Name = "x" }))
				.Error.Should().Be(MapVisualizationSaveErrors.NotAllowed, "even its creator cannot change it once it is shared");

			(await harness.Service.SaveAsync(DepartmentId, UserId, true, new MapVisualizationSaveRequest { MapVisualizationId = "theirs", Name = "x" }))
				.Error.Should().Be(MapVisualizationSaveErrors.NotFound, "someone else's personal visualization is invisible, admin or not");

			var saved = await harness.Service.SaveAsync(DepartmentId, UserId, true, new MapVisualizationSaveRequest { MapVisualizationId = "shared", Name = " Renamed ", IsShared = true });
			saved.Error.Should().Be(MapVisualizationSaveErrors.None);
			saved.Saved.Name.Should().Be("Renamed");
			harness.Repository.Verify(x => x.UpdateAsync(It.Is<MapVisualization>(v => v.MapVisualizationId == "shared"), It.IsAny<CancellationToken>(), true), Times.Once);

			(await harness.Service.DeleteAsync(DepartmentId, UserId, false, "shared")).Should().BeFalse();
		}

		[Test]
		public async Task A_new_visualization_is_inserted_with_a_normalized_definition_and_the_department_limit_holds()
		{
			var harness = new ServiceHarness();
			harness.Repository.Setup(x => x.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<MapVisualization> { Saved("a", OtherUserId, true) });

			var result = await harness.Service.SaveAsync(DepartmentId, UserId, false, new MapVisualizationSaveRequest
			{
				Name = "Night calls",
				TemplateKey = "time-of-day",
				Definition = new MapVisualizationDefinition { Layers = { new MapVisualizationLayer { Id = "x", Source = "nope" } } }
			});

			result.Saved.Should().NotBeNull();
			result.Saved.CreatedByUserId.Should().Be(UserId);
			result.Saved.TemplateKey.Should().Be("time-of-day");
			result.Saved.Definition.Layers[0].Source.Should().Be(MapVisualizationKeys.SourceCalls);
			harness.Repository.Verify(x => x.InsertAsync(It.IsAny<MapVisualization>(), It.IsAny<CancellationToken>(), true), Times.Once);

			harness.Repository.Setup(x => x.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(
				Enumerable.Range(0, MapVisualizationLimits.MaxPersonalPerUser).Select(i => Saved($"p{i}", UserId, false)).ToList());
			(await harness.Service.SaveAsync(DepartmentId, UserId, false, new MapVisualizationSaveRequest { Name = "One more" }))
				.Error.Should().Be(MapVisualizationSaveErrors.LimitReached, "a member's personal list has its own cap");
			(await harness.Service.SaveAsync(DepartmentId, OtherUserId, false, new MapVisualizationSaveRequest { Name = "Someone else's" }))
				.Error.Should().Be(MapVisualizationSaveErrors.None, "the cap is per member");

			harness.Repository.Setup(x => x.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(
				Enumerable.Range(0, MapVisualizationLimits.MaxSavedPerDepartment).Select(i => Saved($"s{i}", OtherUserId, true)).ToList());
			(await harness.Service.SaveAsync(DepartmentId, UserId, true, new MapVisualizationSaveRequest { Name = "Shared", IsShared = true }))
				.Error.Should().Be(MapVisualizationSaveErrors.LimitReached);
		}

		[Test]
		public async Task Run_applies_dispatch_scope_hides_pending_and_filters_by_outcome()
		{
			var harness = new ServiceHarness();
			var now = DateTime.UtcNow;
			var calls = new List<Call>
			{
				new Call { CallId = 1, DepartmentId = DepartmentId, State = (int)CallStates.Closed, GeoLocationData = "40.1,-105.1", LoggedOn = now.AddHours(-3), Type = "Fire" },
				new Call { CallId = 3, DepartmentId = DepartmentId, State = (int)CallStates.FalseAlarm, GeoLocationData = "40.3,-105.3", LoggedOn = now.AddHours(-1) },
				new Call { CallId = 4, DepartmentId = DepartmentId, State = (int)CallStates.Closed, GeoLocationData = "40.4,-105.4", LoggedOn = now.AddHours(-1) }
			};
			harness.Repository.Setup(x => x.GetCallsForMapAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(),
					It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => calls.Select(c => new Call { CallId = c.CallId, DepartmentId = c.DepartmentId, State = c.State, GeoLocationData = c.GeoLocationData, LoggedOn = c.LoggedOn, Type = c.Type }).ToList());

			var scope = new DispatchScope { DepartmentId = DepartmentId, IsDepartmentWide = false, UserId = UserId };
			harness.DispatchScope.Setup(x => x.GetScopeForUserAsync(DepartmentId, UserId)).ReturnsAsync(scope);
			List<Call> scoped = null;
			harness.DispatchScope.Setup(x => x.FilterCallsAsync(scope, It.IsAny<List<Call>>()))
				.Callback((DispatchScope _, List<Call> list) => scoped = list)
				.ReturnsAsync((DispatchScope _, List<Call> list) => list.Where(c => c.CallId != 4).ToList());
			harness.UnitDispatches.Setup(x => x.GetCallUnitDispatchesForCallsInRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new[] { new CallDispatchUnit { CallId = 1, UnitId = 9 } });

			var definition = OneLayer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayPoints);
			definition.Options.ShowStations = false;
			definition.Filters.Outcomes = new List<int> { (int)CallStates.FalseAlarm, (int)CallStates.Closed };

			var result = await harness.Service.RunAsync(DepartmentId, UserId, definition);

			result.Layers[0].Points.Select(p => (int)p[4]).Should().BeEquivalentTo(new[] { 1, 3 }, "call 4 is outside the viewer's dispatch scope");
			harness.Repository.Verify(x => x.GetCallsForMapAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), MapVisualizationLimits.MaxCalls + 1,
				It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<int>>(),
				It.Is<IReadOnlyCollection<int>>(s => s.Count == 2 && s.Contains((int)CallStates.FalseAlarm)), It.IsAny<CancellationToken>()), Times.Once,
				"outcome filters run in SQL so the row cap applies after them");
			scoped.Should().OnlyContain(c => c.Dispatches != null && c.GroupDispatches != null && c.UnitDispatches != null,
				"dispatches are filled from range reads so the scope check never queries per call");
			scoped.Single(c => c.CallId == 1).UnitDispatches.Should().ContainSingle(d => d.UnitId == 9);
			harness.DispatchScope.Verify(x => x.FilterCallsForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<Call>>()), Times.Never);
		}

		[Test]
		public async Task Run_leaves_out_responder_origins_the_viewer_may_not_see_and_says_so()
		{
			var harness = new ServiceHarness();
			var now = DateTime.UtcNow;
			harness.Repository.Setup(x => x.GetActionLogsWithLocationForMapAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), MapVisualizationLimits.MaxPointsPerLayer + 1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ActionLog>
				{
					new ActionLog { UserId = "seen", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "40.1,-105.1", Timestamp = now.AddHours(-1) },
					new ActionLog { UserId = "hidden", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "40.2,-105.2", Timestamp = now.AddHours(-1) },
					new ActionLog { UserId = "seen", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.StandingBy, GeoLocationData = "40.3,-105.3", Timestamp = now.AddHours(-1) },
					new ActionLog { UserId = "seen", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "0,0", Timestamp = now.AddHours(-1) },
					new ActionLog { UserId = "seen", DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "40.5,-105.5", Timestamp = now.AddHours(-1),
						DestinationId = 77, DestinationType = (int)DestinationEntityTypes.Call }
				});
			harness.Authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync("hidden", UserId, DepartmentId)).ReturnsAsync(false);

			var definition = OneLayer(MapVisualizationKeys.SourceResponderOrigins, MapVisualizationKeys.DisplayPoints);
			definition.Options.ShowStations = false;

			var layer = (await harness.Service.RunAsync(DepartmentId, UserId, definition)).Layers[0];

			layer.Points.Should().ContainSingle(p => p[0] == 40.1,
				"only the responding status the viewer may see, with a location, and not one for a call outside the filtered calls (77)");
			layer.WithoutLocation.Should().Be(1);
			layer.Notices.Should().Contain(MapVisualizationNotices.PersonnelLocationsHidden);

			definition.Filters.CallTypes = new List<string> { "Fire" };
			(await harness.Service.RunAsync(DepartmentId, UserId, definition)).Layers[0].Points
				.Should().BeEmpty("with a call filter set, a status linked to no call cannot be shown as matching it");
		}

		[Test]
		public void Chunks_cut_long_call_lists_by_time_and_count()
		{
			var start = new DateTime(2025, 1, 1);
			var calls = Enumerable.Range(0, 400).Select(i => new Call { CallId = i, LoggedOn = start.AddDays(i) }).ToList();

			var chunks = MapVisualizationService.Chunk(calls).ToList();

			chunks.Sum(c => c.Count).Should().Be(400);
			chunks.Should().OnlyContain(c => (c.Max(x => x.LoggedOn) - c.Min(x => x.LoggedOn)).TotalDays <= 366);
			chunks.Should().HaveCountGreaterThan(1, "400 days of calls do not fit one slice");
		}

		[Test]
		public void Track_window_uses_the_calls_own_span_and_caps_long_windows()
		{
			var now = new DateTime(2026, 10, 22, 12, 0, 0);
			var call = new Call { LoggedOn = now.AddHours(-5), ClosedOn = now.AddHours(-3) };

			var focused = MapVisualizationService.TrackWindow(call, now.AddDays(-30), now, now);
			focused.Start.Should().Be(call.LoggedOn.AddMinutes(-15));
			focused.End.Should().Be(call.ClosedOn.Value.AddMinutes(15));
			focused.Shortened.Should().BeFalse();

			var year = MapVisualizationService.TrackWindow(null, now.AddDays(-365), now, now);
			year.Start.Should().Be(now.AddDays(-MapVisualizationLimits.MaxTrackWindowDays));
			year.Shortened.Should().BeTrue();
		}

		[Test]
		public void Bad_times_are_dropped_rather_than_skewing_a_median()
		{
			var logged = new DateTime(2026, 10, 22, 8, 0, 0);

			MapVisualizationService.Minutes(logged, logged.AddMinutes(7.5), 1440).Should().Be(7.5);
			MapVisualizationService.Minutes(logged, logged.AddMinutes(-1), 1440).Should().BeNull("a status before the call was logged is a clock problem");
			MapVisualizationService.Minutes(logged, logged.AddDays(2), 1440).Should().BeNull();
		}

		[Test]
		public async Task Run_ignores_a_unit_filter_for_units_the_viewer_may_not_see()
		{
			var harness = new ServiceHarness();
			harness.Authorization.Setup(x => x.CanUserViewUnitViaMatrixAsync(5, UserId, DepartmentId)).ReturnsAsync(false);

			var definition = OneLayer(MapVisualizationKeys.SourceCalls, MapVisualizationKeys.DisplayHeat);
			definition.Filters.UnitIds = new List<int> { 5, 6 };

			var result = await harness.Service.RunAsync(DepartmentId, UserId, definition);

			result.Definition.Filters.UnitIds.Should().Equal(6);
		}

		[Test]
		public async Task Search_needs_two_characters_and_scopes_the_matches()
		{
			var harness = new ServiceHarness();
			harness.Repository.Setup(x => x.GetCallsForMapAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(),
					It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<Call>
				{
					new Call { CallId = 1, DepartmentId = DepartmentId, Number = "26-100", Name = "Structure fire", LoggedOn = DateTime.UtcNow },
					new Call { CallId = 2, DepartmentId = DepartmentId, Number = "26-101", Name = "Medical", LoggedOn = DateTime.UtcNow }
				});

			(await harness.Service.SearchCallsAsync(DepartmentId, UserId, "s", 10)).Should().BeEmpty();
			(await harness.Service.SearchCallsAsync(DepartmentId, UserId, "fire", 10)).Select(c => c.CallId).Should().Equal(1);
		}

		#endregion Service

		#region Web

		[Test]
		public void The_controller_requires_call_view_and_every_write_checks_the_antiforgery_token()
		{
			var type = typeof(MapVisualizationsController);

			type.GetCustomAttributes<AuthorizeAttribute>().Should().Contain(a => a.Policy == ResgridResources.Call_View);

			var posts = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
				.ToList();

			posts.Select(m => m.Name).Should().BeEquivalentTo("Data", "Save", "Delete");
			posts.Should().OnlyContain(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null);
		}

		[Test]
		public async Task Data_rejects_a_body_that_did_not_bind()
		{
			var controller = new MapVisualizationsController(Mock.Of<IMapVisualizationService>(), Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<ICallsService>(),
				Mock.Of<IUnitsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDispatchScopeService>(),
				Mock.Of<IAuthorizationService>(), Mock.Of<Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.MapVisualizations>>());

			(await controller.Data(null, CancellationToken.None)).Should().BeOfType<BadRequestResult>();
		}

		#endregion Web
	}
}
