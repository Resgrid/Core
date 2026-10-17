using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>
	/// Status meanings (base types). Dispatch, availability, notifications and the apps read a status by its meaning, so an
	/// option without one is unknown to them, and a unit type whose status set has no option for the dispatch or release
	/// meaning keeps its status when its units are dispatched or released (Brockville, 2026-10-13). Status timers key on
	/// meanings too, so with timers set, a unit type dispatched to a status with no meaning, or a meaning with no timer, is
	/// never timed (2026-10-17). Counts only.
	/// </summary>
	public sealed class StatusConfigurationEvidenceSource(ICustomStateService customStates, IUnitsService units,
		IDepartmentSettingsService settings) : IAdminAssistEvidenceSource
	{
		public string SourceId => "StatusConfiguration";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { StatusConfigurationCheck.OptionsWithoutMeaning, StatusConfigurationCheck.UnitTypesWithoutStatusTarget,
			StatusConfigurationCheck.UnitTypesWithUntimedDispatchStatus };

		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var facts = await StatusConfigurationCheck.ReadAsync(actor.DepartmentId, customStates, units, settings, ct);
			return new[]
			{
				new ConfigurationEvidence(StatusConfigurationCheck.OptionsWithoutMeaning, EvidenceState.Known, SourceId, "1", now,
					Number: facts.SetsWithoutMeanings.Sum(x => x.Missing)),
				new ConfigurationEvidence(StatusConfigurationCheck.UnitTypesWithoutStatusTarget, EvidenceState.Known, SourceId, "1", now,
					Number: facts.UnitTypesWithoutTarget.Count),
				new ConfigurationEvidence(StatusConfigurationCheck.UnitTypesWithUntimedDispatchStatus, EvidenceState.Known, SourceId, "1", now,
					Number: facts.UntimedDispatchStatuses.Sum(x => x.UnitTypes.Count))
			};
		}
	}

	/// <summary>Names the status sets and unit types behind the status-meanings and unit-status-targets findings.</summary>
	public sealed class StatusConfigurationFindingSubjects(ICustomStateService customStates, IUnitsService units,
		IDepartmentSettingsService settings) : IAdminAssistFindingSubjectSource
	{
		public IReadOnlyList<string> RuleIds { get; } = new[] { StatusConfigurationCheck.StatusMeaningsRule, StatusConfigurationCheck.UnitStatusTargetsRule,
			StatusConfigurationCheck.UnitStatusTimersRule };

		public async Task<IReadOnlyList<FindingSubject>> ReadAsync(AdminAssistActor actor, string ruleId, CancellationToken ct)
		{
			if (!RuleIds.Contains(ruleId) || actor == null || actor.DepartmentId <= 0) return Array.Empty<FindingSubject>();
			var facts = await StatusConfigurationCheck.ReadAsync(actor.DepartmentId, customStates, units, settings, ct);

			if (ruleId == StatusConfigurationCheck.StatusMeaningsRule)
				return facts.SetsWithoutMeanings
					.Select(x => new FindingSubject(x.Set.CustomStateId.ToString(CultureInfo.InvariantCulture),
						Name(x.Set.Name, x.Set.CustomStateId) + " (" + x.Missing.ToString(CultureInfo.InvariantCulture) + ")"))
					.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
					.ToArray();

			// "PIT → Telefonisch": the unit type and the status its units get on dispatch.
			if (ruleId == StatusConfigurationCheck.UnitStatusTimersRule)
				return facts.UntimedDispatchStatuses
					.SelectMany(x => x.UnitTypes.Select(t => new FindingSubject(t.UnitTypeId.ToString(CultureInfo.InvariantCulture),
						Name(t.Type, t.UnitTypeId) + " → " + (x.Option?.ButtonText ?? ((UnitStateTypes)x.BuiltInStatus.GetValueOrDefault()).GetDisplayString()))))
					.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
					.ToArray();

			return facts.UnitTypesWithoutTarget
				.Select(t => new FindingSubject(t.UnitTypeId.ToString(CultureInfo.InvariantCulture), Name(t.Type, t.UnitTypeId)))
				.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToArray();
		}

		private static string Name(string name, int id) =>
			string.IsNullOrWhiteSpace(name) ? "#" + id.ToString(CultureInfo.InvariantCulture) : name.Trim();
	}

	internal sealed record StatusConfigurationFacts(IReadOnlyList<(CustomState Set, int Missing)> SetsWithoutMeanings, IReadOnlyList<UnitType> UnitTypesWithoutTarget,
		IReadOnlyList<UntimedDispatchStatus> UntimedDispatchStatuses);

	/// <summary>One reading shared by the evidence source and the finding subjects, so the count and the list agree.</summary>
	internal static class StatusConfigurationCheck
	{
		public const string StatusMeaningsRule = "status-meanings";
		public const string UnitStatusTargetsRule = "unit-status-targets";
		public const string OptionsWithoutMeaning = "statusOptionsWithoutMeaning";
		public const string UnitTypesWithoutStatusTarget = "unitTypesWithoutStatusTarget";
		public const string UnitStatusTimersRule = "unit-status-timers";
		public const string UnitTypesWithUntimedDispatchStatus = "unitTypesWithUntimedDispatchStatus";

		public static async Task<StatusConfigurationFacts> ReadAsync(int departmentId, ICustomStateService customStates, IUnitsService units,
			IDepartmentSettingsService settings, CancellationToken ct)
		{
			var sets = (await customStates.GetAllActiveCustomStatesForDepartmentAsync(departmentId).WaitAsync(ct))?.Where(s => s != null && !s.IsDeleted).ToList()
				?? throw new InvalidOperationException("Status metadata unavailable.");
			var unitTypes = (await units.GetUnitTypesForDepartmentAsync(departmentId).WaitAsync(ct))?.Where(t => t != null).ToList()
				?? throw new InvalidOperationException("Unit type metadata unavailable.");
			var limit = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			if (sets.Sum(s => s.Details?.Count ?? 0) + unitTypes.Count > limit || sets.Any(s => s.DepartmentId != departmentId) || unitTypes.Any(t => t.DepartmentId != departmentId))
				throw new InvalidOperationException("Scope or row bound exceeded.");

			var setsById = sets.ToDictionary(s => s.CustomStateId);

			// Only sets something reads: the personnel status and staffing sets, and unit sets assigned to a unit type.
			var inUse = sets.Where(s => s.Type != (int)CustomStateTypes.Unit ||
				unitTypes.Any(t => t.CustomStatesId.HasValue && t.CustomStatesId.Value == s.CustomStateId));
			var withoutMeanings = inUse
				.Select(s => (Set: s, Missing: s.GetActiveDetails().Count(d => d.BaseType < 0)))
				.Where(x => x.Missing > 0)
				.ToList();

			ct.ThrowIfCancellationRequested();
			var dispatchTarget = await settings.GetUnitCallDispatchStatusTargetAsync(departmentId).WaitAsync(ct) ?? StatusTarget.Default;
			var releaseTarget = await settings.GetUnitCallReleaseStatusTargetAsync(departmentId).WaitAsync(ct) ?? StatusTarget.Default;
			var overrideRows = await settings.GetUnitCallStatusOverridesByUnitTypeAsync(departmentId).WaitAsync(ct) ?? new List<UnitTypeCallStatusOverride>();
			var overrides = overrideRows.Where(o => o != null).GroupBy(o => o.UnitTypeId).ToDictionary(g => g.Key, g => g.First());

			var withoutTarget = new List<UnitType>();
			foreach (var unitType in unitTypes)
			{
				CustomState set = null;
				if (unitType.CustomStatesId.HasValue && unitType.CustomStatesId.Value > 0)
					setsById.TryGetValue(unitType.CustomStatesId.Value, out set);
				overrides.TryGetValue(unitType.UnitTypeId, out var unitTypeOverride);

				// Same order as CallDispatchStatusService: a unit-type override naming an active option of the type's own set
				// wins; otherwise the department-wide target is resolved against the set, and no match leaves the status.
				if (!Resolves(dispatchTarget, set, unitTypeOverride?.DispatchStatus, StatusTargetResolver.DispatchDefaultChain, (int)UnitStateTypes.Responding) ||
					!Resolves(releaseTarget, set, unitTypeOverride?.ReleaseStatus, StatusTargetResolver.ReleaseDefaultChain, (int)UnitStateTypes.Released))
					withoutTarget.Add(unitType);
			}

			ct.ThrowIfCancellationRequested();
			var thresholds = await settings.GetUnitStatusThresholdsAsync(departmentId).WaitAsync(ct);
			var timers = UnitStatusTimerCoverage.Check(thresholds, unitTypes, sets, dispatchTarget, overrideRows);

			return new StatusConfigurationFacts(withoutMeanings, withoutTarget, timers.UntimedDispatchStatuses);
		}

		private static bool Resolves(StatusTarget target, CustomState set, int? overrideOption, IReadOnlyList<ActionBaseTypes> defaultChain, int builtInDefault)
		{
			if (set != null && overrideOption.HasValue && overrideOption.Value >= 0 && set.GetActiveDetails().Any(d => d.CustomStateDetailId == overrideOption.Value))
				return true;

			return StatusTargetResolver.ResolveUnit(target, set, defaultChain, builtInDefault).Issue != StatusTargetIssue.NoMatchingOption;
		}
	}
}
