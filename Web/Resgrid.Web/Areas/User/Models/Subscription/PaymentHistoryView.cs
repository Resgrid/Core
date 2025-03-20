using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class PaymentHistoryView : BaseUserModel
	{
		public Department Department { get; set; }
		public string Message { get; set; }
		public List<Payment> Payments { get; set; }
	}
}