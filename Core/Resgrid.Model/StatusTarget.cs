using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	public enum StatusTargetKind
	{
		/// <summary>Resgrid's default for the action (see the caller's default chain and built-in value).</summary>
		Default = 0,
		/// <summary>Do not change the status.</summary>
		LeaveUnchanged = 1,
		/// <summary>A specific built-in value, as saved before base-type targets existed.</summary>
		BuiltIn = 2,
		/// <summary>Whichever option of the entity's own status set means this (an <see cref="ActionBaseTypes"/> value).</summary>
		BaseType = 3
	}

	/// <summary>
	/// A status a setting asks Resgrid to set (on dispatch, on release, ...), stated so it works for built-in and department
	/// statuses alike. A base-type target ("Dispatched") resolves per unit to the first active option of that unit's own
	/// status set with that base type, so one department-wide setting serves unit types with different status sets.
	///
	/// Stored as an integer so existing integer settings keep their type: -1 default, -2 leave unchanged, 0..999 a built-in
	/// value (the old format), 1000 + base type for a base-type target.
	/// </summary>
	public sealed class StatusTarget
	{
		public const int DefaultValue = -1;
		public const int LeaveUnchangedValue = -2;
		public const int BaseTypeOffset = 1000;

		public StatusTargetKind Kind { get; }
		public int Value { get; }

		private StatusTarget(StatusTargetKind kind, int value)
		{
			Kind = kind;
			Value = value;
		}

		public static StatusTarget Default { get; } = new StatusTarget(StatusTargetKind.Default, 0);
		public static StatusTarget LeaveUnchanged { get; } = new StatusTarget(StatusTargetKind.LeaveUnchanged, 0);
		public static StatusTarget ForBuiltIn(int builtInValue) => new StatusTarget(StatusTargetKind.BuiltIn, builtInValue);
		public static StatusTarget ForBaseType(ActionBaseTypes baseType) => new StatusTarget(StatusTargetKind.BaseType, (int)baseType);

		/// <summary>Reads a stored setting value; anything unreadable is the default.</summary>
		public static StatusTarget FromStoredValue(int? stored)
		{
			if (!stored.HasValue || stored.Value == DefaultValue)
				return Default;

			if (stored.Value == LeaveUnchangedValue)
				return LeaveUnchanged;

			if (stored.Value >= BaseTypeOffset)
			{
				var baseType = stored.Value - BaseTypeOffset;
				return Enum.IsDefined(typeof(ActionBaseTypes), baseType) && baseType != (int)ActionBaseTypes.None
					? ForBaseType((ActionBaseTypes)baseType)
					: Default;
			}

			return stored.Value >= 0 ? ForBuiltIn(stored.Value) : Default;
		}

		public static StatusTarget FromStoredValue(string stored)
		{
			return int.TryParse(stored, out var value) ? FromStoredValue(value) : Default;
		}

		public int ToStoredValue()
		{
			switch (Kind)
			{
				case StatusTargetKind.LeaveUnchanged:
					return LeaveUnchangedValue;
				case StatusTargetKind.BuiltIn:
					return Value;
				case StatusTargetKind.BaseType:
					return BaseTypeOffset + Value;
				default:
					return DefaultValue;
			}
		}

		public override string ToString() => ToStoredValue().ToString();
	}

	public enum StatusTargetIssue
	{
		None = 0,
		/// <summary>The status set has no active option with the base type: the status is left unchanged.</summary>
		NoMatchingOption = 1,
		/// <summary>The status set has several active options with the base type: the first by order is used.</summary>
		SeveralMatchingOptions = 2
	}

	/// <summary>What a <see cref="StatusTarget"/> resolved to for one status set.</summary>
	public sealed class StatusTargetResolution
	{
		/// <summary>The value to store (a built-in value or a custom option id), or null to leave the status as it is.</summary>
		public int? Value { get; set; }

		/// <summary>The base type that was looked for, when the target was resolved through base types.</summary>
		public ActionBaseTypes? BaseType { get; set; }

		public StatusTargetIssue Issue { get; set; }

		/// <summary>Every active option that matched the base type, in order (the first is used).</summary>
		public List<CustomStateDetail> Matches { get; set; } = new List<CustomStateDetail>();
	}

	/// <summary>
	/// Resolves a <see cref="StatusTarget"/> against one status set: a department's custom set (its active options, in
	/// order) or the built-in statuses. Pure, so dispatch, release and the settings page's warnings agree.
	/// </summary>
	public static class StatusTargetResolver
	{
		/// <summary>
		/// The built-in unit status Resgrid sets for a base type on a unit with no custom status set. Base types with no
		/// built-in equivalent (Transporting, At Hospital, ...) have none.
		/// </summary>
		public static int? BuiltInUnitStatusFor(ActionBaseTypes baseType)
		{
			switch (baseType)
			{
				case ActionBaseTypes.Available:
				case ActionBaseTypes.InQuarters:
				case ActionBaseTypes.Completed:
				case ActionBaseTypes.OnPatrol:
					return (int)UnitStateTypes.Available;
				case ActionBaseTypes.OnBreak:
					return (int)UnitStateTypes.Delayed;
				case ActionBaseTypes.Unavailable:
				case ActionBaseTypes.Maintenance:
					return (int)UnitStateTypes.OutOfService;
				case ActionBaseTypes.Dispatched:
					return (int)UnitStateTypes.Committed;
				case ActionBaseTypes.Responding:
					return (int)UnitStateTypes.Responding;
				case ActionBaseTypes.Enroute:
					return (int)UnitStateTypes.Enroute;
				case ActionBaseTypes.OnScene:
					return (int)UnitStateTypes.OnScene;
				case ActionBaseTypes.Staging:
					return (int)UnitStateTypes.Staging;
				case ActionBaseTypes.Returning:
					return (int)UnitStateTypes.Returning;
				case ActionBaseTypes.Cleared:
					return (int)UnitStateTypes.Released;
				default:
					return null;
			}
		}

		/// <summary>
		/// The built-in personnel status Resgrid sets for a base type for a department with no custom personnel statuses.
		/// </summary>
		public static int? BuiltInPersonnelStatusFor(ActionBaseTypes baseType, DestinationEntityTypes destination = DestinationEntityTypes.None)
		{
			switch (baseType)
			{
				case ActionBaseTypes.Available:
				case ActionBaseTypes.Cleared:
				case ActionBaseTypes.Completed:
					return (int)ActionTypes.StandingBy;
				case ActionBaseTypes.InQuarters:
					return (int)ActionTypes.AvailableStation;
				case ActionBaseTypes.NotResponding:
					return (int)ActionTypes.NotResponding;
				case ActionBaseTypes.Responding:
				case ActionBaseTypes.Enroute:
				case ActionBaseTypes.Dispatched:
					return destination == DestinationEntityTypes.Call ? (int)ActionTypes.RespondingToScene
						: destination == DestinationEntityTypes.Station ? (int)ActionTypes.RespondingToStation
						: (int)ActionTypes.Responding;
				case ActionBaseTypes.OnScene:
					return (int)ActionTypes.OnScene;
				default:
					return null;
			}
		}

		/// <summary>
		/// Resolves a target for one status set.
		/// </summary>
		/// <param name="target">What the setting asks for.</param>
		/// <param name="customOptions">The entity's custom status set, or null when it uses the built-in statuses.</param>
		/// <param name="defaultChain">For <see cref="StatusTargetKind.Default"/> on a custom set: the base types to look for, in turn.</param>
		/// <param name="builtInDefault">For <see cref="StatusTargetKind.Default"/> on the built-in statuses: the value Resgrid has always set.</param>
		/// <param name="builtInBaseType">A built-in value's base type (for a saved built-in value applied to a custom set).</param>
		/// <param name="builtInFor">The built-in value for a base type (for a base-type target on the built-in statuses).</param>
		public static StatusTargetResolution Resolve(StatusTarget target, CustomState customOptions,
			IReadOnlyList<ActionBaseTypes> defaultChain, int builtInDefault,
			Func<int, ActionBaseTypes> builtInBaseType, Func<ActionBaseTypes, int?> builtInFor)
		{
			target ??= StatusTarget.Default;
			var usesCustomSet = customOptions != null && !customOptions.IsDeleted;

			switch (target.Kind)
			{
				case StatusTargetKind.LeaveUnchanged:
					return new StatusTargetResolution();

				case StatusTargetKind.BuiltIn:
					if (!usesCustomSet)
						return new StatusTargetResolution { Value = target.Value };

					// A built-in value means nothing in a custom set: look for the option with the same meaning.
					return ResolveCustom(customOptions, new[] { builtInBaseType(target.Value) });

				case StatusTargetKind.BaseType:
					var baseType = (ActionBaseTypes)target.Value;
					if (usesCustomSet)
						return ResolveCustom(customOptions, new[] { baseType });

					var builtIn = builtInFor(baseType);
					return new StatusTargetResolution
					{
						Value = builtIn,
						BaseType = baseType,
						Issue = builtIn.HasValue ? StatusTargetIssue.None : StatusTargetIssue.NoMatchingOption
					};

				default:
					return usesCustomSet
						? ResolveCustom(customOptions, defaultChain ?? Array.Empty<ActionBaseTypes>())
						: new StatusTargetResolution { Value = builtInDefault };
			}
		}

		private static StatusTargetResolution ResolveCustom(CustomState set, IReadOnlyList<ActionBaseTypes> chain)
		{
			var options = set.GetActiveDetails() ?? new List<CustomStateDetail>();

			foreach (var baseType in chain.Where(x => x != ActionBaseTypes.None))
			{
				var matches = options.Where(x => x.BaseType == (int)baseType).ToList();
				if (matches.Count == 0)
					continue;

				return new StatusTargetResolution
				{
					Value = matches[0].CustomStateDetailId,
					BaseType = baseType,
					Matches = matches,
					Issue = matches.Count > 1 ? StatusTargetIssue.SeveralMatchingOptions : StatusTargetIssue.None
				};
			}

			return new StatusTargetResolution
			{
				BaseType = chain.Where(x => x != ActionBaseTypes.None).Cast<ActionBaseTypes?>().FirstOrDefault(),
				Issue = StatusTargetIssue.NoMatchingOption
			};
		}

		/// <summary>Resolves a unit status target: built-in unit statuses, or the unit type's custom set.</summary>
		public static StatusTargetResolution ResolveUnit(StatusTarget target, CustomState unitTypeSet, IReadOnlyList<ActionBaseTypes> defaultChain, int builtInDefault)
		{
			return Resolve(target, unitTypeSet, defaultChain, builtInDefault,
				value => BuiltInStatusBaseTypes.ForUnitState(value), BuiltInUnitStatusFor);
		}

		/// <summary>Resolves a personnel status target: built-in statuses, or the department's personnel set.</summary>
		public static StatusTargetResolution ResolvePersonnel(StatusTarget target, CustomState personnelSet, IReadOnlyList<ActionBaseTypes> defaultChain,
			int builtInDefault, DestinationEntityTypes destination = DestinationEntityTypes.None)
		{
			return Resolve(target, personnelSet, defaultChain, builtInDefault,
				value => BuiltInStatusBaseTypes.ForPersonnelStatus(value), baseType => BuiltInPersonnelStatusFor(baseType, destination));
		}

		/// <summary>The base types Resgrid looks for on dispatch when the department left the setting at its default.</summary>
		public static readonly IReadOnlyList<ActionBaseTypes> DispatchDefaultChain = new[] { ActionBaseTypes.Dispatched, ActionBaseTypes.Responding, ActionBaseTypes.Enroute };

		/// <summary>The base types Resgrid looks for on release (call closed, unit taken off) when left at the default: back in service.</summary>
		public static readonly IReadOnlyList<ActionBaseTypes> ReleaseDefaultChain = new[] { ActionBaseTypes.Cleared, ActionBaseTypes.Available, ActionBaseTypes.InQuarters };
	}
}
