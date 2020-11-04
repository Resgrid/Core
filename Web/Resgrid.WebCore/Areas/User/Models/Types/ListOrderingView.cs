using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Types
{
	public class ListOrderingView
	{
		public List<PersonnelListStatusOrder> PersonnelStatusOrders { get; set; }
		public List<CustomStateDetail> AllPersonnelStatuses { get; set; }
		public List<CustomStateDetail> AvailablePersonnelStatuses { get; set; }

		public ListOrderingView()
		{
			PersonnelStatusOrders = new List<PersonnelListStatusOrder>();
			AllPersonnelStatuses = new List<CustomStateDetail>();
			AvailablePersonnelStatuses = new List<CustomStateDetail>();
		}
	}
}
