using Resgrid.Web.Services.Models.v4.CallProtocols;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;
using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Depicts a call in the Resgrid system.
	/// </summary>
	public class CallResult: StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CallResultData Data { get; set; }
	}

	/// <summary>
	/// Depicts a call in the Resgrid system.
	/// </summary>
	public class CallResultData
	{
		/// <summary>
		/// Id of the call
		/// </summary>
		public string CallId { get; set; }

		/// <summary>
		/// ADP: true when this call belongs to a protection-enforced department (clients render the
		/// protected-field shield). Values in this DTO are then broker-decrypted plaintext or the
		/// exact "REDACTED" placeholder — never ciphertext.
		/// </summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP: stable catalog field ids ("calls.natureofcall") whose values are REDACTED.</summary>
		public List<string> RedactedFields { get; set; } = new List<string>();

		/// <summary>
		/// ADP: machine-readable reason when fields are redacted — step_up_required, grant_expired,
		/// grant_revoked, protected_access_denied, or broker_unavailable. Clients map
		/// step_up_required/grant_expired onto the step-up (VerifyStepUp) flow. Null when nothing is
		/// redacted.
		/// </summary>
		public string ProtectedReason { get; set; }

		//public string Unm { get; set; }

		/// <summary>
		/// Priority of the call (Low		= 0, Medium = 1, High	= 2, Emergency = 3)
		/// </summary>
		public int Priority { get; set; }

		/// <summary>
		/// Name of the Call
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Nature of the Call
		/// </summary>
		public string Nature { get; set; }

		/// <summary>
		/// High level note for the Call
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// Call Address
		/// </summary>
		public string Address { get; set; }

		/// <summary>
		/// Destination POI id if the call has a destination.
		/// </summary>
		public int? DestinationPoiId { get; set; }

		/// <summary>
		/// Destination display name.
		/// </summary>
		public string DestinationName { get; set; }

		/// <summary>
		/// Destination address.
		/// </summary>
		public string DestinationAddress { get; set; }

		/// <summary>
		/// Localized display label for the destination type (e.g. "POI", "Station"). Not suitable
		/// for programmatic branching; use <see cref="DestinationPoiTypeId"/> as the
		/// machine-readable POI type identifier instead.
		/// </summary>
		public string DestinationTypeName { get; set; }

		/// <summary>
		/// Destination POI type id.
		/// </summary>
		public int? DestinationPoiTypeId { get; set; }

		/// <summary>
		/// Destination latitude.
		/// </summary>
		public double? DestinationLatitude { get; set; }

		/// <summary>
		/// Destination longitude.
		/// </summary>
		public double? DestinationLongitude { get; set; }

		/// <summary>
		/// Geo location Coordinates
		/// </summary>
		public string Geolocation { get; set; }

		/// <summary>
		/// When was the call Logged On
		/// </summary>
		public DateTime LoggedOn { get; set; }

		/// <summary>
		/// State of the call (Active	= 0, Closed = 1, Cancelled = 2, Unfounded = 3)
		/// </summary>
		public int State { get; set; }

		/// <summary>
		/// Call Number, will be the 2 digit year (i.e. 15 for 2015) and an auto incrementing number for the call in the year. So 15-43 is the 43'rd call in 2015.
		/// </summary>
		public string Number { get; set; }

		/// <summary>
		/// The amount of notes the call has
		/// </summary>
		public int NotesCount { get; set; }

		/// <summary>
		/// The amount of audio the call has
		/// </summary>
		public int AudioCount { get; set; }

		/// <summary>
		/// The amount of images the call has
		/// </summary>
		public int ImgagesCount { get; set; }

		/// <summary>
		/// The amount of files the call has
		/// </summary>
		public int FileCount { get; set; }

		/// <summary>
		/// What 3 Words Address
		/// </summary>
		public string What3Words { get; set; }

		/// <summary>
		/// Reporter Name
		/// </summary>
		public string ContactName { get; set; }

		/// <summary>
		/// Reporter Contact Info
		/// </summary>
		public string ContactInfo { get; set; }

		/// <summary>
		/// Reference Id
		/// </summary>
		public string ReferenceId { get; set; }

		/// <summary>
		/// External Id
		/// </summary>
		public string ExternalId { get; set; }

		/// <summary>
		/// INcident Id
		/// </summary>
		public string IncidentId { get; set; }

		/// <summary>
		/// Audio File Id
		/// </summary>
		public string AudioFileId { get; set; }

		/// <summary>
		/// Call Type
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// Current alarm level (1-based). Only moves above 1 when the call has been escalated through
		/// a run card ("Strike Next Alarm"); clients show it so a dispatcher can see the call is
		/// already running at a higher level before striking again.
		/// </summary>
		public int AlarmLevel { get; set; }

		/// <summary>
		/// The run card driving this call's dispatch, or null when no card matched. Clients use it to
		/// decide whether escalation is available at all.
		/// </summary>
		public int? ActiveRunCardId { get; set; }

		/// <summary>
		/// When was the call Logged On in UTC time. Temporarily serialised WITHOUT the "Z" so app
		/// builds in the field keep showing correct call times; see LegacyZonelessUtcDateTimeConverter
		/// for when to switch this back to UtcDateTimeConverter.
		/// </summary>
		[JsonConverter(typeof(LegacyZonelessUtcDateTimeConverter))]
		public DateTime LoggedOnUtc { get; set; }

		/// <summary>
		/// Dispatch On
		/// </summary>
		public DateTime? DispatchedOn { get; set; }

		/// <summary>
		/// Dispatch On
		/// </summary>
		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? DispatchedOnUtc { get; set; }

		/// <summary>
		/// Geolocation (Latitude)
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// Geolocation (Longitude)
		/// </summary>
		public string Longitude { get; set; }

		/// <summary>
		/// Active Protocols for this call
		/// </summary>
		public List<CallProtocolResultData> Protocols { get; set; }

		/// <summary>
		/// User Defined Field values for this call
		/// </summary>
		public List<UdfFieldValueResultData> UdfValues { get; set; }

		/// <summary>
		/// Whether check-in timers are enabled for this call
		/// </summary>
		public bool CheckInTimersEnabled { get; set; }
	}
}
