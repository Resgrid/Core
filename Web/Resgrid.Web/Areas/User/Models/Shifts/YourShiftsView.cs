using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class YourShiftsView
	{
		public Department Department { get; set; }
		public List<ShiftSignup> Signups { get; set; }
		public List<ShiftSignupTrade> Trades { get; set; }
	}
}