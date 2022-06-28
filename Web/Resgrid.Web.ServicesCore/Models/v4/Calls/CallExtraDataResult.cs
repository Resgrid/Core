using Resgrid.Web.Services.Models.v4.CallPriorities;
using Resgrid.Web.Services.Models.v4.CallProtocols;
using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Depicts a call in the Resgrid system.
	/// </summary>
	public class CallExtraDataResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CallExtraDataResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallExtraDataResult()
		{
			Data = new CallExtraDataResultData();
		}
	}

	/// <summary>
	/// Depicts a call in the Resgrid system.
	/// </summary>
	public class CallExtraDataResultData
	{
		/// <summary>
		/// Call form data that was filled out during call creation.
		/// </summary>
		public string CallFormData { get; set; }

		/// <summary>
		/// Unit and Personnel activities attached to this call
		/// </summary>
		public List<DispatchedEventResultData> Activity { get; set; }

		/// <summary>
		/// Who was dispatched on this call, units, personnel, roles and groups
		/// </summary>
		public List<DispatchedEventResultData> Dispatches { get; set; }

		/// <summary>
		/// Call priority inforamtion
		/// </summary>
		public CallPriorityResultData Priority { get; set; }

		/// <summary>
		/// Protocols active fro this call
		/// </summary>
		public List<CallProtocolResultData> Protocols { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallExtraDataResultData()
		{
			Activity = new List<DispatchedEventResultData>();
			Dispatches = new List<DispatchedEventResultData>();
			Protocols = new List<CallProtocolResultData>();
		}
	}

	public class DispatchedEventResultData
	{
		public string Id { get; set; }
		public DateTime Timestamp { get; set; }
		public string Type { get; set; }
		public string Name { get; set; }
		public string GroupId { get; set; }
		public string Group { get; set; }
		public string Note { get; set; }
		public int StatusId { get; set; }
		public string Location { get; set; }
		public string StatusText { get; set; }
		public string StatusColor { get; set; }
	}
}
