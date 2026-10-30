using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// Conditional run cards: decision graphs, response packages, "any of" targets, fallbacks and first-arriving rows. A
	/// level takes this path when it has a decision graph or uses a response package and the department has
	/// Dispatch.RunCardConditions on; every other level keeps the fixed fill in DispatchRecommendationService.cs unchanged.
	/// One ranking (turnout plus travel, routed when available) feeds both the conditions and the fill, so a condition's
	/// pick is exactly what the fill would send first.
	/// </summary>
	public partial class DispatchRecommendationService
	{
		#region Conditional state

		/// <summary>A unit or person ranked for a set of targets and a status filter.</summary>
		private sealed class RankedResource
		{
			public UnitCandidate Unit { get; set; }
			public PersonnelCandidate Person { get; set; }
			public bool IsPerson => Person != null;
			public double? Distance { get; set; }
			public int TurnoutSeconds { get; set; }
			/// <summary>Turnout plus straight-line travel estimate; null without a position or a call location.</summary>
			public double? EstimatedSeconds { get; set; }
			/// <summary>Routed drive time (travel only) when looked up and the lookup succeeded.</summary>
			public double? RoutedEtaSeconds { get; set; }
			public bool RoutedLookedUp { get; set; }
			public bool InRestPeriod { get; set; }
			public bool Stale { get; set; }
			/// <summary>Station mode: how many stations out (0 = first in the cascade).</summary>
			public int CascadeDepth { get; set; }
			public int? StationGroupId { get; set; }
			public string StationGroupName { get; set; }
			public bool InContainingStation { get; set; }
			public double? StationDistance { get; set; }

			public double? ResponseSeconds => RoutedEtaSeconds.HasValue ? TurnoutSeconds + RoutedEtaSeconds.Value : EstimatedSeconds;
			public ResponseTimeSources TimeSource => RoutedEtaSeconds.HasValue ? ResponseTimeSources.Routed : ResponseTimeSources.Estimated;
			public string ResourceId => IsPerson ? Person.UserId : Unit.Unit.UnitId.ToString(CultureInfo.InvariantCulture);
			public string SortName => IsPerson ? Person.UserId : Unit.Unit.Name;
		}

		private sealed class ResourceRanking
		{
			public List<RankedResource> Ranked { get; set; } = new List<RankedResource>();
			/// <summary>Resources matching the targets, any status.</summary>
			public int Matching { get; set; }
			/// <summary>Resources matching the targets and the status filter, before position checks.</summary>
			public int MatchingFilter { get; set; }
			/// <summary>Left out for data quality: no position, or a position too old.</summary>
			public int ExcludedForData { get; set; }
			public int OutsideRadius { get; set; }
			/// <summary>How many leading entries have had a routed lookup and been re-ranked.</summary>
			public int RoutedThrough { get; set; }
		}

		private sealed class ConditionalState
		{
			public DispatchRecommendationModes Mode { get; set; }
			/// <summary>The department ranks closest resources by straight-line distance rather than by response time.</summary>
			public bool ByDistance { get; set; }
			public GeoMath.GeoPoint? Anchor { get; set; }
			public List<StationDistanceResult> Stations { get; set; } = new List<StationDistanceResult>();
			public Dictionary<int, HashSet<string>> Rosters { get; set; } = new Dictionary<int, HashSet<string>>();
			public Dictionary<string, ResourceRanking> Rankings { get; set; } = new Dictionary<string, ResourceRanking>(StringComparer.Ordinal);
			public Dictionary<int, double?> UnitEtas { get; set; } = new Dictionary<int, double?>();
			public Dictionary<string, double?> UserEtas { get; set; } = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
			public HashSet<int> PickedUnitIds { get; set; } = new HashSet<int>();
			public HashSet<string> PickedUserIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public DispatchResponseCodesConfig Codes { get; set; } = new DispatchResponseCodesConfig();
			public Dictionary<int, ResponsePackage> Packages { get; set; } = new Dictionary<int, ResponsePackage>();
			public Dictionary<string, string> PersonNames { get; set; }
			public RunCardDecisionExplanation Decision { get; set; }
			/// <summary>Add-resources: what is already on the call, consumed row by row (each resource counted once).</summary>
			public List<KeyValuePair<int, UnitType>> DispatchedUnitPool { get; set; } = new List<KeyValuePair<int, UnitType>>();
			public List<KeyValuePair<string, List<PersonnelRole>>> DispatchedUserPool { get; set; } = new List<KeyValuePair<string, List<PersonnelRole>>>();
			public bool AnythingNeeded { get; set; }
		}

		private sealed class RowFill
		{
			public int Needed { get; set; }
			public int Configured { get; set; }
			public int Relaxed { get; set; }
			public int Filled => Configured + Relaxed;
			public ResourceRanking Ranking { get; set; }
		}

		#endregion

		#region Entry points

		private async Task<bool> IsConditionalLevelAsync(int departmentId, RunCardAlarmLevel level)
		{
			if (level == null || (!RunCardDecisionGraph.HasGraph(level) && !level.ResponsePackageId.HasValue))
				return false;

			return await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, departmentId);
		}

		private async Task RunConditionalLevelAsync(RecommendationContext context, DispatchRecommendationModes mode)
		{
			var request = context.Request;
			var level = context.Level;

			var state = new ConditionalState
			{
				Mode = mode,
				ByDistance = context.Config.RanksByDistance,
				Decision = new RunCardDecisionExplanation
				{
					LevelHasGraph = RunCardDecisionGraph.HasGraph(level),
					Outcome = RecommendationOutcomes.Dispatch
				}
			};
			context.Result.Decision = state.Decision;

			state.Codes = await _departmentSettingsService.GetDispatchResponseCodesAsync(request.DepartmentId) ?? new DispatchResponseCodesConfig();
			state.Packages = (await _responsePackagesService.GetPackagesForDepartmentAsync(request.DepartmentId) ?? new List<ResponsePackage>())
				.Where(p => p != null)
				.GroupBy(p => p.ResponsePackageId)
				.ToDictionary(g => g.Key, g => g.First());

			// Rows and conditions name roles nobody holds, which the candidate pool never saw.
			foreach (var role in (await _personnelRolesService.GetRolesForDepartmentUnlimitedAsync(request.DepartmentId) ?? new List<PersonnelRole>()).Where(r => r != null))
			{
				if (!context.RoleNames.ContainsKey(role.PersonnelRoleId))
					context.RoleNames[role.PersonnelRoleId] = role.Name;
			}

			if (context.Config.RestPeriodMinutes > 0)
			{
				context.UnitLastDispatched = await _runCardsService.GetLastUnitDispatchTimesAsync(request.DepartmentId);
				context.UserLastDispatched = await _runCardsService.GetLastUserDispatchTimesAsync(request.DepartmentId);
			}

			context.CallLocation = ResolveCallLocation(request);
			await PrepareConditionalPositionsAsync(context, state);

			List<ResolvedRequirementRow> rows = null;

			// Adding resources to a call already out reuses the decision pinned when it was saved: re-walking now would
			// see the dispatched units missing from the pool and flip conditions they decided.
			if (request.CallId.HasValue && request.CountDispatchedTowardRequirements && !request.Reevaluate)
				rows = await TryUsePinnedDecisionAsync(context, state);

			if (rows == null)
				rows = await ResolveRowsAsync(context, state);

			if (request.CountDispatchedTowardRequirements)
			{
				state.DispatchedUnitPool = context.DispatchedUnitTypes.ToList();
				state.DispatchedUserPool = context.DispatchedUserRoles.ToList();
			}

			await FillResolvedRowsAsync(context, state, rows);

			state.Decision.ResolvedRows = rows;
			state.Decision.IdealConfigurationUnavailable = state.Decision.Fallbacks.Any();

			if (request.CountDispatchedTowardRequirements && rows.Any() && !state.AnythingNeeded)
				context.Result.Notes.Add($"The resources on the call already cover alarm level {level.AlarmLevel} of run card '{context.Card.Name}'.");

			if (state.Decision.IdealConfigurationUnavailable)
				context.Result.Notes.Add("The ideal configuration was not available; see the fallbacks taken.");
		}

		public async Task RecordDecisionAsync(Call call, DispatchRecommendationResult result, string createdByUserId, RunCardDecisionSources source,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (call == null || call.CallId <= 0 || result?.Decision == null || !result.MatchedRunCardId.HasValue)
				return;

			try
			{
				if (!result.Decision.Pinned)
				{
					var decision = result.Decision;

					await _runCardDecisionsRepository.SaveOrUpdateAsync(new RunCardDecision
					{
						DepartmentId = call.DepartmentId,
						CallId = call.CallId,
						RunCardId = result.MatchedRunCardId.Value,
						AlarmLevel = result.AlarmLevel,
						LeafNodeKey = Truncate(decision.OutcomeNodeKey, 36),
						LeafLabel = Truncate(decision.OutcomeLabel ?? decision.PackageName, 200),
						ResponsePackageId = decision.PackageId,
						PackageSnapshotJson = JsonConvert.SerializeObject(decision.ResolvedRows ?? new List<ResolvedRequirementRow>()),
						PathJson = JsonConvert.SerializeObject(WithoutRows(decision)),
						Source = (int)(decision.FromPreview ? RunCardDecisionSources.DispatcherPreview : source),
						CreatedOn = DateTime.UtcNow,
						CreatedByUserId = createdByUserId
					}, cancellationToken, true);

					decision.Pinned = true;
					decision.PinnedOn = DateTime.UtcNow;
					decision.PinSource = decision.FromPreview ? RunCardDecisionSources.DispatcherPreview : source;
				}

				await _outstandingRequirementsService.CreateFromRecommendationAsync(call, result, createdByUserId, cancellationToken);
			}
			catch (Exception ex)
			{
				// The call is saved already; a missing pin only means add-resources re-walks the graph.
				Logging.LogException(ex, $"Recording the run card decision for call {call.CallId} failed.");
			}
		}

		#endregion

		#region Positions and anchor

		private async Task PrepareConditionalPositionsAsync(RecommendationContext context, ConditionalState state)
		{
			var anchor = context.CallLocation;

			if (anchor == null && context.Card.HomeStationGroupId.HasValue)
			{
				var homeStation = await _departmentGroupsService.GetGroupByIdAsync(context.Card.HomeStationGroupId.Value, false);
				anchor = await _geoService.GetStationCoordinatesAsync(homeStation);

				if (anchor != null)
					context.Result.Notes.Add($"Call has no location; measuring from the run card's home station '{homeStation?.Name}'.");
			}

			state.Anchor = anchor;

			if (anchor == null)
			{
				context.Result.Notes.Add("Call has no usable location and the run card has no home station; response times cannot be measured.");
				return;
			}

			await AttachUnitLocationsAsync(context);
			AttachPersonnelLocations(context, await _personnelLocationResolver.GetLatestLocationsAsync(
				context.Request.DepartmentId, context.Config.PersonnelMaxLocationAgeSeconds, context.Now));

			if (state.Mode != DispatchRecommendationModes.StationBased)
				return;

			var stations = await _geoService.OrderStationsByDistanceAsync(context.Request.DepartmentId, anchor.Value.Latitude, anchor.Value.Longitude);

			if (stations == null || !stations.Any())
			{
				context.Result.Notes.Add("No station groups with usable coordinates or geofences exist; station-based selection cannot run.");
				return;
			}

			state.Stations = stations.Where(s => s.ContainsPoint).Concat(stations.Where(s => !s.ContainsPoint)).ToList();

			if (context.CallLocation != null && !stations.Any(s => s.ContainsPoint))
				context.Result.Notes.Add("Call location is not inside any station's response area; filling from the nearest station outward.");

			var dispatchShift = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(context.Request.DepartmentId);
			state.Rosters = await BuildStationRostersAsync(context, state.Stations, dispatchShift);
		}

		#endregion

		#region Rows

		private async Task<List<ResolvedRequirementRow>> ResolveRowsAsync(RecommendationContext context, ConditionalState state)
		{
			var level = context.Level;
			var decision = state.Decision;
			var rows = new List<ResolvedRequirementRow>();
			var sortOrder = 0;

			// A graph level's fixed rows are "always" rows, sent with whatever the graph picks; a fixed level's rows are
			// filled beside its package.
			foreach (var requirement in (level.UnitRequirements ?? new List<RunCardUnitRequirement>()).OrderBy(r => r.SortOrder))
			{
				rows.Add(new ResolvedRequirementRow
				{
					Scope = RequirementScopes.Level,
					RequirementId = requirement.RunCardUnitRequirementId,
					Label = NameOrNull(context.UnitTypeNames, requirement.UnitTypeId) ?? "Unit",
					RequiredCount = requirement.RequiredCount,
					SortOrder = sortOrder++,
					Targets = new List<RunCardTarget> { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.UnitType, TargetRefId = requirement.UnitTypeId } }
				});
			}

			foreach (var requirement in (level.RoleRequirements ?? new List<RunCardRoleRequirement>()).OrderBy(r => r.SortOrder))
			{
				rows.Add(new ResolvedRequirementRow
				{
					Scope = RequirementScopes.Level,
					RequirementId = requirement.RunCardRoleRequirementId,
					Label = NameOrNull(context.RoleNames, requirement.PersonnelRoleId) ?? "Role",
					RequiredCount = requirement.RequiredCount,
					SortOrder = sortOrder++,
					Targets = new List<RunCardTarget> { new RunCardTarget { TargetKind = (int)RunCardTargetKinds.PersonnelRole, TargetRefId = requirement.PersonnelRoleId } }
				});
			}

			if (level.ResponsePackageId.HasValue)
			{
				var package = AddPackageRows(context, state, rows, level.ResponsePackageId.Value, ref sortOrder);

				if (!decision.LevelHasGraph && package != null)
				{
					decision.PackageId = package.ResponsePackageId;
					decision.PackageName = package.Name;
				}
			}

			if (!decision.LevelHasGraph)
				return rows;

			var outcome = await WalkGraphAsync(context, state);

			if (outcome == null)
			{
				context.Result.Notes.Add("The decision graph did not reach an outcome; only the level's always rows apply.");
				return rows;
			}

			decision.OutcomeNodeKey = outcome.NodeKey;
			decision.OutcomeLabel = outcome.Label;
			decision.AdviceText = string.IsNullOrWhiteSpace(outcome.AdviceText) ? null : outcome.AdviceText.Trim();

			if (outcome.ResponsePackageId.HasValue && outcome.ResponsePackageId == level.ResponsePackageId)
			{
				// The outcome names the package the level always sends: its rows are already in, and sending them twice
				// would ask for every resource twice.
				decision.PackageId = level.ResponsePackageId;
				decision.PackageName = state.Packages.TryGetValue(level.ResponsePackageId.Value, out var levelPackage) ? levelPackage.Name : null;
			}
			else if (outcome.ResponsePackageId.HasValue)
			{
				var package = AddPackageRows(context, state, rows, outcome.ResponsePackageId.Value, ref sortOrder);

				if (package != null)
				{
					decision.PackageId = package.ResponsePackageId;
					decision.PackageName = package.Name;
				}
			}
			else if (decision.AdviceText != null)
			{
				decision.Outcome = RecommendationOutcomes.Advice;
			}

			return rows;
		}

		private ResponsePackage AddPackageRows(RecommendationContext context, ConditionalState state, List<ResolvedRequirementRow> rows, int responsePackageId, ref int sortOrder)
		{
			if (!state.Packages.TryGetValue(responsePackageId, out var package))
			{
				context.Result.Notes.Add($"Response package {responsePackageId} no longer exists; its rows were skipped.");
				return null;
			}

			foreach (var row in (package.Rows ?? new List<ResponsePackageRow>()).Where(r => r != null).OrderBy(r => r.SortOrder))
			{
				var resolved = ResolvePackageRow(context, package, row, sortOrder++);

				if (row.SubstitutePackageId.HasValue && row.SubstitutePackageId.Value != package.ResponsePackageId
					&& state.Packages.TryGetValue(row.SubstitutePackageId.Value, out var substitute))
				{
					resolved.SubstitutePackageName = substitute.Name;
					var substituteOrder = 0;

					foreach (var substituteRow in (substitute.Rows ?? new List<ResponsePackageRow>())
						.Where(r => r != null && r.FillMode == (int)RequirementFillModes.Count).OrderBy(r => r.SortOrder))
					{
						// One level only: a substitute's own substitutes are not followed.
						var resolvedSubstitute = ResolvePackageRow(context, substitute, substituteRow, substituteOrder++);
						resolvedSubstitute.SubstitutePackageId = null;
						resolvedSubstitute.KeepOutstanding = false;
						resolved.SubstituteRows.Add(resolvedSubstitute);
					}
				}

				rows.Add(resolved);
			}

			return package;
		}

		private ResolvedRequirementRow ResolvePackageRow(RecommendationContext context, ResponsePackage package, ResponsePackageRow row, int sortOrder)
		{
			var targets = (row.Targets ?? new List<RunCardTarget>()).Where(t => t != null).Select(t => t.Copy()).ToList();

			return new ResolvedRequirementRow
			{
				Scope = RequirementScopes.Package,
				RequirementId = row.ResponsePackageRowId,
				ResponsePackageId = package.ResponsePackageId,
				Label = string.IsNullOrWhiteSpace(row.Label) ? DescribeTargets(context, targets) : row.Label.Trim(),
				FillMode = row.FillMode == (int)RequirementFillModes.EnsureFirstArriving ? RequirementFillModes.EnsureFirstArriving : RequirementFillModes.Count,
				RequiredCount = Math.Max(1, row.RequiredCount),
				StatusFilter = Enum.IsDefined(typeof(RunCardStatusFilters), row.StatusFilter) ? (RunCardStatusFilters)row.StatusFilter : RunCardStatusFilters.AnyAvailable,
				StatusBaseTypes = row.StatusBaseTypes,
				ResponseCodeId = row.ResponseCodeId,
				SubstitutePackageId = row.SubstitutePackageId,
				KeepOutstanding = row.KeepOutstanding,
				SortOrder = sortOrder,
				Targets = targets
			};
		}

		private async Task<List<ResolvedRequirementRow>> TryUsePinnedDecisionAsync(RecommendationContext context, ConditionalState state)
		{
			var decisions = await _runCardDecisionsRepository.GetDecisionsByCallIdAsync(context.Request.CallId.Value);

			var pin = decisions?
				.Where(d => d != null && d.DepartmentId == context.Request.DepartmentId && d.RunCardId == context.Card.RunCardId && d.AlarmLevel == context.Level.AlarmLevel)
				.OrderByDescending(d => d.CreatedOn)
				.ThenByDescending(d => d.RunCardDecisionId)
				.FirstOrDefault();

			if (pin == null || string.IsNullOrWhiteSpace(pin.PackageSnapshotJson))
				return null;

			List<ResolvedRequirementRow> rows;
			RunCardDecisionExplanation pinned = null;

			try
			{
				rows = JsonConvert.DeserializeObject<List<ResolvedRequirementRow>>(pin.PackageSnapshotJson);

				if (!string.IsNullOrWhiteSpace(pin.PathJson))
					pinned = JsonConvert.DeserializeObject<RunCardDecisionExplanation>(pin.PathJson);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Pinned run card decision {pin.RunCardDecisionId} could not be read; walking the graph fresh.");
				return null;
			}

			if (rows == null)
				return null;

			var decision = state.Decision;
			decision.Pinned = true;
			decision.PinnedOn = pin.CreatedOn;
			decision.PinSource = Enum.IsDefined(typeof(RunCardDecisionSources), pin.Source) ? (RunCardDecisionSources)pin.Source : RunCardDecisionSources.SaveTime;
			decision.OutcomeNodeKey = pin.LeafNodeKey;
			decision.OutcomeLabel = pin.LeafLabel;
			decision.PackageId = pin.ResponsePackageId;

			if (pinned != null)
			{
				decision.Outcome = pinned.Outcome;
				decision.OutcomeLabel = pinned.OutcomeLabel ?? pin.LeafLabel;
				decision.PackageName = pinned.PackageName;
				decision.AdviceText = pinned.AdviceText;
				decision.Uncertain = pinned.Uncertain;
				decision.Path = pinned.Path ?? new List<RunCardConditionExplanation>();
			}

			context.Result.Notes.Add($"Using the decision pinned on {pin.CreatedOn:yyyy-MM-dd HH:mm} UTC (outcome '{decision.OutcomeLabel ?? decision.PackageName}'); re-evaluate to walk the graph again.");

			return rows;
		}

		#endregion

		#region Decision graph

		private async Task<RunCardDecisionNode> WalkGraphAsync(RecommendationContext context, ConditionalState state)
		{
			var nodes = (context.Level.DecisionNodes ?? new List<RunCardDecisionNode>()).Where(n => n != null && !string.IsNullOrWhiteSpace(n.NodeKey)).ToList();
			var byKey = nodes.GroupBy(n => n.NodeKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var forcedKey = context.Request.ForcedLeafNodeKey;
			if (!string.IsNullOrWhiteSpace(forcedKey) && byKey.TryGetValue(forcedKey.Trim(), out var forced) && forced.IsOutcome)
			{
				state.Decision.FromPreview = true;
				context.Result.Notes.Add($"Outcome '{forced.Label}' is the one shown on the New Call preview.");
				return forced;
			}

			var node = RunCardDecisionGraph.Root(nodes);

			if (node == null)
			{
				context.Result.Notes.Add("The decision graph has no single start node.");
				return null;
			}

			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			while (node != null && node.IsCondition)
			{
				if (!visited.Add(node.NodeKey) || visited.Count > 64)
				{
					context.Result.Notes.Add("The decision graph loops; no outcome was reached.");
					return null;
				}

				var condition = await EvaluateConditionAsync(context, state, node);
				state.Decision.Path.Add(condition);

				if (condition.Uncertain)
					state.Decision.Uncertain = true;

				context.Result.Notes.Add(DescribeCondition(condition));

				var nextKey = condition.Result == RunCardConditionResults.Yes ? node.YesNodeKey : node.NoNodeKey;
				node = !string.IsNullOrWhiteSpace(nextKey) && byKey.TryGetValue(nextKey, out var next) ? next : null;
			}

			if (node == null)
			{
				context.Result.Notes.Add("A decision branch leads to no node.");
				return null;
			}

			context.Result.Notes.Add(string.IsNullOrWhiteSpace(node.AdviceText)
				? $"Outcome: {node.Label}."
				: $"Outcome: {node.Label}. Advice: {node.AdviceText.Trim()}");

			return node;
		}

		private async Task<RunCardConditionExplanation> EvaluateConditionAsync(RecommendationContext context, ConditionalState state, RunCardDecisionNode node)
		{
			var conditionType = (RunCardConditionTypes)node.ConditionType;
			var subjectFilter = ParseFilter(node.SubjectStatusFilter);
			var requiredCount = Math.Max(1, node.RequiredCount);

			var condition = new RunCardConditionExplanation
			{
				NodeKey = node.NodeKey,
				Label = node.Label,
				ConditionType = conditionType,
				ThresholdSeconds = node.ThresholdSeconds,
				RequiredCount = requiredCount,
				Result = RunCardConditionResults.No
			};

			var subject = await RankAsync(context, state, node.SubjectTargets, subjectFilter, node.SubjectStatusBaseTypes,
				conditionType == RunCardConditionTypes.AvailableWithin ? requiredCount : 1);

			if (conditionType == RunCardConditionTypes.ArrivesBefore)
			{
				var referenceFilter = ParseFilter(node.ReferenceStatusFilter);
				var reference = await RankAsync(context, state, node.ReferenceTargets, referenceFilter, node.ReferenceStatusBaseTypes, 1);
				var s = subject.Ranked.FirstOrDefault();
				var r = reference.Ranked.FirstOrDefault();
				var margin = Math.Max(0, node.ThresholdSeconds ?? 0);

				// Like for like: routed only when both picks have a route, otherwise both on the estimate.
				var routed = s?.RoutedEtaSeconds != null && r?.RoutedEtaSeconds != null;
				var sSeconds = s == null ? null : (routed ? s.ResponseSeconds : s.EstimatedSeconds);
				var rSeconds = r == null ? null : (routed ? r.ResponseSeconds : r.EstimatedSeconds);
				condition.TimeSource = routed ? ResponseTimeSources.Routed : ResponseTimeSources.Estimated;

				if (s == null)
				{
					condition.Result = RunCardConditionResults.No;
					condition.Note = "Nothing matching the first resource is available.";
				}
				else if (r == null)
				{
					condition.Result = RunCardConditionResults.Yes;
					condition.Note = "Nothing matching the compared resource is available.";
				}
				else if (!sSeconds.HasValue || !rSeconds.HasValue)
				{
					condition.Result = RunCardConditionResults.No;
					condition.Uncertain = true;
					condition.Note = "Response times could not be measured.";
				}
				else
				{
					condition.MarginSeconds = rSeconds.Value - sSeconds.Value;
					condition.Result = sSeconds.Value + margin <= rSeconds.Value ? RunCardConditionResults.Yes : RunCardConditionResults.No;
				}

				// A resource left out for its position could only be faster than the pick it would have displaced.
				if ((condition.Result == RunCardConditionResults.Yes && reference.ExcludedForData > 0)
					|| (condition.Result == RunCardConditionResults.No && subject.ExcludedForData > 0))
					condition.Uncertain = true;

				condition.Subject = await DescribeOperandAsync(context, state, node.SubjectTargets, subjectFilter, subject, s, sSeconds, condition.TimeSource);
				condition.Reference = await DescribeOperandAsync(context, state, node.ReferenceTargets, referenceFilter, reference, r, rSeconds, condition.TimeSource);
			}
			else if (conditionType == RunCardConditionTypes.AvailableWithin)
			{
				var limit = Math.Max(0, node.ThresholdSeconds ?? 0);
				var picks = subject.Ranked.Take(requiredCount).ToList();

				condition.TimeSource = picks.Any() && picks.All(p => p.RoutedEtaSeconds.HasValue) ? ResponseTimeSources.Routed : ResponseTimeSources.Estimated;

				if (picks.Count < requiredCount)
				{
					condition.Result = RunCardConditionResults.No;
					condition.Note = $"{picks.Count} of {requiredCount} matching resource(s) available.";
				}
				else if (picks.Any(p => !p.ResponseSeconds.HasValue))
				{
					condition.Result = RunCardConditionResults.No;
					condition.Uncertain = true;
					condition.Note = "Response times could not be measured.";
				}
				else
				{
					var slowest = picks.Max(p => p.ResponseSeconds.Value);
					condition.MarginSeconds = limit - slowest;
					condition.Result = picks.All(p => p.ResponseSeconds.Value < limit) ? RunCardConditionResults.Yes : RunCardConditionResults.No;
				}

				if (condition.Result == RunCardConditionResults.No && subject.ExcludedForData > 0)
					condition.Uncertain = true;

				var last = picks.LastOrDefault();
				condition.Subject = await DescribeOperandAsync(context, state, node.SubjectTargets, subjectFilter, subject, last, last?.ResponseSeconds, condition.TimeSource);
			}
			else
			{
				condition.Note = "Unknown condition type; treated as No.";
			}

			return condition;
		}

		#endregion

		#region Ranking

		private async Task<ResourceRanking> RankAsync(RecommendationContext context, ConditionalState state, IEnumerable<RunCardTarget> targets,
			RunCardStatusFilters filter, string statusBaseTypes, int needed)
		{
			var targetList = (targets ?? Enumerable.Empty<RunCardTarget>()).Where(t => t != null).ToList();
			var baseTypes = filter == RunCardStatusFilters.BaseTypes ? RunCardDecisionGraph.ParseBaseTypes(statusBaseTypes) : new HashSet<int>();
			var key = string.Join(",", targetList.Select(t => $"{t.TargetKind}:{t.TargetRefId}").OrderBy(x => x, StringComparer.Ordinal))
				+ "|" + (int)filter + "|" + string.Join(",", baseTypes.OrderBy(x => x));

			if (!state.Rankings.TryGetValue(key, out var ranking))
			{
				ranking = BuildRanking(context, state, targetList, filter, baseTypes);
				state.Rankings[key] = ranking;
			}

			await EnsureRoutedAsync(context, state, ranking, needed);

			return ranking;
		}

		private ResourceRanking BuildRanking(RecommendationContext context, ConditionalState state, List<RunCardTarget> targets,
			RunCardStatusFilters filter, HashSet<int> baseTypes)
		{
			var ranking = new ResourceRanking();
			var anchor = state.Anchor;
			var closest = state.Mode != DispatchRecommendationModes.StationBased;
			var stationIndex = new Dictionary<int, int>();

			for (var i = 0; i < state.Stations.Count; i++)
				stationIndex[state.Stations[i].Station.DepartmentGroupId] = i;

			if (RunCardTargetRules.HasUnitTargets(targets))
			{
				foreach (var candidate in context.UnitCandidates)
				{
					if (!RunCardTargetRules.MatchesUnit(targets, candidate.UnitTypeId, candidate.UnitTypeClass))
						continue;

					ranking.Matching++;

					if (!UnitMatchesStatusFilter(candidate, filter, baseTypes))
						continue;

					ranking.MatchingFilter++;

					var entry = new RankedResource
					{
						Unit = candidate,
						TurnoutSeconds = candidate.TurnoutSeconds,
						InRestPeriod = IsUnitInRestPeriod(context, candidate.Unit.UnitId),
						Stale = candidate.LocationIsStale,
						StationGroupId = candidate.Unit.StationGroupId,
						StationGroupName = candidate.Unit.StationGroup?.Name
					};

					if (anchor.HasValue && candidate.Latitude.HasValue && candidate.Longitude.HasValue)
					{
						entry.Distance = GeoMath.HaversineMeters(anchor.Value.Latitude, anchor.Value.Longitude, candidate.Latitude.Value, candidate.Longitude.Value);
						entry.EstimatedSeconds = candidate.TurnoutSeconds + UnitResponseOrigin.EstimateTravelSeconds(entry.Distance.Value);
					}

					if (!PlaceEntry(context, state, ranking, entry, closest, stationIndex, candidate.Unit.StationGroupId.HasValue
						? new[] { candidate.Unit.StationGroupId.Value } : new int[0]))
						continue;

					ranking.Ranked.Add(entry);
				}
			}

			if (RunCardTargetRules.HasPersonTargets(targets))
			{
				var personnelTurnout = Math.Max(0, context.Config.PersonnelTurnoutSeconds);

				foreach (var candidate in context.PersonnelCandidates)
				{
					if (!RunCardTargetRules.MatchesPerson(targets, candidate.RoleIds))
						continue;

					// Status filters are unit status meanings; people pass every filter.
					ranking.Matching++;
					ranking.MatchingFilter++;

					var entry = new RankedResource
					{
						Person = candidate,
						TurnoutSeconds = personnelTurnout,
						InRestPeriod = IsUserInRestPeriod(context, candidate.UserId),
						Stale = candidate.LocationIsStale,
						StationGroupId = candidate.StationGroupId,
						StationGroupName = candidate.StationGroupName
					};

					if (anchor.HasValue && candidate.Latitude.HasValue && candidate.Longitude.HasValue)
					{
						entry.Distance = GeoMath.HaversineMeters(anchor.Value.Latitude, anchor.Value.Longitude, candidate.Latitude.Value, candidate.Longitude.Value);
						entry.EstimatedSeconds = personnelTurnout + UnitResponseOrigin.EstimateTravelSeconds(entry.Distance.Value);
					}

					var rosterStations = state.Rosters.Where(r => r.Value.Contains(candidate.UserId)).Select(r => r.Key).ToArray();

					if (!PlaceEntry(context, state, ranking, entry, closest, stationIndex, rosterStations))
						continue;

					ranking.Ranked.Add(entry);
				}
			}

			ranking.Ranked = SortRanked(state, ranking.Ranked);

			return ranking;
		}

		/// <summary>
		/// Applies the mode's placement rules: closest-unit needs a fresh enough position inside the radius; station-based
		/// needs a station in the cascade (the unit's station, or a roster the person is on).
		/// </summary>
		private static bool PlaceEntry(RecommendationContext context, ConditionalState state, ResourceRanking ranking, RankedResource entry,
			bool closest, Dictionary<int, int> stationIndex, IEnumerable<int> stationGroupIds)
		{
			if (!state.Anchor.HasValue)
				return false;

			if (closest)
			{
				if (!entry.Distance.HasValue)
				{
					ranking.ExcludedForData++;
					return false;
				}

				if (entry.Stale && !context.Config.IncludeStaleLocations)
				{
					ranking.ExcludedForData++;
					return false;
				}

				if (context.Config.MaxRadiusMeters > 0 && entry.Distance.Value > context.Config.MaxRadiusMeters)
				{
					ranking.OutsideRadius++;
					return false;
				}

				return true;
			}

			var best = -1;
			foreach (var stationGroupId in stationGroupIds)
			{
				if (stationIndex.TryGetValue(stationGroupId, out var index) && (best < 0 || index < best))
					best = index;
			}

			if (best < 0)
				return false;

			var station = state.Stations[best];
			entry.CascadeDepth = best;
			entry.StationGroupId = station.Station.DepartmentGroupId;
			entry.StationGroupName = station.Station.Name;
			entry.InContainingStation = station.ContainsPoint;
			entry.StationDistance = station.DistanceMeters;

			return true;
		}

		private static List<RankedResource> SortRanked(ConditionalState state, IEnumerable<RankedResource> entries)
		{
			var ordered = entries.OrderBy(e => e.InRestPeriod ? 1 : 0);

			if (state.Mode == DispatchRecommendationModes.StationBased)
				ordered = ordered.ThenBy(e => e.CascadeDepth);

			if (state.ByDistance)
				return ordered
					.ThenBy(e => e.Distance.HasValue ? 0 : 1)
					.ThenBy(e => e.Distance ?? 0)
					.ThenBy(e => e.ResponseSeconds ?? double.MaxValue)
					.ThenBy(e => e.SortName, StringComparer.OrdinalIgnoreCase)
					.ToList();

			return ordered
				.ThenBy(e => e.ResponseSeconds.HasValue ? 0 : 1)
				.ThenBy(e => e.ResponseSeconds ?? 0)
				.ThenBy(e => e.Distance ?? double.MaxValue)
				.ThenBy(e => e.SortName, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>
		/// Routed lookups for the leading entries (at least the department's shortlist size, more when a row needs more),
		/// cached per resource so overlapping target sets share them, then the looked-up head is re-ranked. A failed
		/// lookup keeps its estimate.
		/// </summary>
		private async Task EnsureRoutedAsync(RecommendationContext context, ConditionalState state, ResourceRanking ranking, int needed)
		{
			// Drive times only re-rank, so ranking by distance makes no lookups.
			if (!context.Config.UseRoutedEta || state.ByDistance || !state.Anchor.HasValue || ranking.Ranked.Count == 0)
				return;

			var shortlist = Math.Min(ranking.Ranked.Count, Math.Max(Math.Max(1, context.Config.EtaShortlistSize), needed));

			if (shortlist <= ranking.RoutedThrough)
				return;

			var anchor = state.Anchor.Value;

			for (var i = 0; i < shortlist; i++)
			{
				var entry = ranking.Ranked[i];

				if (entry.RoutedLookedUp)
					continue;

				entry.RoutedLookedUp = true;

				double? latitude = entry.IsPerson ? entry.Person.Latitude : entry.Unit.Latitude;
				double? longitude = entry.IsPerson ? entry.Person.Longitude : entry.Unit.Longitude;

				if (!latitude.HasValue || !longitude.HasValue)
					continue;

				double? eta;
				var cached = entry.IsPerson
					? state.UserEtas.TryGetValue(entry.Person.UserId, out eta)
					: state.UnitEtas.TryGetValue(entry.Unit.Unit.UnitId, out eta);

				if (!cached)
				{
					context.CancellationToken.ThrowIfCancellationRequested();

					var seconds = await _geoService.GetEtaInSecondsAsync(FormatPoint(latitude.Value, longitude.Value), FormatPoint(anchor.Latitude, anchor.Longitude));
					eta = seconds >= 0 ? seconds : (double?)null;

					if (entry.IsPerson)
						state.UserEtas[entry.Person.UserId] = eta;
					else
						state.UnitEtas[entry.Unit.Unit.UnitId] = eta;
				}

				entry.RoutedEtaSeconds = eta;
			}

			ranking.Ranked = SortRanked(state, ranking.Ranked.Take(shortlist)).Concat(ranking.Ranked.Skip(shortlist)).ToList();
			ranking.RoutedThrough = shortlist;
		}

		private static bool UnitMatchesStatusFilter(UnitCandidate candidate, RunCardStatusFilters filter, HashSet<int> baseTypes)
		{
			switch (filter)
			{
				case RunCardStatusFilters.InQuarters:
					return candidate.InQuarters;
				case RunCardStatusFilters.AwayFromQuarters:
					return !candidate.InQuarters;
				case RunCardStatusFilters.BaseTypes:
					return baseTypes != null && baseTypes.Contains(candidate.BaseType);
				default:
					return true;
			}
		}

		private static RunCardStatusFilters ParseFilter(int value)
		{
			return Enum.IsDefined(typeof(RunCardStatusFilters), value) ? (RunCardStatusFilters)value : RunCardStatusFilters.AnyAvailable;
		}

		#endregion

		#region Fill

		private async Task FillResolvedRowsAsync(RecommendationContext context, ConditionalState state, List<ResolvedRequirementRow> rows)
		{
			var countRows = rows.Where(r => r.FillMode != RequirementFillModes.EnsureFirstArriving).OrderBy(r => r.SortOrder).ToList();
			var firstArrivingRows = rows.Where(r => r.FillMode == RequirementFillModes.EnsureFirstArriving).OrderBy(r => r.SortOrder).ToList();
			var shortRows = new List<Tuple<ResolvedRequirementRow, RowFill>>();

			// 1-2. Configured, then relaxed, for every primary row first, so primary rows get first pick.
			foreach (var row in countRows)
			{
				var needed = Math.Max(0, row.RequiredCount) - CountOnCall(context, state, row.Targets, row.RequiredCount, row.Label);

				if (needed <= 0)
					continue;

				state.AnythingNeeded = true;

				var fill = await FillCountRowAsync(context, state, row, needed, null, row.ResponseCodeId);

				if (fill.Relaxed > 0)
				{
					state.Decision.Fallbacks.Add(new RunCardFallbackExplanation
					{
						RowLabel = row.Label,
						RequirementScope = row.Scope,
						RequirementId = row.RequirementId,
						Step = RequirementFallbackSteps.Relaxed,
						RequiredCount = needed,
						FilledCount = fill.Filled
					});
					context.Result.Notes.Add($"'{row.Label}': {fill.Relaxed} picked with the status filter relaxed.");
				}

				if (fill.Filled < needed)
					shortRows.Add(Tuple.Create(row, fill));
			}

			// 3. Substitutes, then 4. shortfalls.
			foreach (var pair in shortRows)
			{
				var row = pair.Item1;
				var fill = pair.Item2;
				var missing = fill.Needed - fill.Filled;
				var substituted = false;
				string sentInstead = null;

				if (row.SubstituteRows != null && row.SubstituteRows.Any())
				{
					var complete = true;
					var sent = new List<string>();

					foreach (var substituteRow in row.SubstituteRows.OrderBy(r => r.SortOrder))
					{
						var substituteNeeded = Math.Max(1, substituteRow.RequiredCount) * missing;
						substituteNeeded -= CountOnCall(context, state, substituteRow.Targets, substituteNeeded, substituteRow.Label);

						if (substituteNeeded <= 0)
							continue;

						// A substitute row with no code of its own carries the code of the row it stands in for.
						var substituteFill = await FillCountRowAsync(context, state, substituteRow, substituteNeeded,
							RecommendationSelectionReasons.SubstitutePackage, substituteRow.ResponseCodeId ?? row.ResponseCodeId, row);

						sent.AddRange(await PickedNamesAsync(context, state, row, substituteRow));

						if (substituteFill.Filled < substituteNeeded)
							complete = false;
					}

					substituted = complete;
					sentInstead = sent.Any() ? string.Join(", ", sent.Distinct()) : null;

					state.Decision.Fallbacks.Add(new RunCardFallbackExplanation
					{
						RowLabel = row.Label,
						RequirementScope = row.Scope,
						RequirementId = row.RequirementId,
						Step = RequirementFallbackSteps.Substitute,
						SubstitutePackageName = row.SubstitutePackageName,
						SentInstead = sentInstead,
						Outstanding = row.KeepOutstanding,
						RequiredCount = fill.Needed,
						FilledCount = fill.Filled
					});

					context.Result.Notes.Add($"'{row.Label}': {missing} short; substitute '{row.SubstitutePackageName}'"
						+ (sentInstead != null ? $" sent {sentInstead}" : " found nothing")
						+ (row.KeepOutstanding ? "; the call keeps awaiting it." : "."));
				}

				if (substituted)
					continue;

				AddConditionalShortfall(context, state, row, fill.Needed, fill.Filled, ShortfallReason(context, state, row, fill.Ranking));

				if (row.SubstituteRows == null || !row.SubstituteRows.Any())
				{
					state.Decision.Fallbacks.Add(new RunCardFallbackExplanation
					{
						RowLabel = row.Label,
						RequirementScope = row.Scope,
						RequirementId = row.RequirementId,
						Step = RequirementFallbackSteps.Shortfall,
						Outstanding = row.KeepOutstanding,
						RequiredCount = fill.Needed,
						FilledCount = fill.Filled
					});

					if (row.KeepOutstanding)
						context.Result.Notes.Add($"'{row.Label}': {missing} short; the call keeps awaiting it.");
				}
			}

			// Ensure first-arriving rows last: they add the earliest arrival only when no other row already chose it.
			foreach (var row in firstArrivingRows)
				await FillFirstArrivingRowAsync(context, state, row);
		}

		private async Task<RowFill> FillCountRowAsync(RecommendationContext context, ConditionalState state, ResolvedRequirementRow row, int needed,
			RecommendationSelectionReasons? reason, int? responseCodeId, ResolvedRequirementRow countsFor = null)
		{
			var fill = new RowFill { Needed = needed };

			fill.Ranking = await RankAsync(context, state, row.Targets, row.StatusFilter, row.StatusBaseTypes, needed + state.PickedUnitIds.Count + state.PickedUserIds.Count);
			fill.Configured = await TakeAsync(context, state, fill.Ranking, needed, row, countsFor, reason, responseCodeId);

			if (fill.Filled < needed && context.Card.RelaxUnfilledRows && row.StatusFilter != RunCardStatusFilters.AnyAvailable
				&& RunCardTargetRules.HasUnitTargets(row.Targets))
			{
				var relaxed = await RankAsync(context, state, row.Targets, RunCardStatusFilters.AnyAvailable, null,
					needed - fill.Filled + state.PickedUnitIds.Count + state.PickedUserIds.Count);
				fill.Relaxed = await TakeAsync(context, state, relaxed, needed - fill.Filled, row, countsFor,
					reason ?? RecommendationSelectionReasons.RelaxedFill, responseCodeId);
			}

			return fill;
		}

		private async Task<int> TakeAsync(RecommendationContext context, ConditionalState state, ResourceRanking ranking, int count,
			ResolvedRequirementRow row, ResolvedRequirementRow countsFor, RecommendationSelectionReasons? reason, int? responseCodeId)
		{
			var taken = 0;

			foreach (var entry in ranking.Ranked)
			{
				if (taken >= count)
					break;

				if (IsPicked(state, entry))
					continue;

				await AddConditionalRecommendationAsync(context, state, entry, row, countsFor, reason, responseCodeId);
				taken++;
			}

			return taken;
		}

		private async Task FillFirstArrivingRowAsync(RecommendationContext context, ConditionalState state, ResolvedRequirementRow row)
		{
			var onCallUnit = context.DispatchedUnitTypes.FirstOrDefault(x => x.Value != null && RunCardTargetRules.MatchesUnit(row.Targets, x.Value.UnitTypeId, x.Value.BaseType));
			var onCallPerson = context.DispatchedUserRoles.FirstOrDefault(x => RunCardTargetRules.MatchesPerson(row.Targets, (x.Value ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId)));

			if (onCallUnit.Value != null || onCallPerson.Key != null)
			{
				state.Decision.FirstArriving.Add(new RunCardFirstArrivingExplanation
				{
					RowLabel = row.Label,
					AlreadyIncluded = true,
					IsPerson = onCallUnit.Value == null
				});
				context.Result.Notes.Add($"'{row.Label}': a matching resource is already on the call.");
				return;
			}

			var ranking = await RankAsync(context, state, row.Targets, row.StatusFilter, row.StatusBaseTypes, 1);

			// Station order is not arrival order: the earliest arrival is the fastest response, rested resources first.
			var first = ranking.Ranked
				.OrderBy(e => e.InRestPeriod ? 1 : 0)
				.ThenBy(e => e.ResponseSeconds.HasValue ? 0 : 1)
				.ThenBy(e => e.ResponseSeconds ?? 0)
				.FirstOrDefault();

			if (first == null)
			{
				state.AnythingNeeded = true;
				AddConditionalShortfall(context, state, row, 1, 0, ShortfallReason(context, state, row, ranking));
				state.Decision.Fallbacks.Add(new RunCardFallbackExplanation
				{
					RowLabel = row.Label,
					RequirementScope = row.Scope,
					RequirementId = row.RequirementId,
					Step = RequirementFallbackSteps.Shortfall,
					Outstanding = row.KeepOutstanding,
					RequiredCount = 1
				});
				return;
			}

			var name = await ResourceNameAsync(context, state, first);
			var alreadyIncluded = IsPicked(state, first);

			state.Decision.FirstArriving.Add(new RunCardFirstArrivingExplanation
			{
				RowLabel = row.Label,
				ResourceName = name,
				IsPerson = first.IsPerson,
				ResponseSeconds = first.ResponseSeconds,
				AlreadyIncluded = alreadyIncluded
			});

			if (alreadyIncluded)
			{
				context.Result.Notes.Add($"'{row.Label}': first-arriving {name} ({FormatSeconds(first.ResponseSeconds)}) already included.");
				return;
			}

			state.AnythingNeeded = true;
			await AddConditionalRecommendationAsync(context, state, first, row, null, RecommendationSelectionReasons.FirstArriving, row.ResponseCodeId);
			context.Result.Notes.Add($"'{row.Label}': added {name} ({FormatSeconds(first.ResponseSeconds)}), arrives first.");
		}

		/// <summary>
		/// Add-resources: how many of a row's count the call already covers, taken from what is on the call in row order
		/// (each resource counted once). Zero for a new call.
		/// </summary>
		private static int CountOnCall(RecommendationContext context, ConditionalState state, List<RunCardTarget> targets, int requiredCount, string label)
		{
			if (!context.Request.CountDispatchedTowardRequirements || requiredCount <= 0)
				return 0;

			var units = state.DispatchedUnitPool
				.Where(x => x.Value != null && RunCardTargetRules.MatchesUnit(targets, x.Value.UnitTypeId, x.Value.BaseType))
				.Take(requiredCount)
				.ToList();
			state.DispatchedUnitPool.RemoveAll(x => units.Any(u => u.Key == x.Key));

			var people = state.DispatchedUserPool
				.Where(x => RunCardTargetRules.MatchesPerson(targets, (x.Value ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId)))
				.Take(requiredCount - units.Count)
				.ToList();
			state.DispatchedUserPool.RemoveAll(x => people.Any(p => string.Equals(p.Key, x.Key, StringComparison.OrdinalIgnoreCase)));

			var covered = units.Count + people.Count;

			if (covered > 0)
				context.Result.Notes.Add($"{covered} of {requiredCount} '{label}' already on the call.");

			return covered;
		}

		private static bool IsPicked(ConditionalState state, RankedResource entry)
		{
			return entry.IsPerson ? state.PickedUserIds.Contains(entry.Person.UserId) : state.PickedUnitIds.Contains(entry.Unit.Unit.UnitId);
		}

		private async Task AddConditionalRecommendationAsync(RecommendationContext context, ConditionalState state, RankedResource entry,
			ResolvedRequirementRow row, ResolvedRequirementRow countsFor, RecommendationSelectionReasons? reason, int? responseCodeId)
		{
			var code = responseCodeId.HasValue && state.Codes.Codes != null && state.Codes.Codes.Any() ? state.Codes.Find(responseCodeId) : null;
			var selection = reason ?? (entry.InRestPeriod ? RecommendationSelectionReasons.RestPeriodOverridden : ModeReason(state, entry));
			// A substitute is grouped under the row it stands in for.
			var owner = countsFor ?? row;
			var stationMode = state.Mode == DispatchRecommendationModes.StationBased;

			if (entry.IsPerson)
			{
				var candidate = entry.Person;
				state.PickedUserIds.Add(candidate.UserId);

				var roleId = row.Targets.Where(t => t.TargetKind == (int)RunCardTargetKinds.PersonnelRole && t.TargetRefId.HasValue)
					.Select(t => t.TargetRefId.Value)
					.FirstOrDefault(id => candidate.RoleIds.Contains(id));

				context.Result.Personnel.Add(new PersonnelRecommendation
				{
					UserId = candidate.UserId,
					RoleId = roleId > 0 ? roleId : candidate.RoleIds.FirstOrDefault(),
					StationGroupId = entry.StationGroupId,
					StationGroupName = entry.StationGroupName,
					SelectionReason = selection,
					CascadeDepth = entry.CascadeDepth,
					DistanceMeters = stationMode ? entry.StationDistance : entry.Distance,
					EtaSeconds = entry.RoutedEtaSeconds,
					ResponseSeconds = entry.ResponseSeconds,
					LocationTimestamp = candidate.LocationTimestamp,
					LocationIsStale = candidate.LocationIsStale,
					CurrentStatusText = candidate.StatusText,
					SatisfiesRequirementId = owner.RequirementId,
					RequirementScope = owner.Scope,
					RowLabel = row == owner ? row.Label : $"{row.Label} (for {owner.Label})",
					ResponseCodeId = code?.Id,
					ResponseCode = code?.Code,
					ResponseCodeName = code?.Name,
					ResponseCodeColor = code?.Color
				});
			}
			else
			{
				var candidate = entry.Unit;
				state.PickedUnitIds.Add(candidate.Unit.UnitId);

				context.Result.Units.Add(new UnitRecommendation
				{
					UnitId = candidate.Unit.UnitId,
					UnitName = candidate.Unit.Name,
					UnitTypeId = candidate.UnitTypeId,
					UnitTypeName = candidate.UnitTypeName,
					StationGroupId = entry.StationGroupId,
					StationGroupName = entry.StationGroupName,
					SelectionReason = selection,
					CascadeDepth = entry.CascadeDepth,
					DistanceMeters = stationMode ? entry.StationDistance : entry.Distance,
					EtaSeconds = entry.RoutedEtaSeconds,
					PositionSource = candidate.PositionSource,
					TurnoutSeconds = candidate.TurnoutSeconds,
					ResponseSeconds = entry.ResponseSeconds,
					LocationTimestamp = candidate.LocationTimestamp,
					LocationIsStale = candidate.LocationIsStale,
					CurrentStatusText = candidate.StatusText,
					CurrentStatusColor = candidate.StatusColor,
					CurrentStatusTextColor = candidate.StatusTextColor,
					StaffingLevel = candidate.StaffingLevel,
					SatisfiesRequirementId = owner.RequirementId,
					RequirementScope = owner.Scope,
					RowLabel = row == owner ? row.Label : $"{row.Label} (for {owner.Label})",
					ResponseCodeId = code?.Id,
					ResponseCode = code?.Code,
					ResponseCodeName = code?.Name,
					ResponseCodeColor = code?.Color
				});
			}

			if (entry.InRestPeriod)
				context.Result.Notes.Add($"{await ResourceNameAsync(context, state, entry)} is inside its rest period but was needed to fill '{row.Label}'.");
		}

		private static RecommendationSelectionReasons ModeReason(ConditionalState state, RankedResource entry)
		{
			if (state.Mode == DispatchRecommendationModes.StationBased)
				return entry.InContainingStation && entry.CascadeDepth == 0 ? RecommendationSelectionReasons.InGeofence : RecommendationSelectionReasons.CascadeStation;

			return entry.RoutedEtaSeconds.HasValue ? RecommendationSelectionReasons.ClosestByEta : RecommendationSelectionReasons.ClosestByDistance;
		}

		private RequirementShortfallReasons ShortfallReason(RecommendationContext context, ConditionalState state, ResolvedRequirementRow row, ResourceRanking ranking)
		{
			if (!state.Anchor.HasValue)
				return RequirementShortfallReasons.NoLocationData;

			if (ranking == null || ranking.Matching == 0)
			{
				var staffingExcluded = row.Targets.Any(t => t.TargetKind == (int)RunCardTargetKinds.AnyUnit && context.UnitTypesWithStaffingExclusions.Any()
					|| t.TargetKind == (int)RunCardTargetKinds.UnitType && t.TargetRefId.HasValue && context.UnitTypesWithStaffingExclusions.Contains(t.TargetRefId.Value));

				return staffingExcluded ? RequirementShortfallReasons.UnitsNotStaffed : RequirementShortfallReasons.NoCandidatesAvailable;
			}

			if (ranking.MatchingFilter == 0)
				return RequirementShortfallReasons.NoUnitMatchingStatusFilter;

			if (ranking.OutsideRadius > 0)
				return RequirementShortfallReasons.OutsideRadius;

			if (ranking.ExcludedForData > 0)
				return RequirementShortfallReasons.LocationsTooStale;

			return state.Mode == DispatchRecommendationModes.StationBased
				? RequirementShortfallReasons.StationsExhausted
				: RequirementShortfallReasons.NoCandidatesAvailable;
		}

		private static void AddConditionalShortfall(RecommendationContext context, ConditionalState state, ResolvedRequirementRow row, int required, int filled,
			RequirementShortfallReasons reason)
		{
			var single = row.Targets.Count == 1 && row.Targets[0].TargetRefId.HasValue
				&& (row.Targets[0].TargetKind == (int)RunCardTargetKinds.UnitType || row.Targets[0].TargetKind == (int)RunCardTargetKinds.PersonnelRole)
				? row.Targets[0].TargetRefId.Value
				: 0;

			context.Result.Shortfalls.Add(new RequirementShortfall
			{
				IsUnitRequirement = RunCardTargetRules.HasUnitTargets(row.Targets),
				RequirementId = row.RequirementId,
				RequirementScope = row.Scope,
				TypeOrRoleId = single,
				TypeOrRoleName = row.Label,
				AlarmLevel = context.Level.AlarmLevel,
				RequiredCount = required,
				FilledCount = filled,
				Reason = reason,
				Outstanding = row.KeepOutstanding
			});
		}

		#endregion

		#region Explanation

		private async Task<RunCardOperandExplanation> DescribeOperandAsync(RecommendationContext context, ConditionalState state, List<RunCardTarget> targets,
			RunCardStatusFilters filter, ResourceRanking ranking, RankedResource pick, double? seconds, ResponseTimeSources source)
		{
			return new RunCardOperandExplanation
			{
				TargetsLabel = DescribeTargets(context, targets),
				StatusFilter = filter,
				IsPerson = pick?.IsPerson ?? false,
				ResourceId = pick?.ResourceId,
				ResourceName = pick == null ? null : await ResourceNameAsync(context, state, pick),
				ResponseSeconds = seconds,
				TimeSource = source,
				PositionSource = pick?.Unit?.PositionSource ?? UnitPositionSources.Live,
				Stale = pick?.Stale ?? false,
				CandidateCount = ranking?.Ranked.Count ?? 0
			};
		}

		private async Task<string> ResourceNameAsync(RecommendationContext context, ConditionalState state, RankedResource entry)
		{
			if (!entry.IsPerson)
				return entry.Unit.Unit.Name;

			if (state.PersonNames == null)
			{
				state.PersonNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

				foreach (var person in (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(context.Request.DepartmentId) ?? new List<PersonName>()).Where(n => n != null))
				{
					if (!string.IsNullOrWhiteSpace(person.UserId) && !string.IsNullOrWhiteSpace(person.Name) && !state.PersonNames.ContainsKey(person.UserId))
						state.PersonNames[person.UserId] = person.Name.Trim();
				}
			}

			return state.PersonNames.TryGetValue(entry.Person.UserId, out var name) ? name : "A member";
		}

		/// <summary>Names of what a substitute row put in for a primary row, for "sent instead".</summary>
		private async Task<List<string>> PickedNamesAsync(RecommendationContext context, ConditionalState state, ResolvedRequirementRow primary, ResolvedRequirementRow substitute)
		{
			var names = context.Result.Units
				.Where(u => u.SatisfiesRequirementId == primary.RequirementId && u.RequirementScope == primary.Scope && u.SelectionReason == RecommendationSelectionReasons.SubstitutePackage)
				.Select(u => u.UnitName)
				.ToList();

			foreach (var person in context.Result.Personnel
				.Where(p => p.SatisfiesRequirementId == primary.RequirementId && p.RequirementScope == primary.Scope && p.SelectionReason == RecommendationSelectionReasons.SubstitutePackage))
			{
				if (state.PersonNames == null)
					await ResourceNameAsync(context, state, new RankedResource { Person = new PersonnelCandidate { UserId = person.UserId } });

				names.Add(state.PersonNames.TryGetValue(person.UserId, out var name) ? name : "A member");
			}

			return names;
		}

		private static string DescribeTargets(RecommendationContext context, IEnumerable<RunCardTarget> targets)
		{
			var described = RunCardTargetRules.Describe(targets, context.UnitTypeNames, context.RoleNames);
			return string.IsNullOrWhiteSpace(described) ? "Resource" : described;
		}

		private static string DescribeCondition(RunCardConditionExplanation condition)
		{
			var answer = condition.Result == RunCardConditionResults.Yes ? "Yes" : "No";
			var detail = new List<string>();

			if (condition.Subject?.ResourceName != null)
				detail.Add($"{condition.Subject.ResourceName} {FormatSeconds(condition.Subject.ResponseSeconds)}");

			if (condition.ConditionType == RunCardConditionTypes.ArrivesBefore && condition.Reference?.ResourceName != null)
				detail.Add($"vs {condition.Reference.ResourceName} {FormatSeconds(condition.Reference.ResponseSeconds)}");

			if (condition.ConditionType == RunCardConditionTypes.AvailableWithin && condition.ThresholdSeconds.HasValue)
				detail.Add($"limit {FormatSeconds(condition.ThresholdSeconds)}");

			var text = $"Condition '{condition.Label}': {answer}";

			if (detail.Any())
				text += $" ({string.Join(" ", detail)}, {(condition.TimeSource == ResponseTimeSources.Routed ? "routed" : "estimated")})";

			if (!string.IsNullOrWhiteSpace(condition.Note))
				text += $". {condition.Note}";

			if (condition.Uncertain)
				text += " Uncertain: a resource left out for its position could change this.";

			return text;
		}

		private static string FormatSeconds(double? seconds)
		{
			if (!seconds.HasValue)
				return "?";

			var total = (int)Math.Round(Math.Max(0, seconds.Value));
			return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
		}

		private static RunCardDecisionExplanation WithoutRows(RunCardDecisionExplanation decision)
		{
			return new RunCardDecisionExplanation
			{
				LevelHasGraph = decision.LevelHasGraph,
				FromPreview = decision.FromPreview,
				Uncertain = decision.Uncertain,
				IdealConfigurationUnavailable = decision.IdealConfigurationUnavailable,
				Outcome = decision.Outcome,
				OutcomeNodeKey = decision.OutcomeNodeKey,
				OutcomeLabel = decision.OutcomeLabel,
				PackageId = decision.PackageId,
				PackageName = decision.PackageName,
				AdviceText = decision.AdviceText,
				Path = decision.Path,
				Fallbacks = decision.Fallbacks,
				FirstArriving = decision.FirstArriving
			};
		}

		private static string Truncate(string value, int length)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= length)
				return value;

			return value.Substring(0, length);
		}

		#endregion
	}
}
