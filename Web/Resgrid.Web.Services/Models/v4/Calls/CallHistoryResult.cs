using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Gets the history of all actions taken during a call
	/// </summary>
	public class CallHistoryResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallHistoryResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallHistoryResult()
		{
			Data = new List<CallHistoryResultData>();
		}
	}

	/// <summary>
	/// Call history item
	/// </summary>
	public class CallHistoryResultData
	{
		/// <summary>
		/// Timestamp of the history event in local time
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// Timestamp of the history event in UTC/GMT
		/// </summary>
		public DateTime TimestampUtc { get; set; }

		/// <summary>
		/// Type of the event (0 = Call, 1 = Note, 2 = Personnel Status, 3 = Unit Status)
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// Source Identifier
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Text describing the history event
		/// </summary>
		public string Info { get; set; }
	}
}
