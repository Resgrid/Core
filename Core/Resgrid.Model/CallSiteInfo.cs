using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Everything a responder needs about the premises linked to a call, composed in one round trip
	/// (Contacts plan Phase A, decision 3b). This is also the RMS NERIS authoring prefill source for
	/// calls linked to a Contact (A8), so its shape is a consumed contract: additive changes only.
	/// </summary>
	public class CallSiteInfo
	{
		public int CallId { get; set; }
		public int DepartmentId { get; set; }
		public List<CallSiteContactInfo> Contacts { get; set; } = new List<CallSiteContactInfo>();
	}

	/// <summary>One linked contact's site knowledge: the contact, its pre-plan, hazards, live alert notes and file metadata.</summary>
	public class CallSiteContactInfo
	{
		public Contact Contact { get; set; }

		/// <summary>0 = Primary, 1 = Additional (matches CallContact.CallContactType).</summary>
		public int CallContactType { get; set; }

		public ContactPreplan Preplan { get; set; }
		public List<ContactPreplanHazard> Hazards { get; set; } = new List<ContactPreplanHazard>();

		/// <summary>ShouldAlert notes that are not deleted and not expired.</summary>
		public List<ContactNote> AlertNotes { get; set; } = new List<ContactNote>();

		/// <summary>Attachment metadata only; <see cref="ContactAttachment.Data"/> is never loaded here.</summary>
		public List<ContactAttachment> Attachments { get; set; } = new List<ContactAttachment>();
	}

	/// <summary>Lightweight per-call contact summary for list payloads (Contacts plan Phase A, decision 3a).</summary>
	public class CallContactSummary
	{
		public Contact Contact { get; set; }
		public int CallContactType { get; set; }
		public bool HasPreplan { get; set; }
		public int AlertNoteCount { get; set; }
		public int HazardCount { get; set; }
	}
}
