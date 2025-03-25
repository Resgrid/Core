using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class PaymentFailedView : BaseUserModel
	{
		public string Message { get; set; }
		public string ChargeId { get; set; }
		public string ErrorMessage { get; set; }
	}
}