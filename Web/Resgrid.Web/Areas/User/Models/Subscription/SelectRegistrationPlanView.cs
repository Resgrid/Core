namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class SelectRegistrationPlanView
	{
		public string StripeKey { get; set; }
		public int DepartmentId { get; set; }
		public string PaddleEnvironment { get; set; }
		public bool IsPaddleDepartment { get; set; }
		public string PaddleClientToken { get; set; }
		public string DiscountCode { get; set; }
	}
}
