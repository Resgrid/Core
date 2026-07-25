using System;
using System.Collections.Generic;

namespace Resgrid.Model.Tracking
{
	public enum TrackingIngressStatus
	{
		Accepted = 0,
		Invalid = 1,
		Unavailable = 2
	}

	public sealed class TrackingIngressResult
	{
		public TrackingIngressStatus Status { get; set; }
		public int Accepted { get; set; }
		public bool DuplicatesPossible { get; set; }
		public DateTime ReceivedOn { get; set; }
		public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
	}
}
