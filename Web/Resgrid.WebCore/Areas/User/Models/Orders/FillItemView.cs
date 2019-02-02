using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Orders
{
	public class FillItemView
	{
		public bool Error { get; set; }
		public string ErrorMessage { get; set; }
		public Department Department { get; set; }
		public ResourceOrder Order { get; set; }
		public ResourceOrderFill Fill { get; set; }
		public int Count { get; set; }
	}
}