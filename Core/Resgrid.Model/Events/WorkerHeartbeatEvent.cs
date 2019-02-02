using System;

namespace Resgrid.Model.Events
{
	public class WorkerHeartbeatEvent
	{
		public int WorkerType { get; set; }
		public DateTime Timestamp { get; set; }
	}
}