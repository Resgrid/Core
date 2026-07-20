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
	}
}
