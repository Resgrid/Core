using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Orders
{
	public class NewOrderView
	{
		public string Message { get; set; }
		public Department Department { get; set; }
		public ResourceOrder Order { get; set; }
	}
}