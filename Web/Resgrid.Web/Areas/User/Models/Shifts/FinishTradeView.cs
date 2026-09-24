using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class FinishTradeView
	{
		public string Message { get; set; }
		public ShiftSignupTrade Trade { get; set; }
		public List<FinishTradeOffer> Offers { get; set; } = new List<FinishTradeOffer>();

		/// <summary>An offer can be picked: none has been yet, or a supervisor denied the last pick.</summary>
		public bool CanPick { get; set; }
		public string AcceptedName { get; set; }
	}

	public class FinishTradeOffer
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool Offered { get; set; }
		public bool Declined { get; set; }
		public string Reason { get; set; }
		public List<FinishTradeOfferedShift> Shifts { get; set; } = new List<FinishTradeOfferedShift>();
	}

	public class FinishTradeOfferedShift
	{
		public int ShiftSignupId { get; set; }
		public string ShiftName { get; set; }
		public DateTime Day { get; set; }
	}
}
