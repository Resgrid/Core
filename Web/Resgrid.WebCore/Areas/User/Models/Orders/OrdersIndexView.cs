using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Orders
{
	public class OrdersIndexView
	{
		public Department Department { get; set; }
		public List<ResourceOrder> YourOrders { get; set; }
		public List<ResourceOrder> OthersOrders { get; set; }
		public Coordinates Coordinates { get; set; }
	}
}