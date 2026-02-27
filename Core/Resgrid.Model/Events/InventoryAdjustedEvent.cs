namespace Resgrid.Model.Events
{
	public class InventoryAdjustedEvent
	{
		public int DepartmentId { get; set; }
		public Inventory Inventory { get; set; }
		public double PreviousAmount { get; set; }
		public double NewAmount { get; set; }
	}
}

