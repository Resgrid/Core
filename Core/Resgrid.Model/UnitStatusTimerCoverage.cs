using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Why a department's unit status timers might never fire. Timers key on a status's meaning (base type), so a unit status
	/// with no meaning is never timed, and neither is the status units get on dispatch when its meaning has no timer. The board
	/// then says every unit is within its timers, which is the wrong reassurance (customer report, 2026-10-17: units were
	/// dispatched to a custom status with no meaning through a unit type override). Pure, so the settings pages and Admin Assist
	/// say the same thing.
	/// </summary>
	public static class UnitStatusTimerCoverage
	{
		/// <summary>
		/// Checks a department's timers against its unit statuses. With no timers set nothing is timed on purpose, so the report
		/// is empty.
		/// </summary>
		/// <param name="thresholds">The department's unit status timers.</param>
		/// <param name="unitTypes">The department's unit types.</param>
		/// <param name="customStates">The department's status sets; only unit sets assigned to a unit type are read.</param>
		/// <param name="dispatchTarget">The department-wide status units get on dispatch (setting 50).</param>
		/// <param name="overrides">Per-unit-type dispatch and release statuses (setting 52).</param>
		public static UnitStatusTimerCoverageReport Check(UnitStatusThresholds thresholds, IEnumerable<UnitType> unitTypes,
			IEnumerable<CustomState> customStates, StatusTarget dispatchTarget, IEnumerable<UnitTypeCallStatusOverride> overrides)
		{
			var report = new UnitStatusTimerCoverageReport();

			if (thresholds == null || thresholds.IsEmpty)
				return report;

			var types = (unitTypes ?? Enumerable.Empty<UnitType>()).Where(x => x != null).OrderBy(x => x.Type).ToList();
			var sets = (customStates ?? Enumerable.Empty<CustomState>())
				.Where(x => x != null && !x.IsDeleted && x.Type == (int)CustomStateTypes.Unit)
				.GroupBy(x => x.CustomStateId)
				.ToDictionary(g => g.Key, g => g.First());
			var overridesByType = (overrides ?? Enumerable.Empty<UnitTypeCallStatusOverride>())
				.Where(x => x != null)
				.GroupBy(x => x.UnitTypeId)
				.ToDictionary(g => g.Key, g => g.Last());

			foreach (var set in sets.Values.OrderBy(x => x.Name))
			{
				var usedBy = types.Where(t => t.CustomStatesId.HasValue && t.CustomStatesId.Value == set.CustomStateId).ToList();
				var withoutMeaning = set.GetActiveDetails().Where(d => d.BaseType < 0).ToList();

				if (usedBy.Any() && withoutMeaning.Any())
					report.SetsWithoutMeanings.Add(new UnitStatusSetWithoutMeanings(set, withoutMeaning, usedBy));
			}

			foreach (var unitType in types)
			{
				CustomState set = null;
				if (unitType.CustomStatesId.HasValue && unitType.CustomStatesId.Value > 0)
					sets.TryGetValue(unitType.CustomStatesId.Value, out set);

				overridesByType.TryGetValue(unitType.UnitTypeId, out var unitTypeOverride);
				var status = DispatchStatusFor(set, unitTypeOverride, dispatchTarget);

				// Nothing is written on dispatch (left unchanged, or no option with the meaning; Dispatch Settings warns about that).
				if (status == null)
					continue;

				var timed = status.BaseType >= 0 && thresholds.Find(status.BaseType) != null;
				var timeable = status.BaseType >= 0 && UnitStatusThresholds.TimeableBaseTypes.Contains((ActionBaseTypes)status.BaseType);

				// A meaning the timers grid does not offer (Available, Out of Service...) was not meant to be timed.
				if (timed || (status.BaseType >= 0 && !timeable))
					continue;

				var existing = report.UntimedDispatchStatuses.FirstOrDefault(x => x.Option?.CustomStateDetailId == status.Option?.CustomStateDetailId &&
					x.BuiltInStatus == status.BuiltInStatus);

				if (existing != null)
					existing.UnitTypes.Add(unitType);
				else
					report.UntimedDispatchStatuses.Add(new UntimedDispatchStatus(status.Option, status.BuiltInStatus, status.BaseType, new List<UnitType> { unitType }));
			}

			return report;
		}

		/// <summary>
		/// The status a unit type's units get on dispatch, in the same order as CallDispatchStatusService: a unit-type override
		/// naming an active option of the type's own set wins; otherwise the department-wide target resolved against the set.
		/// Null when nothing is written.
		/// </summary>
		public static DispatchStatusMeaning DispatchStatusFor(CustomState set, UnitTypeCallStatusOverride unitTypeOverride, StatusTarget dispatchTarget)
		{
			var options = set?.GetActiveDetails() ?? new List<CustomStateDetail>();

			if (set != null && unitTypeOverride != null && unitTypeOverride.DispatchStatus >= 0)
			{
				var chosen = options.FirstOrDefault(x => x.CustomStateDetailId == unitTypeOverride.DispatchStatus);
				if (chosen != null)
					return new DispatchStatusMeaning(chosen, null, chosen.BaseType);
			}

			var resolution = StatusTargetResolver.ResolveUnit(dispatchTarget ?? StatusTarget.Default, set,
				StatusTargetResolver.DispatchDefaultChain, (int)UnitStateTypes.Responding);

			if (!resolution.Value.HasValue)
				return null;

			if (set != null)
			{
				var option = options.FirstOrDefault(x => x.CustomStateDetailId == resolution.Value.Value);
				return option == null ? null : new DispatchStatusMeaning(option, null, option.BaseType);
			}

			return new DispatchStatusMeaning(null, resolution.Value.Value, (int)BuiltInStatusBaseTypes.ForUnitState(resolution.Value.Value));
		}
	}

	/// <summary>What <see cref="UnitStatusTimerCoverage.Check"/> found. Empty means every timed meaning can be reached.</summary>
	public sealed class UnitStatusTimerCoverageReport
	{
		/// <summary>Unit status sets assigned to a unit type with active options that have no meaning.</summary>
		public List<UnitStatusSetWithoutMeanings> SetsWithoutMeanings { get; } = new List<UnitStatusSetWithoutMeanings>();

		/// <summary>Statuses units get on dispatch that no timer can apply to, one entry per status with the unit types using it.</summary>
		public List<UntimedDispatchStatus> UntimedDispatchStatuses { get; } = new List<UntimedDispatchStatus>();

		public bool IsEmpty => SetsWithoutMeanings.Count == 0 && UntimedDispatchStatuses.Count == 0;

		/// <summary>Unit types whose dispatched units are never timed.</summary>
		public int UnitTypesWithUntimedDispatchStatus => UntimedDispatchStatuses.Sum(x => x.UnitTypes.Count);
	}

	public sealed class UnitStatusSetWithoutMeanings
	{
		public UnitStatusSetWithoutMeanings(CustomState set, List<CustomStateDetail> options, List<UnitType> unitTypes)
		{
			Set = set;
			Options = options;
			UnitTypes = unitTypes;
		}

		public CustomState Set { get; }

		/// <summary>The set's active options with no meaning, in order.</summary>
		public List<CustomStateDetail> Options { get; }

		/// <summary>The unit types that use the set.</summary>
		public List<UnitType> UnitTypes { get; }
	}

	public sealed class UntimedDispatchStatus
	{
		public UntimedDispatchStatus(CustomStateDetail option, int? builtInStatus, int baseType, List<UnitType> unitTypes)
		{
			Option = option;
			BuiltInStatus = builtInStatus;
			BaseType = baseType;
			UnitTypes = unitTypes;
		}

		/// <summary>The custom option units get on dispatch, or null for a built-in status.</summary>
		public CustomStateDetail Option { get; }

		/// <summary>The built-in <see cref="UnitStateTypes"/> value, when <see cref="Option"/> is null.</summary>
		public int? BuiltInStatus { get; }

		/// <summary>The status's meaning; below zero when it has none.</summary>
		public int BaseType { get; }

		public bool HasMeaning => BaseType >= 0;

		public List<UnitType> UnitTypes { get; }
	}

	/// <summary>A status units get on dispatch: a custom option or a built-in value, with its meaning (below zero for none).</summary>
	public sealed class DispatchStatusMeaning
	{
		public DispatchStatusMeaning(CustomStateDetail option, int? builtInStatus, int baseType)
		{
			Option = option;
			BuiltInStatus = builtInStatus;
			BaseType = baseType;
		}

		public CustomStateDetail Option { get; }

		public int? BuiltInStatus { get; }

		public int BaseType { get; }
	}
}
