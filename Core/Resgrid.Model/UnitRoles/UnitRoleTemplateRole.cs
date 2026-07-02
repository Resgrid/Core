namespace Resgrid.Model.UnitRoles
{
	/// <summary>
	/// A single seat/position within a <see cref="UnitRoleTemplate"/> (e.g. "Driver/Engineer",
	/// "Lead Medic"). Pure code-defined data; never persisted. When a template is applied the seat becomes
	/// an editable <see cref="UnitRole"/> row on the unit form.
	/// </summary>
	public class UnitRoleTemplateRole
	{
		/// <summary>The name of the role/seat (e.g. "Officer", "Firefighter").</summary>
		public string Name { get; set; }

		/// <summary>
		/// Optional suggested personnel qualification (by name, e.g. "Paramedic") for this seat. Because
		/// personnel roles are per-department, this is matched to the department's existing personnel roles
		/// by name when the template is applied; if no match exists the seat is added without a requirement.
		/// </summary>
		public string SuggestedPersonnelRole { get; set; }

		/// <summary>
		/// Whether the suggested personnel role should default to a hard requirement (blocks unqualified
		/// members) rather than a preference (allowed but degraded). Only meaningful when
		/// <see cref="SuggestedPersonnelRole"/> is set and matches an existing personnel role.
		/// </summary>
		public bool Required { get; set; }

		public UnitRoleTemplateRole()
		{
		}

		public UnitRoleTemplateRole(string name, string suggestedPersonnelRole = null, bool required = false)
		{
			Name = name;
			SuggestedPersonnelRole = suggestedPersonnelRole;
			Required = required;
		}
	}
}
