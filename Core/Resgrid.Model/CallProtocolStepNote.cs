using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// The free-text note of one step-through event on a call (P3), kept apart from the step log (M0403, ADP catalog 39). The log on
	/// <c>CallProtocols.StepLogJson</c> stays operational (step ids, states, times, who) and is appended with an optimistic revision by
	/// concurrent and offline clients that never decrypt; the note is call narrative (a responder's words about the patient or scene), so it
	/// lives here, one row per event, sealed for a department whose ADP is on, and the log entry only points at it (<c>noteId</c>). A replay
	/// of the same offline event (same client step id) finds its row instead of writing a second one. No foreign keys: the protocol
	/// department cleanup removes the rows.
	/// </summary>
	[Table("CallProtocolStepNotes")]
	public class CallProtocolStepNote : IEntity
	{
		public string CallProtocolStepNoteId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public int CallProtocolId { get; set; }

		/// <summary>The step the note was written on (operational).</summary>
		public string StepId { get; set; }

		/// <summary>The device's id of the event (offline replays), when it had one.</summary>
		public string ClientStepId { get; set; }

		/// <summary>Who wrote it (operational).</summary>
		public string UserId { get; set; }

		/// <summary>Protected (catalog 39, Calls family): the note as written.</summary>
		public string Note { get; set; }

		/// <summary>True once the note was sealed under the department's ADP.</summary>
		public bool IsProtected { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue { get => CallProtocolStepNoteId; set => CallProtocolStepNoteId = value?.ToString(); }

		[NotMapped]
		public string TableName => "CallProtocolStepNotes";

		[NotMapped]
		public string IdName => "CallProtocolStepNoteId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
