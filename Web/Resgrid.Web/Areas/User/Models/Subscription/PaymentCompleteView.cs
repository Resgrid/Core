using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class PaymentCompleteView : BaseUserModel
	{
		public string Message { get; set; }
		public string ChargeId { get; set; }
		public int PaymentId { get; set; }
		public string SessionId { get; set; }
	}
}
