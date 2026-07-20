using System;

namespace Resgrid.Model.CommandBoards
{
	/// <summary>
	/// A code-defined lane in a <see cref="CommandBoardTemplate"/>. Suggested unit types and personnel
	/// roles are names rather than database identifiers because both are configured per department.
	/// </summary>
	public class CommandBoardTemplateLane
	{
		public string Name { get; set; }

		public string Description { get; set; }

		public CommandNodeType LaneType { get; set; }

		public string[] SuggestedUnitTypes { get; set; } = Array.Empty<string>();

		public string[] SuggestedPersonnelRoles { get; set; } = Array.Empty<string>();

		/// <summary>
		/// Whether matched requirements should block incompatible assignments. This is only applied when
		/// at least one suggested unit type or personnel role exists in the department.
		/// </summary>
		public bool ForceRequirements { get; set; }

		/// <summary>
		/// Lane identification color (hex). Carried onto the created definition role and, from there,
		/// onto runtime board lanes and the map markers of assigned resources.
		/// </summary>
		public string Color { get; set; }

		/// <summary>Minimum units this lane wants filled (0 = none; advisory under-filled indicator).</summary>
		public int MinUnits { get; set; }

		/// <summary>Maximum units in this lane at once (0 = unlimited).</summary>
		public int MaxUnits { get; set; }

		/// <summary>Minimum personnel riding a unit for it to fit this lane (0 = none).</summary>
		public int MinUnitPersonnel { get; set; }

		/// <summary>Maximum personnel riding a unit for it to fit this lane (0 = none).</summary>
		public int MaxUnitPersonnel { get; set; }

		/// <summary>Minimum minutes a resource should stay before rotating out (0 = none; advisory).</summary>
		public int MinTimeInRole { get; set; }

		/// <summary>Minutes before an assigned resource shows rotation-due (0 = none).</summary>
		public int MaxTimeInRole { get; set; }
	}
}
