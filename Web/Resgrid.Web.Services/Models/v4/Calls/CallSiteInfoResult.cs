using System.Collections.Generic;
using Resgrid.Web.Services.Models.v4.ContactFiles;
using Resgrid.Web.Services.Models.v4.Contacts;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// A contact linked to a call, as carried on the call payload (Contacts plan Phase A, decision 3a).
	/// </summary>
	public class CallContactResultData
	{
		/// <summary>Id of the contact</summary>
		public string ContactId { get; set; }

		/// <summary>Display name (person or company). REDACTED in a protected department without a grant.</summary>
		public string Name { get; set; }

		/// <summary>0 = Person, 1 = Company</summary>
		public int ContactType { get; set; }

		/// <summary>0 = Primary, 1 = Additional</summary>
		public int CallContactType { get; set; }

		/// <summary>True when the contact has a pre-incident plan</summary>
		public bool HasPreplan { get; set; }

		/// <summary>Count of live ShouldAlert notes</summary>
		public int AlertNoteCount { get; set; }

		/// <summary>Count of live premise hazards</summary>
		public int HazardCount { get; set; }
	}

	/// <summary>
	/// Site information for every contact linked to a call, composed in one round trip
	/// (Contacts plan Phase A, decision 3b). Also the RMS NERIS authoring prefill source (A8): additive changes only.
	/// </summary>
	public class CallSiteInfoResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CallSiteInfoData Data { get; set; }
	}

	public class CallSiteInfoData
	{
		/// <summary>Id of the call</summary>
		public string CallId { get; set; }

		/// <summary>
		/// ADP: true when the department enforces protection; contact identity fields are then decrypted
		/// (valid grant) or the exact "REDACTED" placeholder. Pre-plan and hazard text is not cataloged.
		/// </summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP: machine-readable reason when contact fields are redacted; null when nothing is redacted.</summary>
		public string ProtectedReason { get; set; }

		/// <summary>Linked contacts, primary first</summary>
		public List<CallSiteContactData> Contacts { get; set; } = new List<CallSiteContactData>();
	}

	public class CallSiteContactData
	{
		/// <summary>Id of the contact</summary>
		public string ContactId { get; set; }

		/// <summary>Display name (person or company)</summary>
		public string Name { get; set; }

		/// <summary>0 = Person, 1 = Company</summary>
		public int ContactType { get; set; }

		/// <summary>0 = Primary, 1 = Additional</summary>
		public int CallContactType { get; set; }

		/// <summary>Physical location coordinates ("lat,lng") when known</summary>
		public string LocationGpsCoordinates { get; set; }

		/// <summary>Entrance coordinates ("lat,lng") when known</summary>
		public string EntranceGpsCoordinates { get; set; }

		/// <summary>Best phone number on file (cell, then office, then home)</summary>
		public string PhoneNumber { get; set; }

		/// <summary>The pre-incident plan, null when none</summary>
		public ContactPreplanData Preplan { get; set; }

		/// <summary>RMS-5: the Records occupancy the pre-plan is projected from, when the department has switched pre-plan ownership to Records. Null otherwise (additive, contract unchanged).</summary>
		public string OccupancyId { get; set; }

		/// <summary>Live premise hazards, most severe first</summary>
		public List<ContactHazardData> Hazards { get; set; } = new List<ContactHazardData>();

		/// <summary>Live ShouldAlert notes (not expired)</summary>
		public List<ContactNoteResultData> AlertNotes { get; set; } = new List<ContactNoteResultData>();

		/// <summary>Attachment metadata with signed download URLs (no Data)</summary>
		public List<ContactFileResultData> Attachments { get; set; } = new List<ContactFileResultData>();
	}
}
