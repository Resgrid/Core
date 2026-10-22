using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Framework;

namespace Resgrid.Model
{
	/// <summary>
	/// Rules for "any of" targets (conditional run cards), shared by run card and response package saves and by the engine:
	/// which ids a target may name, how a unit or person matches it, and how it reads to a dispatcher.
	/// </summary>
	public static class RunCardTargetRules
	{
		public const int MaximumTargetsPerOwner = 20;

		/// <summary>Problems with a set of targets, given the department's own unit type and role ids. Empty = valid.</summary>
		public static List<string> Validate(IEnumerable<RunCardTarget> targets, ISet<int> departmentUnitTypeIds, ISet<int> departmentRoleIds, string ownerName)
		{
			var errors = new List<string>();
			var list = (targets ?? Enumerable.Empty<RunCardTarget>()).Where(t => t != null).ToList();

			if (!list.Any())
			{
				errors.Add($"{ownerName}: choose at least one resource type.");
				return errors;
			}

			if (list.Count > MaximumTargetsPerOwner)
				errors.Add($"{ownerName}: at most {MaximumTargetsPerOwner} resource types.");

			foreach (var target in list)
			{
				switch ((RunCardTargetKinds)target.TargetKind)
				{
					case RunCardTargetKinds.UnitType:
						if (!target.TargetRefId.HasValue || departmentUnitTypeIds == null || !departmentUnitTypeIds.Contains(target.TargetRefId.Value))
							errors.Add($"{ownerName}: a unit type does not belong to this department.");
						break;
					case RunCardTargetKinds.UnitClass:
						if (!target.TargetRefId.HasValue || !Enum.IsDefined(typeof(UnitTypeClasses), target.TargetRefId.Value) || target.TargetRefId.Value <= 0)
							errors.Add($"{ownerName}: unknown unit class.");
						break;
					case RunCardTargetKinds.PersonnelRole:
						if (!target.TargetRefId.HasValue || departmentRoleIds == null || !departmentRoleIds.Contains(target.TargetRefId.Value))
							errors.Add($"{ownerName}: a personnel role does not belong to this department.");
						break;
					case RunCardTargetKinds.AnyUnit:
					case RunCardTargetKinds.AnyPerson:
						break;
					default:
						errors.Add($"{ownerName}: unknown resource type.");
						break;
				}
			}

			return errors;
		}

		/// <summary>Drops targets naming a unit type or role the department no longer has. Returns how many were dropped.</summary>
		public static int RemoveDetached(List<RunCardTarget> targets, ISet<int> departmentUnitTypeIds, ISet<int> departmentRoleIds)
		{
			if (targets == null)
				return 0;

			return targets.RemoveAll(t => t == null
				|| (t.TargetKind == (int)RunCardTargetKinds.UnitType && (!t.TargetRefId.HasValue || departmentUnitTypeIds == null || !departmentUnitTypeIds.Contains(t.TargetRefId.Value)))
				|| (t.TargetKind == (int)RunCardTargetKinds.PersonnelRole && (!t.TargetRefId.HasValue || departmentRoleIds == null || !departmentRoleIds.Contains(t.TargetRefId.Value))));
		}

		public static bool HasUnitTargets(IEnumerable<RunCardTarget> targets)
		{
			return (targets ?? Enumerable.Empty<RunCardTarget>()).Any(t => t != null && t.IsUnitTarget);
		}

		public static bool HasPersonTargets(IEnumerable<RunCardTarget> targets)
		{
			return (targets ?? Enumerable.Empty<RunCardTarget>()).Any(t => t != null && t.IsPersonTarget);
		}

		/// <summary>True when a unit of this type (with this class) matches any of the targets.</summary>
		public static bool MatchesUnit(IEnumerable<RunCardTarget> targets, int unitTypeId, int unitTypeClass)
		{
			foreach (var target in targets ?? Enumerable.Empty<RunCardTarget>())
			{
				if (target == null)
					continue;

				switch ((RunCardTargetKinds)target.TargetKind)
				{
					case RunCardTargetKinds.AnyUnit:
						return true;
					case RunCardTargetKinds.UnitType when target.TargetRefId == unitTypeId:
						return true;
					case RunCardTargetKinds.UnitClass when unitTypeClass > 0 && target.TargetRefId == unitTypeClass:
						return true;
				}
			}

			return false;
		}

		/// <summary>True when a person holding these roles matches any of the targets.</summary>
		public static bool MatchesPerson(IEnumerable<RunCardTarget> targets, IEnumerable<int> roleIds)
		{
			var roles = roleIds as ICollection<int> ?? (roleIds ?? Enumerable.Empty<int>()).ToList();

			foreach (var target in targets ?? Enumerable.Empty<RunCardTarget>())
			{
				if (target == null)
					continue;

				if (target.TargetKind == (int)RunCardTargetKinds.AnyPerson)
					return true;

				if (target.TargetKind == (int)RunCardTargetKinds.PersonnelRole && target.TargetRefId.HasValue && roles.Contains(target.TargetRefId.Value))
					return true;
			}

			return false;
		}

		/// <summary>
		/// How the targets read to a dispatcher ("PIT", "Engine or Ladder", "Any unit or person"), from the department's
		/// names. Unknown ids read as their kind.
		/// </summary>
		public static string Describe(IEnumerable<RunCardTarget> targets, IDictionary<int, string> unitTypeNames, IDictionary<int, string> roleNames)
		{
			var parts = new List<string>();

			foreach (var target in targets ?? Enumerable.Empty<RunCardTarget>())
			{
				if (target == null)
					continue;

				string part;
				switch ((RunCardTargetKinds)target.TargetKind)
				{
					case RunCardTargetKinds.UnitType:
						part = target.TargetRefId.HasValue && unitTypeNames != null && unitTypeNames.TryGetValue(target.TargetRefId.Value, out var typeName) && !string.IsNullOrWhiteSpace(typeName)
							? typeName : "Unit type";
						break;
					case RunCardTargetKinds.UnitClass:
						part = target.TargetRefId.HasValue && Enum.IsDefined(typeof(UnitTypeClasses), target.TargetRefId.Value)
							? $"Any {((UnitTypeClasses)target.TargetRefId.Value).GetDisplayString()}" : "Unit class";
						break;
					case RunCardTargetKinds.AnyUnit:
						part = "Any unit";
						break;
					case RunCardTargetKinds.PersonnelRole:
						part = target.TargetRefId.HasValue && roleNames != null && roleNames.TryGetValue(target.TargetRefId.Value, out var roleName) && !string.IsNullOrWhiteSpace(roleName)
							? roleName : "Role";
						break;
					case RunCardTargetKinds.AnyPerson:
						part = "Any person";
						break;
					default:
						continue;
				}

				if (!parts.Contains(part, StringComparer.OrdinalIgnoreCase))
					parts.Add(part);
			}

			return parts.Count == 0 ? string.Empty : string.Join(" or ", parts);
		}
	}
}
