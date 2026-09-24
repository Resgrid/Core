using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ProcessTradeView
	{
		public ShiftSignupTrade Trade { get; set; }
		public string RequesterName { get; set; }
		public string GroupName { get; set; }

		/// <summary>Department-local start and end of the day being traded.</summary>
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public Department Department { get; set; }
	}
}
