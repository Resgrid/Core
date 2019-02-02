using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Inventory
{
	public class AdjustView
	{
		public string Message { get; set; }
		public int UnitId { get; set; }
		public Model.Inventory Inventory { get; set; }
		public List<InventoryType> Types { get; set; }
		public List<DepartmentGroup> Stations { get; set; } 
	}
}