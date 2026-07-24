using System;

namespace Resgrid.Web.Services.Models.v4.UnitTracking
{
	public sealed class UnitTrackingIngressResponse
	{
		public int Accepted { get; set; }
		public bool DuplicatesPossible { get; set; }
		public DateTime ReceivedOn { get; set; }
	}

	public sealed class UnitTrackingIngressErrorResponse
	{
		public string[] Errors { get; set; }
	}
}
