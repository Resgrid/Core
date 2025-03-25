using System;
using System.Collections.Generic;
using Resgrid.Web.Services.Controllers.Version3.Models.Protocols;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	public class CallDataResult
	{
		public List<DispatchedEventResult> Activity { get; set; }
		public List<DispatchedEventResult> Dispatches { get; set; }
		public CallPriorityDataResult Priority {get; set; }
		public List<ProtocolResult> Protocols { get; set; }

		public CallDataResult()
		{
			Activity = new List<DispatchedEventResult>();
			Dispatches = new List<DispatchedEventResult>();
			Protocols = new List<ProtocolResult>();
		}
	}

	public class DispatchedEventResult
	{
		public string Id { get; set; }
		public DateTime Timestamp { get; set; }
		public string Type { get; set; }
		public string Name { get; set; }
		public int GroupId { get; set; }
		public string Group { get; set; }
		public string Note { get; set; }
		public int StatusId { get; set; }
		public string Location { get; set; }
	}
}
