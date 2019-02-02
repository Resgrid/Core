using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class FinishTradeView
	{
		public string Message { get; set; }
		public ShiftSignupTrade Trade { get; set; }
		public List<UserProfile> Profiles { get; set; }
	}
}