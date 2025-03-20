using System;

namespace Resgrid.Web.Areas.User.Models.Inventory
{
	public class InventoryJson
	{
		public int InventoryId { get; set; }
		public string Type { get; set; }
		public double Amount { get; set; }
		public string Group { get; set; }
		public string Unit { get; set; }
		public string Batch { get; set; }
		public string Timestamp { get; set; }
		public string UserName { get; set; }
	}
}