using System;

namespace Resgrid.Model
{
	public class StripeSubscriptionData
	{
		public string Id
		{
			get;
			set;
		}

		public DateTime CurrentPeriodStart
		{
			get;
			set;
		}

		public DateTime CurrentPeriodEnd
		{
			get;
			set;
		}

		public string CustomerId
		{
			get;
			set;
		}

		public bool IsCanceled
		{
			get;
			set;
		}
	}
}
