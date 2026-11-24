using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// What an inbound dispatch email carrying a CAD call ID did. Values 1-13 are stored on <see cref="CallEmailUpdate.Outcome"/>
	/// (append only); 0, -1 and -2 are only ever returned. Applying, Saved, FollowedUp and Noting are the steps of work still in
	/// progress: a row the engine wrote first and moves on as each step is done, so a redelivery finishes what is left.
	/// </summary>
	public enum CallEmailUpdateOutcomes
	{
		/// <summary>Not stored. CAD updates are off for the department or the email carries no CAD call ID: a new call as before.</summary>
		NotApplicable = 0,

		/// <summary>No call had the ID: a new call was created and carries it.</summary>
		Created = 1,

		/// <summary>An open call was updated.</summary>
		Updated = 2,

		/// <summary>An open call matched and nothing the email carried differed.</summary>
		NoChange = 3,

		/// <summary>The email is older than one already applied to the call: recorded as a note only.</summary>
		Stale = 4,

		/// <summary>A call closed within the department's window matched: recorded as a note only.</summary>
		ClosedNote = 5,

		/// <summary>A close email closed the call.</summary>
		Closed = 6,

		/// <summary>A cancel email cancelled the call.</summary>
		Cancelled = 7,

		/// <summary>A close or cancel email matched a call under an active incident command: noted, left open.</summary>
		CloseBlocked = 8,

		/// <summary>A close or cancel email matched no call: nothing was created.</summary>
		CloseUnmatched = 9,

		/// <summary>
		/// Written before an update or close changes the call, with the hashes it will set, and moved to Saved once the call is saved.
		/// A row left here proves nothing (the save may or may not have happened): it never counts as the CAD's values, and its email
		/// runs again on a redelivery.
		/// </summary>
		Applying = 10,

		/// <summary>
		/// The update's or close's call save went through; its follow-up (an update's page in the outbox, a close's resource release)
		/// is not done yet. Counts as applied; a redelivery finishes the work from here instead of applying the email again.
		/// </summary>
		Saved = 11,

		/// <summary>
		/// The follow-up is done too; the added resource rows (an update) or the close notice (a close), and the note, may not be.
		/// Counts as applied; a redelivery finishes the rest.
		/// </summary>
		FollowedUp = 12,

		/// <summary>
		/// A note-only outcome (stale, no change, close blocked, a closed call noted) written before its note and moved to that outcome
		/// once the note is saved. Never counts; a redelivery of an email left here writes its note again.
		/// </summary>
		Noting = 13,

		/// <summary>Not stored. Another email for the same CAD call ID is being handled; try again later.</summary>
		Busy = -1,

		/// <summary>Not stored. This email (same provider message id) was already handled; a redelivery changes, notes and pages nothing.</summary>
		AlreadyHandled = -2
	}

	/// <summary>Where a CAD email came in. Stored on <see cref="CallEmailUpdate.Source"/>; append only.</summary>
	public enum CallEmailUpdateSources
	{
		DispatchAddress = 1,
		GroupDispatchAddress = 2,
		MailboxImport = 3,
		IntakeChannel = 4
	}

	/// <summary>
	/// One row per inbound dispatch email that carried a CAD call ID (CallEmailUpdates, M0330): the call it created or touched,
	/// when it was sent, what it changed, and a hash of each field group's value as the CAD set it. No email content is kept, only
	/// hashes, so the table holds no protected data. The hashes tell a value a person changed in Resgrid from the CAD's; the
	/// sent time orders emails that arrive out of order. No foreign keys; removed by UnlinkedDepartmentCleanup.
	/// </summary>
	[Table("CallEmailUpdates")]
	public class CallEmailUpdate : IEntity
	{
		public const int MaxCadCallIdLength = 100;

		[Key]
		[Required]
		[MaxLength(36)]
		public string CallEmailUpdateId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The call; null for <see cref="CallEmailUpdateOutcomes.CloseUnmatched"/>.</summary>
		public int? CallId { get; set; }

		/// <summary>The normalised CAD call ID.</summary>
		[MaxLength(MaxCadCallIdLength)]
		public string CadCallId { get; set; }

		/// <summary><see cref="CallEmailUpdateSources"/>.</summary>
		public int Source { get; set; }

		[MaxLength(128)]
		public string IntakeChannelId { get; set; }

		[MaxLength(500)]
		public string MessageId { get; set; }

		/// <summary>The email's Date header, when it had a readable one.</summary>
		public DateTime? SentOn { get; set; }

		public DateTime ReceivedOn { get; set; }

		/// <summary><see cref="CallEmailUpdateOutcomes"/>.</summary>
		public int Outcome { get; set; }

		/// <summary>
		/// Comma-separated field groups the email changed (for a stale or closed-call note, the ones it would have changed). A close
		/// records Close or Cancel, and Live when the call was active, so a redelivery can finish its release and notice.
		/// </summary>
		[MaxLength(500)]
		public string ChangedFields { get; set; }

		/// <summary>JSON object of field group to SHA-256 (hex) of the value the CAD last set; null when nothing was applied.</summary>
		[MaxLength(2000)]
		public string FieldHashes { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallEmailUpdateId; }
			set { CallEmailUpdateId = (string)value; }
		}

		[NotMapped]
		public string TableName => "CallEmailUpdates";

		[NotMapped]
		public string IdName => "CallEmailUpdateId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
