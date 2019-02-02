using System;

namespace Resgrid.Model.Events
{
	public class DistributionListCheckEvent
	{
		public int DistributionListId { get; set; }
		public DateTime Timestamp { get; set; }
		public bool IsFailure { get; set; }
		public string ErrorMessage { get; set; }
	}
}