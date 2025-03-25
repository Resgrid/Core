using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class UnableToPurchaseView : BaseUserModel
	{
		public Payment CurrentPayment { get; set; }
		public Payment NextPayment { get; set; }
	}
}