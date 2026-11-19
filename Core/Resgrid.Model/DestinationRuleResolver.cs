using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>What a destination is being chosen for: the call's type and priority, and the unit travelling (when there is one).</summary>
	public class DestinationContext
	{
		/// <summary>The call's <see cref="CallType.CallTypeId"/>; null when the call has no (known) type.</summary>
		public int? CallTypeId { get; set; }

		/// <summary>The call's <see cref="Call.Priority"/>; null when unknown.</summary>
		public int? CallPriority { get; set; }

		/// <summary>The travelling unit's <see cref="UnitType.UnitTypeId"/>; null for the call's own destination or an untyped unit.</summary>
		public int? UnitTypeId { get; set; }
	}

	/// <summary>Why a rule that matched did not apply (<see cref="DestinationRequirements.SetAsideRules"/>).</summary>
	public enum DestinationRuleSetAsideReasons
	{
		/// <summary>Every destination type the rule named was deleted or is no longer a destination type.</summary>
		TypesRemoved = 1,

		/// <summary>The rule names destination types none of a more specific rule's types are (e.g. hospitals vs water sources for a tender).</summary>
		TypeConflict = 2
	}

	/// <summary>A matched rule that was set aside, and why.</summary>
	public class SetAsideDestinationRule
	{
		public DestinationRule Rule { get; set; }

		public DestinationRuleSetAsideReasons Reason { get; set; }
	}

	/// <summary>What the destination must be, from the rules that apply (<see cref="DestinationRuleResolver.Resolve"/>).</summary>
	public class DestinationRequirements
	{
		/// <summary>The destination types allowed; null for any destination type.</summary>
		public List<int> PoiTypeIds { get; set; }

		/// <summary>Capabilities the destination must offer (all of them).</summary>
		public List<string> RequiredCapabilityIds { get; set; } = new List<string>();

		/// <summary>Capabilities that win a near-tie (never also required).</summary>
		public List<string> PreferredCapabilityIds { get; set; } = new List<string>();

		/// <summary>The rules that apply, most specific first.</summary>
		public List<DestinationRule> AppliedRules { get; set; } = new List<DestinationRule>();

		/// <summary>Rules that matched but did not apply.</summary>
		public List<SetAsideDestinationRule> SetAsideRules { get; set; } = new List<SetAsideDestinationRule>();

		/// <summary>True when any rule narrows or orders the destinations.</summary>
		public bool HasRequirements => PoiTypeIds != null || RequiredCapabilityIds.Count > 0 || PreferredCapabilityIds.Count > 0;

		/// <summary>No rules: any destination, nothing required.</summary>
		public static DestinationRequirements None => new DestinationRequirements();

		/// <summary>True when a destination of this type is allowed.</summary>
		public bool AllowsType(int poiTypeId)
		{
			return PoiTypeIds == null || PoiTypeIds.Contains(poiTypeId);
		}
	}

	/// <summary>
	/// Works out what a destination must be from a department's <see cref="DestinationRule"/>s. Every rule whose conditions all
	/// hold applies together: their required capabilities add up, and their destination types narrow each other. Rules are taken
	/// most specific first (a unit-type rule before call rules, then more conditions, then by name); when a rule's types share
	/// none with the types already set, the rule is set aside, so a water tender on a Burns call goes to a water source with
	/// drafting rather than nowhere. A rule naming no types applies to any type.
	/// </summary>
	public static class DestinationRuleResolver
	{
		/// <summary>True when every condition the rule sets holds for the context.</summary>
		public static bool Matches(DestinationRule rule, DestinationContext context)
		{
			if (rule == null)
				return false;

			context ??= new DestinationContext();

			if (rule.CallTypeId.HasValue && rule.CallTypeId != context.CallTypeId)
				return false;

			if (rule.CallPriority.HasValue && rule.CallPriority != context.CallPriority)
				return false;

			if (rule.UnitTypeId.HasValue && rule.UnitTypeId != context.UnitTypeId)
				return false;

			return true;
		}

		/// <summary>The matching rules, most specific first.</summary>
		public static List<DestinationRule> Order(IEnumerable<DestinationRule> rules)
		{
			return (rules ?? Enumerable.Empty<DestinationRule>())
				.Where(x => x != null)
				.OrderByDescending(x => x.UnitTypeId.HasValue)
				.ThenByDescending(x => x.ConditionCount)
				.ThenBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.DestinationRuleId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <param name="rules">The department's rules.</param>
		/// <param name="context">The call (and unit) the destination is for.</param>
		/// <param name="destinationPoiTypeIds">The department's destination types; ids outside it are dropped. Null keeps every id.</param>
		/// <param name="capabilityIds">The department's capabilities; ids outside it are dropped. Null keeps every id.</param>
		public static DestinationRequirements Resolve(IEnumerable<DestinationRule> rules, DestinationContext context,
			IEnumerable<int> destinationPoiTypeIds, IEnumerable<string> capabilityIds)
		{
			var result = new DestinationRequirements();
			var knownTypes = destinationPoiTypeIds == null ? null : new HashSet<int>(destinationPoiTypeIds);
			var knownCapabilities = capabilityIds == null ? null : new HashSet<string>(capabilityIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);

			List<int> types = null;
			var required = new List<string>();
			var preferred = new List<string>();

			foreach (var rule in Order((rules ?? Enumerable.Empty<DestinationRule>()).Where(x => Matches(x, context))))
			{
				var named = rule.GetPoiTypeIds();
				var ruleTypes = knownTypes == null ? named : named.Where(knownTypes.Contains).ToList();

				if (named.Count > 0 && ruleTypes.Count == 0)
				{
					result.SetAsideRules.Add(new SetAsideDestinationRule { Rule = rule, Reason = DestinationRuleSetAsideReasons.TypesRemoved });
					continue;
				}

				if (ruleTypes.Count > 0)
				{
					if (types == null)
					{
						types = ruleTypes;
					}
					else
					{
						var shared = types.Where(ruleTypes.Contains).ToList();
						if (shared.Count == 0)
						{
							result.SetAsideRules.Add(new SetAsideDestinationRule { Rule = rule, Reason = DestinationRuleSetAsideReasons.TypeConflict });
							continue;
						}

						types = shared;
					}
				}

				result.AppliedRules.Add(rule);
				AddKnown(required, rule.GetRequiredCapabilityIds(), knownCapabilities);
				AddKnown(preferred, rule.GetPreferredCapabilityIds(), knownCapabilities);
			}

			result.PoiTypeIds = types;
			result.RequiredCapabilityIds = required;
			result.PreferredCapabilityIds = preferred.Where(x => !required.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();

			return result;
		}

		private static void AddKnown(List<string> target, IEnumerable<string> ids, HashSet<string> known)
		{
			foreach (var id in ids)
			{
				if (known != null && !known.Contains(id))
					continue;

				if (!target.Contains(id, StringComparer.OrdinalIgnoreCase))
					target.Add(id);
			}
		}
	}
}
