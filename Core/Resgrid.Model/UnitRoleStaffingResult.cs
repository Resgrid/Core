using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The computed staffing picture for a single unit: how many of its defined roles are filled and
	/// whether the assignees hold any required personnel qualification. Produced by
	/// <see cref="Calculate"/> and by <c>IUnitsService.GetUnitStaffingAsync</c>.
	/// </summary>
	public class UnitRoleStaffingResult
	{
		public int UnitId { get; set; }

		public UnitStaffingLevel Level { get; set; } = UnitStaffingLevel.Unknown;

		/// <summary>Number of roles defined on the unit.</summary>
		public int DefinedRoleCount { get; set; }

		/// <summary>Number of defined roles that currently have someone assigned.</summary>
		public int FilledRoleCount { get; set; }

		/// <summary>Names of defined roles that have no one assigned.</summary>
		public List<string> UnfilledRoleNames { get; set; } = new List<string>();

		/// <summary>Filled roles whose assignee does not hold the required/preferred personnel role.</summary>
		public List<UnitRoleStaffingIssue> QualificationIssues { get; set; } = new List<UnitRoleStaffingIssue>();

		/// <summary>True when the unit has at least one defined role.</summary>
		public bool HasDefinedRoles => DefinedRoleCount > 0;

		/// <summary>True when the unit is anything other than fully staffed (and has roles defined).</summary>
		public bool IsDegraded => HasDefinedRoles && Level != UnitStaffingLevel.FullyStaffed;

		/// <summary>A short, human-friendly summary suitable for a badge tooltip.</summary>
		public string Summary
		{
			get
			{
				if (!HasDefinedRoles)
					return "No roles defined for this unit.";

				var parts = new List<string> { $"{FilledRoleCount} of {DefinedRoleCount} roles filled" };

				if (QualificationIssues.Any())
					parts.Add($"{QualificationIssues.Count} unqualified");

				return string.Join(", ", parts) + ".";
			}
		}

		/// <summary>
		/// Computes a unit's staffing from its defined roles, the current active-role assignments and a
		/// qualification predicate. Kept as a pure function (no I/O) so it is easy to test and reuse.
		/// </summary>
		/// <param name="unitId">The unit being assessed.</param>
		/// <param name="definedRoles">The unit's defined <see cref="UnitRole"/> seats.</param>
		/// <param name="activeRoles">The current <see cref="UnitActiveRole"/> assignments (matched to a
		/// defined role by <c>Role</c> name).</param>
		/// <param name="isUserQualified">Predicate: does the given user hold the given personnel role id?</param>
		/// <param name="personnelRoleNameLookup">Optional map of personnel role id -> display name.</param>
		public static UnitRoleStaffingResult Calculate(int unitId, IEnumerable<UnitRole> definedRoles,
			IEnumerable<UnitActiveRole> activeRoles, Func<string, int, bool> isUserQualified,
			IReadOnlyDictionary<int, string> personnelRoleNameLookup = null)
		{
			var result = new UnitRoleStaffingResult { UnitId = unitId };

			var roles = definedRoles?.ToList() ?? new List<UnitRole>();
			var active = activeRoles?.Where(x => x.UnitId == unitId).ToList() ?? new List<UnitActiveRole>();

			result.DefinedRoleCount = roles.Count;

			if (result.DefinedRoleCount == 0)
			{
				result.Level = UnitStaffingLevel.Unknown;
				return result;
			}

			foreach (var role in roles)
			{
				var assignment = active.FirstOrDefault(x => string.Equals(x.Role, role.Name, StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrWhiteSpace(x.UserId));

				if (assignment == null)
				{
					result.UnfilledRoleNames.Add(role.Name);
					continue;
				}

				result.FilledRoleCount++;

				if (role.PersonnelRoleId.HasValue && isUserQualified != null
					&& !isUserQualified(assignment.UserId, role.PersonnelRoleId.Value))
				{
					string roleName = null;
					personnelRoleNameLookup?.TryGetValue(role.PersonnelRoleId.Value, out roleName);

					result.QualificationIssues.Add(new UnitRoleStaffingIssue
					{
						RoleName = role.Name,
						UserId = assignment.UserId,
						RequiredPersonnelRoleId = role.PersonnelRoleId.Value,
						RequiredPersonnelRoleName = roleName,
						Required = role.PersonnelRoleRequired
					});
				}
			}

			if (result.FilledRoleCount == 0)
				result.Level = UnitStaffingLevel.NotStaffed;
			else if (result.FilledRoleCount < result.DefinedRoleCount)
				result.Level = UnitStaffingLevel.PartiallyStaffed;
			else if (result.QualificationIssues.Count > 0)
				result.Level = UnitStaffingLevel.Degraded;
			else
				result.Level = UnitStaffingLevel.FullyStaffed;

			return result;
		}
	}

	/// <summary>A single filled seat whose assignee lacks the required/preferred personnel role.</summary>
	public class UnitRoleStaffingIssue
	{
		public string RoleName { get; set; }
		public string UserId { get; set; }
		public int RequiredPersonnelRoleId { get; set; }
		public string RequiredPersonnelRoleName { get; set; }

		/// <summary>True if the qualification was a hard requirement (block) vs. a preference (warn).</summary>
		public bool Required { get; set; }
	}
}
