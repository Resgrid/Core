using System;
using Resgrid.Model;

namespace Resgrid.Workers.Framework.Backend.Heartbeat
{
	public class HeartbeatQueueItem : QueueItem
	{
		public HeartbeatTypes Type { get; set; }
		//public DateTime Timestamp { get; set; }
		public string Data { get; set; }
	}
}