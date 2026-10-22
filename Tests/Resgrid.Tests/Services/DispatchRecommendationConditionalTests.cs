using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	/// <summary>Conditional run cards: decision graphs, packages, fallbacks, first-arriving rows, pins and codes.</summary>
	public partial class DispatchRecommendationServiceTests
	{
		private const int LadderPackageId = 700;
		private const int EnginePackageId = 701;
		private const int SubstitutePackageId = 702;

		private void EnableConditions(params ResponsePackage[] packages)
		{
			_featureToggleService.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()))
				.ReturnsAsync(true);
			_responsePackagesService.Setup(x => x.GetPackagesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(packages.ToList());
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationModeAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(DispatchRecommendationModes.ClosestUnit);
		}

		private static ResponsePackage Package(int id, string name, params ResponsePackageRow[] rows)
		{
			var sort = 0;
			foreach (var row in rows)
			{
				row.ResponsePackageId = id;
				row.SortOrder = sort++;
			}

			return new ResponsePackage { ResponsePackageId = id, DepartmentId = DepartmentId, Name = name, Rows = rows.ToList() };
		}

		private static ResponsePackageRow PackageRow(int rowId, string label, int count, params RunCardTarget[] targets)
		{
			return new ResponsePackageRow { ResponsePackageRowId = rowId, Label = label, RequiredCount = count, Targets = targets.ToList() };
		}

		private static RunCardTarget UnitTypeTarget(int unitTypeId) => new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitType, TargetRefId = unitTypeId };

		private static RunCardTarget AnyUnitTarget() => new RunCardTarget { TargetKind = (int)RunCardTargetKinds.AnyUnit };

		/// <summary>"Ladder at least a minute before the closest engine?" yes: ladder package, no: engine package.</summary>
		private RunCard BuildGraphCard()
		{
			var card = BuildCard();
			card.AlarmLevels.First().DecisionNodes = new List<RunCardDecisionNode>
			{
				new RunCardDecisionNode
				{
					NodeKey = "root", IsRoot = true, NodeKind = (int)RunCardDecisionNodeKinds.Condition, Label = "Ladder first?",
					ConditionType = (int)RunCardConditionTypes.ArrivesBefore, ThresholdSeconds = 60,
					SubjectTargets = new List<RunCardTarget> { UnitTypeTarget(LadderTypeId) },
					ReferenceTargets = new List<RunCardTarget> { UnitTypeTarget(EngineTypeId) },
					YesNodeKey = "ladder", NoNodeKey = "engine"
				},
				new RunCardDecisionNode { NodeKey = "ladder", NodeKind = (int)RunCardDecisionNodeKinds.Outcome, Label = "Send the ladder", ResponsePackageId = LadderPackageId },
				new RunCardDecisionNode { NodeKey = "engine", NodeKind = (int)RunCardDecisionNodeKinds.Outcome, Label = "Send an engine", ResponsePackageId = EnginePackageId }
			};

			EnableConditions(
				Package(LadderPackageId, "Ladder", PackageRow(9001, "Ladder", 1, UnitTypeTarget(LadderTypeId))),
				Package(EnginePackageId, "Engine", PackageRow(9002, "Engine", 1, UnitTypeTarget(EngineTypeId))));

			return card;
		}

		private void PlaceUnits(params (int unitId, double latitude, double longitude)[] fixes)
		{
			_unitsService.Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId))
				.ReturnsAsync(fixes.Select(f => new UnitsLocation { UnitId = f.unitId, Latitude = (decimal)f.latitude, Longitude = (decimal)f.longitude, Timestamp = DateTime.UtcNow }).ToList());
		}

		[Test]
		public async Task conditional_arrives_before_picks_the_yes_outcome_package()
		{
			BuildGraphCard();
			// Ladder on top of the call, both engines ~70 km away.
			PlaceUnits((1, 40.5, -105.5), (2, 40.5, -105.5), (3, 39.7501, -104.9501));

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.Should().NotBeNull();
			result.Decision.LevelHasGraph.Should().BeTrue();
			result.Decision.Path.Should().ContainSingle(p => p.NodeKey == "root" && p.Result == RunCardConditionResults.Yes);
			result.Decision.OutcomeNodeKey.Should().Be("ladder");
			result.Decision.PackageName.Should().Be("Ladder");
			result.Units.Should().ContainSingle(u => u.UnitId == 3 && u.RequirementScope == RequirementScopes.Package && u.SatisfiesRequirementId == 9001 && u.RowLabel == "Ladder");
			result.Notes.Should().Contain(n => n.StartsWith("Condition 'Ladder first?': Yes"));
		}

		[Test]
		public async Task conditional_arrives_before_takes_the_no_branch_when_the_reference_is_faster()
		{
			BuildGraphCard();
			// Engine 1 on the call, the ladder far away.
			PlaceUnits((1, 39.7501, -104.9501), (2, 40.5, -105.5), (3, 40.5, -105.5));

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.Path.Single().Result.Should().Be(RunCardConditionResults.No);
			result.Decision.Path.Single().MarginSeconds.Should().BeLessThan(0);
			result.Decision.OutcomeNodeKey.Should().Be("engine");
			result.Units.Should().ContainSingle(u => u.UnitId == 1);
		}

		[Test]
		public async Task conditional_missing_reference_counts_as_infinitely_slow()
		{
			BuildGraphCard();
			PlaceUnits((3, 39.7501, -104.9501));
			// No engine is available at all.
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId))
				.ReturnsAsync(new List<UnitState>
				{
					new UnitState { UnitId = 1, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow },
					new UnitState { UnitId = 2, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow }
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.Path.Single().Result.Should().Be(RunCardConditionResults.Yes);
			result.Decision.Path.Single().Note.Should().Contain("compared resource");
		}

		[Test]
		public async Task conditional_available_within_counts_the_first_ranked_units()
		{
			var card = BuildCard();
			card.AlarmLevels.First().DecisionNodes = new List<RunCardDecisionNode>
			{
				new RunCardDecisionNode
				{
					NodeKey = "erf", IsRoot = true, NodeKind = (int)RunCardDecisionNodeKinds.Condition, Label = "Two engines within 8 minutes?",
					ConditionType = (int)RunCardConditionTypes.AvailableWithin, ThresholdSeconds = 480, RequiredCount = 2,
					SubjectTargets = new List<RunCardTarget> { UnitTypeTarget(EngineTypeId) }, YesNodeKey = "ok", NoNodeKey = "more"
				},
				new RunCardDecisionNode { NodeKey = "ok", NodeKind = (int)RunCardDecisionNodeKinds.Outcome, Label = "Enough", ResponsePackageId = EnginePackageId },
				new RunCardDecisionNode { NodeKey = "more", NodeKind = (int)RunCardDecisionNodeKinds.Outcome, Label = "Add the ladder", ResponsePackageId = LadderPackageId }
			};
			EnableConditions(
				Package(LadderPackageId, "Ladder", PackageRow(9001, "Ladder", 1, UnitTypeTarget(LadderTypeId))),
				Package(EnginePackageId, "Engine", PackageRow(9002, "Engine", 1, UnitTypeTarget(EngineTypeId))));

			// Both engines close: yes.
			PlaceUnits((1, 39.7501, -104.9501), (2, 39.7502, -104.9502), (3, 40.5, -105.5));
			var both = await _service.GetRecommendationAsync(BuildRequest());
			both.Decision.OutcomeNodeKey.Should().Be("ok");

			// The second engine far away: no.
			PlaceUnits((1, 39.7501, -104.9501), (2, 40.5, -105.5), (3, 40.5, -105.5));
			var one = await _service.GetRecommendationAsync(BuildRequest());
			one.Decision.OutcomeNodeKey.Should().Be("more");
			one.Decision.Path.Single().MarginSeconds.Should().BeLessThan(0);
		}

		[Test]
		public async Task conditional_substitute_fills_a_short_row_and_keeps_it_outstanding()
		{
			var card = BuildCard();
			card.AlarmLevels.First().ResponsePackageId = LadderPackageId;
			var ladders = PackageRow(9001, "Ladders", 2, UnitTypeTarget(LadderTypeId));
			ladders.SubstitutePackageId = SubstitutePackageId;
			ladders.KeepOutstanding = true;
			EnableConditions(
				Package(LadderPackageId, "Ladders", ladders),
				Package(SubstitutePackageId, "Engine instead", PackageRow(9003, "Engine", 1, UnitTypeTarget(EngineTypeId))));

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.LevelHasGraph.Should().BeFalse();
			result.Decision.PackageName.Should().Be("Ladders");
			result.Units.Should().Contain(u => u.UnitId == 3 && u.SatisfiesRequirementId == 9001);
			result.Units.Should().ContainSingle(u => u.SelectionReason == RecommendationSelectionReasons.SubstitutePackage && u.SatisfiesRequirementId == 9001);
			result.Shortfalls.Should().BeEmpty();
			result.Decision.IdealConfigurationUnavailable.Should().BeTrue();
			var fallback = result.Decision.Fallbacks.Should().ContainSingle().Subject;
			fallback.Step.Should().Be(RequirementFallbackSteps.Substitute);
			fallback.Outstanding.Should().BeTrue();
			fallback.RequiredCount.Should().Be(2);
			fallback.FilledCount.Should().Be(1);
			fallback.SentInstead.Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task conditional_short_row_without_substitute_is_a_shortfall_marked_outstanding()
		{
			var card = BuildCard();
			card.AlarmLevels.First().ResponsePackageId = LadderPackageId;
			var ladders = PackageRow(9001, "Ladders", 2, UnitTypeTarget(LadderTypeId));
			ladders.KeepOutstanding = true;
			EnableConditions(Package(LadderPackageId, "Ladders", ladders));

			var result = await _service.GetRecommendationAsync(BuildRequest());

			var shortfall = result.Shortfalls.Should().ContainSingle().Subject;
			shortfall.RequirementScope.Should().Be(RequirementScopes.Package);
			shortfall.RequirementId.Should().Be(9001);
			shortfall.Outstanding.Should().BeTrue();
			shortfall.FilledCount.Should().Be(1);
			shortfall.TypeOrRoleName.Should().Be("Ladders");
			result.Decision.Fallbacks.Should().ContainSingle(f => f.Step == RequirementFallbackSteps.Shortfall && f.Outstanding);
		}

		[Test]
		public async Task conditional_relaxed_fill_drops_the_status_filter_when_the_card_allows_it()
		{
			var card = BuildCard();
			card.RelaxUnfilledRows = true;
			card.AlarmLevels.First().ResponsePackageId = EnginePackageId;
			var engine = PackageRow(9002, "Engine from quarters", 1, UnitTypeTarget(EngineTypeId));
			engine.StatusFilter = (int)RunCardStatusFilters.InQuarters;
			EnableConditions(Package(EnginePackageId, "Engine", engine));

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.SelectionReason == RecommendationSelectionReasons.RelaxedFill);
			result.Decision.Fallbacks.Should().ContainSingle(f => f.Step == RequirementFallbackSteps.Relaxed && !f.Outstanding);
			result.Shortfalls.Should().BeEmpty();

			// Without the card option the filter holds and the row is short.
			card.RelaxUnfilledRows = false;
			var strict = await _service.GetRecommendationAsync(BuildRequest());
			strict.Units.Should().BeEmpty();
			strict.Shortfalls.Should().ContainSingle(s => s.Reason == RequirementShortfallReasons.NoUnitMatchingStatusFilter);
		}

		[Test]
		public async Task conditional_first_arriving_row_adds_the_fastest_unit_only_when_not_already_proposed()
		{
			var card = BuildCard();
			card.AlarmLevels.First().ResponsePackageId = EnginePackageId;
			var first = new ResponsePackageRow { ResponsePackageRowId = 9010, Label = "Closest resource", FillMode = (int)RequirementFillModes.EnsureFirstArriving, Targets = new List<RunCardTarget> { AnyUnitTarget() } };
			EnableConditions(Package(EnginePackageId, "Engine", PackageRow(9002, "Engine", 1, UnitTypeTarget(EngineTypeId)), first));

			// Engine 1 is the fastest of all: already included.
			PlaceUnits((1, 39.7501, -104.9501), (2, 40.5, -105.5), (3, 40.0, -105.0));
			var included = await _service.GetRecommendationAsync(BuildRequest());
			included.Units.Should().ContainSingle(u => u.UnitId == 1);
			included.Decision.FirstArriving.Should().ContainSingle(f => f.AlreadyIncluded && f.ResourceName == "Engine 1");

			// The ladder is fastest: it is added.
			PlaceUnits((1, 40.0, -105.0), (2, 40.5, -105.5), (3, 39.7501, -104.9501));
			var added = await _service.GetRecommendationAsync(BuildRequest());
			added.Units.Should().Contain(u => u.UnitId == 3 && u.SelectionReason == RecommendationSelectionReasons.FirstArriving);
			added.Decision.FirstArriving.Should().ContainSingle(f => !f.AlreadyIncluded && f.ResourceName == "Ladder 1");
		}

		[Test]
		public async Task conditional_rows_carry_the_response_code_and_enrich_writes_it_on_the_dispatch()
		{
			var card = BuildCard();
			card.AlarmLevels.First().ResponsePackageId = EnginePackageId;
			var engine = PackageRow(9002, "Engine", 1, UnitTypeTarget(EngineTypeId));
			engine.ResponseCodeId = 4;
			EnableConditions(Package(EnginePackageId, "Engine", engine));
			_departmentSettingsService.Setup(x => x.GetDispatchResponseCodesAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DispatchResponseCodesConfig { Codes = new List<DispatchResponseCode> { new DispatchResponseCode { Id = 4, Code = "C3", Name = "Lights and siren", Color = "#d9534f" } } });
			_runCardsService.Setup(x => x.GetMatchingRunCardAsync(DepartmentId, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync(card);

			var result = await _service.GetRecommendationAsync(BuildRequest());
			result.Units.Single().ResponseCode.Should().Be("C3");
			result.Units.Single().ResponseCodeColor.Should().Be("#d9534f");

			var call = new Call { CallId = 0, DepartmentId = DepartmentId, Priority = 3, Type = "Structure Fire", GeoLocationData = "39.75,-104.95", UnitDispatches = new Collection<CallDispatchUnit>() };
			await _service.EnrichCallForDispatchAsync(call, 1);

			var dispatch = call.UnitDispatches.Should().ContainSingle().Subject;
			dispatch.ResponseCode.Should().Be("C3");
			dispatch.ResponseCodeName.Should().Be("Lights and siren");
			dispatch.ResponseCodeId.Should().Be(4);
		}

		[Test]
		public async Task conditional_adding_resources_uses_the_pinned_rows_minus_what_is_on_the_call()
		{
			BuildGraphCard();
			var pinnedRows = new List<ResolvedRequirementRow>
			{
				new ResolvedRequirementRow { Scope = RequirementScopes.Package, RequirementId = 9002, Label = "Engines", RequiredCount = 2, Targets = new List<RunCardTarget> { UnitTypeTarget(EngineTypeId) } }
			};
			_runCardDecisionsRepository.Setup(x => x.GetDecisionsByCallIdAsync(77)).ReturnsAsync(new List<RunCardDecision>
			{
				new RunCardDecision
				{
					RunCardDecisionId = 1, DepartmentId = DepartmentId, CallId = 77, RunCardId = 5, AlarmLevel = 1, LeafNodeKey = "engine", LeafLabel = "Send an engine",
					PackageSnapshotJson = JsonConvert.SerializeObject(pinnedRows),
					PathJson = JsonConvert.SerializeObject(new RunCardDecisionExplanation { OutcomeLabel = "Send an engine", PackageName = "Engine" }),
					Source = (int)RunCardDecisionSources.SaveTime, CreatedOn = DateTime.UtcNow.AddMinutes(-5)
				}
			});

			var request = BuildRequest();
			request.CallId = 77;
			request.CountDispatchedTowardRequirements = true;
			request.AlreadyDispatchedUnitIds.Add(1);

			var result = await _service.GetRecommendationAsync(request);

			result.Decision.Pinned.Should().BeTrue();
			result.Decision.OutcomeLabel.Should().Be("Send an engine");
			result.Decision.Path.Should().BeEmpty();
			result.Units.Should().ContainSingle(u => u.UnitId == 2);
			result.Notes.Should().Contain("1 of 2 'Engines' already on the call.");

			// Re-evaluate walks the graph fresh instead.
			request.Reevaluate = true;
			var fresh = await _service.GetRecommendationAsync(request);
			fresh.Decision.Pinned.Should().BeFalse();
			fresh.Decision.Path.Should().ContainSingle();
		}

		[Test]
		public async Task conditional_forced_leaf_from_the_preview_is_kept()
		{
			BuildGraphCard();
			PlaceUnits((1, 40.5, -105.5), (2, 40.5, -105.5), (3, 39.7501, -104.9501));

			var request = BuildRequest();
			request.ForcedLeafNodeKey = "engine";
			var result = await _service.GetRecommendationAsync(request);

			result.Decision.FromPreview.Should().BeTrue();
			result.Decision.OutcomeNodeKey.Should().Be("engine");
			result.Units.Should().OnlyContain(u => u.UnitTypeId == EngineTypeId);

			// A key that is not an outcome of this level is ignored.
			request.ForcedLeafNodeKey = "root";
			var walked = await _service.GetRecommendationAsync(request);
			walked.Decision.FromPreview.Should().BeFalse();
			walked.Decision.OutcomeNodeKey.Should().Be("ladder");
		}

		[Test]
		public async Task conditional_advice_only_outcome_recommends_nothing_and_raises_no_shortfall()
		{
			var card = BuildCard();
			card.AlarmLevels.First().DecisionNodes = new List<RunCardDecisionNode>
			{
				new RunCardDecisionNode { NodeKey = "aid", IsRoot = true, NodeKind = (int)RunCardDecisionNodeKinds.Outcome, Label = "Mutual aid", AdviceText = "Request mutual aid from the county." }
			};
			EnableConditions();

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.Outcome.Should().Be(RecommendationOutcomes.Advice);
			result.Decision.AdviceText.Should().Be("Request mutual aid from the county.");
			result.Units.Should().BeEmpty();
			result.Shortfalls.Should().BeEmpty();
		}

		[Test]
		public async Task fixed_level_without_graph_or_package_stays_on_the_legacy_path_with_the_feature_on()
		{
			BuildCard(engineCount: 1);
			EnableConditions();
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationModeAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(DispatchRecommendationModes.StationBased);

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.Should().BeNull();
			result.Units.Should().ContainSingle(u => u.UnitId == 1 && u.SelectionReason == RecommendationSelectionReasons.InGeofence);
		}

		[Test]
		public async Task conditional_level_is_ignored_while_the_feature_is_off()
		{
			BuildGraphCard();
			_featureToggleService.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()))
				.ReturnsAsync(false);

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.Should().BeNull();
			result.Units.Should().BeEmpty();
		}

		[Test]
		public async Task a_named_run_card_is_evaluated_even_when_disabled_and_only_for_its_department()
		{
			var card = BuildGraphCard();
			card.IsDisabled = true;
			_runCardsService.Setup(x => x.GetRunCardByIdAsync(5)).ReturnsAsync(card);
			PlaceUnits((1, 40.5, -105.5), (2, 40.5, -105.5), (3, 39.7501, -104.9501));

			var request = BuildRequest();
			request.RunCardId = 5;
			var result = await _service.GetRecommendationAsync(request);

			result.MatchedRunCardId.Should().Be(5);
			result.Decision.OutcomeNodeKey.Should().Be("ladder");
			_runCardsService.Verify(x => x.GetMatchingRunCardAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);

			card.DepartmentId = DepartmentId + 1;
			(await _service.GetRecommendationAsync(request)).MatchedRunCardId.Should().BeNull();
		}

		[Test]
		public async Task an_outcome_naming_the_level_package_does_not_ask_for_its_rows_twice()
		{
			var card = BuildGraphCard();
			card.AlarmLevels.First().ResponsePackageId = LadderPackageId;
			PlaceUnits((1, 40.5, -105.5), (2, 40.5, -105.5), (3, 39.7501, -104.9501));

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Decision.OutcomeNodeKey.Should().Be("ladder");
			result.Decision.PackageName.Should().Be("Ladder");
			result.Decision.ResolvedRows.Should().ContainSingle(r => r.RequirementId == 9001);
			result.Units.Should().ContainSingle(u => u.UnitId == 3);
		}

		[Test]
		public async Task record_decision_pins_the_outcome_and_records_outstanding_requirements()
		{
			BuildGraphCard();
			PlaceUnits((1, 40.5, -105.5), (2, 40.5, -105.5), (3, 39.7501, -104.9501));
			var result = await _service.GetRecommendationAsync(BuildRequest());

			RunCardDecision saved = null;
			_runCardDecisionsRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<RunCardDecision>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback<RunCardDecision, CancellationToken, bool>((d, c, f) => saved = d)
				.ReturnsAsync((RunCardDecision d, CancellationToken c, bool f) => d);

			var call = new Call { CallId = 77, DepartmentId = DepartmentId };
			await _service.RecordDecisionAsync(call, result, "user-1", RunCardDecisionSources.SaveTime);

			saved.Should().NotBeNull();
			saved.CallId.Should().Be(77);
			saved.LeafNodeKey.Should().Be("ladder");
			JsonConvert.DeserializeObject<List<ResolvedRequirementRow>>(saved.PackageSnapshotJson).Should().ContainSingle(r => r.RequirementId == 9001);
			result.Decision.Pinned.Should().BeTrue();
			_outstandingRequirementsService.Verify(x => x.CreateFromRecommendationAsync(call, result, "user-1", It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
