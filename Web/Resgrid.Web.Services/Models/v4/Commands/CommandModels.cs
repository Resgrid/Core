using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Commands
{
	/// <summary>
	/// A command definition (predefined incident-command template / set of swimlanes for a call type).
	/// </summary>
	public class CommandResultData
	{
		/// <summary>Identifier of the command definition.</summary>
		public int CommandDefinitionId { get; set; }

		/// <summary>The call type this template applies to, or null for "Any Call Type".</summary>
		public int? CallTypeId { get; set; }

		/// <summary>Name of the command definition.</summary>
		public string Name { get; set; }

		/// <summary>Description of the command definition.</summary>
		public string Description { get; set; }

		/// <summary>Whether a default timer is enabled for this definition.</summary>
		public bool Timer { get; set; }

		/// <summary>Default timer length in minutes.</summary>
		public int TimerMinutes { get; set; }

		/// <summary>The predefined lanes (roles) for this command definition.</summary>
		public List<CommandRoleResultData> Lanes { get; set; } = new List<CommandRoleResultData>();
	}

	/// <summary>
	/// A predefined lane (role) within a command definition.
	/// </summary>
	public class CommandRoleResultData
	{
		public int CommandDefinitionRoleId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int LaneType { get; set; }
		public int SortOrder { get; set; }
		public int MinUnitPersonnel { get; set; }
		public int MaxUnitPersonnel { get; set; }
		public int MaxUnits { get; set; }
		public int MinTimeInRole { get; set; }
		public int MaxTimeInRole { get; set; }

		/// <summary>
		/// When true the API rejects assigning/moving own-department resources into this lane unless
		/// they match the requirement lists below; when false the requirements are advisory (UI hints).
		/// </summary>
		public bool ForceRequirements { get; set; }

		/// <summary>UnitTypeIds a unit must match to satisfy this lane (empty = unrestricted).</summary>
		public List<int> RequiredUnitTypes { get; set; } = new List<int>();

		/// <summary>PersonnelRoleIds a member must hold (any one) to satisfy this lane (empty = unrestricted).</summary>
		public List<int> RequiredPersonnelRoles { get; set; } = new List<int>();
	}

	/// <summary>
	/// Result wrapper for a collection of command definitions.
	/// </summary>
	public class CommandsResult : StandardApiResponseV4Base
	{
		public List<CommandResultData> Data { get; set; } = new List<CommandResultData>();
	}

	/// <summary>
	/// Result wrapper for a single command definition.
	/// </summary>
	public class CommandResult : StandardApiResponseV4Base
	{
		public CommandResultData Data { get; set; }
	}

	/// <summary>
	/// Input payload to create or update a command definition.
	/// </summary>
	public class SaveCommandInput
	{
		public int? CommandDefinitionId { get; set; }
		public int? CallTypeId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool Timer { get; set; }
		public int TimerMinutes { get; set; }
		public List<SaveCommandLaneInput> Lanes { get; set; } = new List<SaveCommandLaneInput>();
	}

	/// <summary>
	/// Input payload for a single lane within a command definition.
	/// </summary>
	public class SaveCommandLaneInput
	{
		public int? CommandDefinitionRoleId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int LaneType { get; set; }
		public int SortOrder { get; set; }
		public int MinUnitPersonnel { get; set; }
		public int MaxUnitPersonnel { get; set; }
		public int MaxUnits { get; set; }
		public int MinTimeInRole { get; set; }
		public int MaxTimeInRole { get; set; }
		public bool ForceRequirements { get; set; }

		/// <summary>UnitTypeIds required to assign a unit to this lane (empty = unrestricted).</summary>
		public List<int> RequiredUnitTypes { get; set; } = new List<int>();

		/// <summary>PersonnelRoleIds required (any one) to assign a member to this lane (empty = unrestricted).</summary>
		public List<int> RequiredPersonnelRoles { get; set; } = new List<int>();
	}
}
