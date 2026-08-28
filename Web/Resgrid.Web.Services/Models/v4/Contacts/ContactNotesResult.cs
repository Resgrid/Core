using System;
using Resgrid.Web.Services.Models.v4.CallTypes;
using System.Collections.Generic;

using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.Contacts
{
	/// <summary>
	/// Gets the notes for a contact
	/// </summary>
	public class ContactNotesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ContactNoteResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ContactNotesResult()
		{
			Data = new List<ContactNoteResultData>();
		}
	}

	/// <summary>
	/// A contact note
	/// </summary>
	public class ContactNoteResultData
	{
		/// <summary>
		/// ADP: true when this row belongs to a protection-enforced department (shield indicator).
		/// Protected values here are broker-decrypted plaintext or the exact "REDACTED" placeholder
		/// — never ciphertext.
		/// </summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP: machine-readable reason when values are redacted (step_up_required,
		/// grant_expired, grant_revoked, protected_access_denied, broker_unavailable); null when
		/// nothing is redacted.</summary>
		public string ProtectedReason { get; set; }

		public string ContactNoteId { get; set; }

		public string ContactId { get; set; }

		public string ContactNoteTypeId { get; set; }

		public string Note { get; set; }

		public string NoteType { get; set; }

		public bool ShouldAlert { get; set; }

		public int Visibility { get; set; } // 0 Internal, 1 Visible to Client

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? ExpiresOnUtc { get; set; }

		public string ExpiresOn { get; set; }

		public bool IsDeleted { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime AddedOnUtc { get; set; }

		public string AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public string AddedByName { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? EditedOnUtc { get; set; }

		public string EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		public string EditedByName { get; set; }
	}
}
