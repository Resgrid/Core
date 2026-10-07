using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Previous calls at a location: the calls related to a call, a contact or an occupancy, found by contact link and by
	/// address (compared parsed, so "110 S Main St" and "110 South Main" match) or proximity.
	/// </summary>
	public class LocationHistoryResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public LocationHistoryResultData Data { get; set; }
	}

	/// <summary>A location history.</summary>
	public class LocationHistoryResultData
	{
		/// <summary>False while the department's Advanced Data Protection is on: call addresses are encrypted, so only contact links were searched.</summary>
		public bool AddressMatchingAvailable { get; set; }

		/// <summary>False while older calls are still being indexed; some may be missing.</summary>
		public bool IndexComplete { get; set; }

		/// <summary>More calls matched than were returned; the newest are returned.</summary>
		public bool HasMore { get; set; }

		/// <summary>How the address was understood ("110 S MAIN ST"); null when it did not parse.</summary>
		public string InterpretedAddress { get; set; }

		/// <summary>True when call fields are protected; values read as REDACTED without a valid grant.</summary>
		public bool IsProtected { get; set; }

		/// <summary>Why protected values were withheld, when they were.</summary>
		public string ProtectedReason { get; set; }

		/// <summary>The calls, newest first.</summary>
		public List<LocationHistoryCallData> Calls { get; set; } = new List<LocationHistoryCallData>();
	}

	/// <summary>One call in a location history.</summary>
	public class LocationHistoryCallData
	{
		/// <summary>Id of the call</summary>
		public string CallId { get; set; }

		/// <summary>Department call number</summary>
		public string Number { get; set; }

		/// <summary>Call name</summary>
		public string Name { get; set; }

		/// <summary>Nature of the call</summary>
		public string Nature { get; set; }

		/// <summary>Call address as entered</summary>
		public string Address { get; set; }

		/// <summary>Call type</summary>
		public string Type { get; set; }

		/// <summary>Priority value</summary>
		public int Priority { get; set; }

		/// <summary>Priority name</summary>
		public string PriorityText { get; set; }

		/// <summary>Priority color (hex)</summary>
		public string PriorityColor { get; set; }

		/// <summary>CallStates value: 0 Active, 1 Closed, 2 Cancelled, 3 Unfounded, 4 Founded, 5 Minor, 6 Transferred, 7 False Alarm, 8 Pending</summary>
		public int State { get; set; }

		/// <summary>When the call was logged (UTC)</summary>
		public DateTime LoggedOnUtc { get; set; }

		/// <summary>When the call was logged, formatted in the department's time zone</summary>
		public string LoggedOn { get; set; }

		/// <summary>When the call was closed (UTC), if it was</summary>
		public DateTime? ClosedOnUtc { get; set; }

		/// <summary>Notes entered when the call was closed</summary>
		public string CompletedNotes { get; set; }

		/// <summary>Why the call is in the history: any of SameContact, SameAddress, SimilarAddress, Nearby</summary>
		public List<string> Matches { get; set; } = new List<string>();

		/// <summary>Meters from the looked-up location, when both have coordinates</summary>
		public double? DistanceMeters { get; set; }

		/// <summary>The call's notes, oldest first</summary>
		public List<LocationHistoryNoteData> Notes { get; set; } = new List<LocationHistoryNoteData>();
	}

	/// <summary>A note on a call in a location history.</summary>
	public class LocationHistoryNoteData
	{
		/// <summary>Id of the note</summary>
		public string CallNoteId { get; set; }

		/// <summary>Author user id</summary>
		public string UserId { get; set; }

		/// <summary>Author name</summary>
		public string FullName { get; set; }

		/// <summary>Note text</summary>
		public string Note { get; set; }

		/// <summary>When the note was added (UTC)</summary>
		public DateTime TimestampUtc { get; set; }
	}
}
