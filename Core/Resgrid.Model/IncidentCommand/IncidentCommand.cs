using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A live incident-command instance established on a specific <c>Call</c>. Seeded (optionally) from a
	/// <c>CommandDefinition</c> template and then freely editable by the Commander for the life of the incident.
	/// </summary>
	public class IncidentCommand : IEntity, IChangeTracked
	{
		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>The CommandDefinition this instance was seeded from, if any.</summary>
		public int? SourceCommandDefinitionId { get; set; }

		public string EstablishedByUserId { get; set; }

		public DateTime EstablishedOn { get; set; }

		public string CurrentCommanderUserId { get; set; }

		public string CommandPostLatitude { get; set; }

		public string CommandPostLongitude { get; set; }

		public string IncidentActionPlan { get; set; }

		/// <summary>NIMS/ICS escalation level for the incident (department defined).</summary>
		public int IcsLevel { get; set; }

		/// <summary>Maps to <see cref="IncidentCommandStatus"/>.</summary>
		public int Status { get; set; }

		public DateTime? ClosedOn { get; set; }

		/// <summary>Change cursor for offline delta sync + last-write-wins; stamped on every write.</summary>
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentCommands";

		[NotMapped]
		public string IdName => "IncidentCommandId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentCommandId; }
			set { IncidentCommandId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Log of a command transfer (handoff of Incident Commander).</summary>
	public class CommandTransfer : IEntity
	{
		public string CommandTransferId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string FromUserId { get; set; }

		public string ToUserId { get; set; }

		public DateTime TransferredOn { get; set; }

		public string Notes { get; set; }

		[NotMapped]
		public string TableName => "CommandTransfers";

		[NotMapped]
		public string IdName => "CommandTransferId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommandTransferId; }
			set { CommandTransferId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An append-only ICS-201 style timeline entry, auto-written on every command action.</summary>
	public class CommandLogEntry : IEntity
	{
		public string CommandLogEntryId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="CommandLogEntryType"/>.</summary>
		public int EntryType { get; set; }

		public string Description { get; set; }

		public string UserId { get; set; }

		public string Latitude { get; set; }

		public string Longitude { get; set; }

		public DateTime OccurredOn { get; set; }

		[NotMapped]
		public string TableName => "CommandLogEntries";

		[NotMapped]
		public string IdName => "CommandLogEntryId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommandLogEntryId; }
			set { CommandLogEntryId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
