using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	/// <summary>The Workforce Pro seats page: the counter and the department's active members with their switch.</summary>
	public sealed class WorkforceProSeatsView
	{
		public WorkforceProSeatSummary Summary { get; set; } = new WorkforceProSeatSummary();
		public List<WorkforceProSeatRow> Members { get; set; } = new List<WorkforceProSeatRow>();
		public string Message { get; set; }
		public string MessageKind { get; set; }
	}

	public sealed class WorkforceProSeatRow
	{
		public string UserId { get; set; }
		public string Name { get; set; }

		/// <summary>Workforce Pro is switched on for the member.</summary>
		public bool Enabled { get; set; }

		/// <summary>Switched on and within the purchased seats while the add-on runs (records can be created and edited).</summary>
		public bool Licensed { get; set; }
	}
}
