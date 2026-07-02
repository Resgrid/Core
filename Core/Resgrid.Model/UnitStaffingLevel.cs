using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// The computed staffing level of a unit, derived from how many of the unit's defined roles
	/// (<see cref="UnitRole"/>) are currently filled (<see cref="UnitActiveRole"/>) and whether the
	/// people filling them hold any required personnel qualification (<see cref="PersonnelRole"/>).
	/// This is NOT a stored value — it is calculated on demand (see <see cref="UnitRoleStaffingResult"/>).
	/// </summary>
	public enum UnitStaffingLevel
	{
		/// <summary>The unit has no defined roles, so staffing cannot be assessed.</summary>
		[Description("No Roles")]
		[Display(Name = "No Roles")]
		Unknown = 0,

		/// <summary>The unit has defined roles but none of them are currently filled.</summary>
		[Description("Not Staffed")]
		[Display(Name = "Not Staffed")]
		NotStaffed = 1,

		/// <summary>Some, but not all, of the unit's defined roles are filled.</summary>
		[Description("Partially Staffed")]
		[Display(Name = "Partially Staffed")]
		PartiallyStaffed = 2,

		/// <summary>
		/// Every defined role is filled, but at least one assignee does not hold the personnel role
		/// required (or preferred) for their seat, so the unit is operating in a degraded capacity.
		/// </summary>
		[Description("Degraded")]
		[Display(Name = "Degraded")]
		Degraded = 3,

		/// <summary>Every defined role is filled and every qualification requirement is met.</summary>
		[Description("Fully Staffed")]
		[Display(Name = "Fully Staffed")]
		FullyStaffed = 4
	}
}
