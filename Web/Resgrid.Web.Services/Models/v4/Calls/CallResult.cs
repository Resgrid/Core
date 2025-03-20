using Resgrid.Web.Services.Models.v4.CallProtocols;
using System;
using System.Collections.Generic;

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
		/// When was the call Logged On in UTC time
		/// </summary>
		public DateTime LoggedOnUtc { get; set; }

		/// <summary>
		/// Dispatch On
		/// </summary>
		public DateTime? DispatchedOn { get; set; }

		/// <summary>
		/// Dispatch On
		/// </summary>
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
	}
}
