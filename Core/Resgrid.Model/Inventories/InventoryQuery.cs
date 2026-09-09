namespace Resgrid.Model.Inventories
{
	/// <summary>Structural filters applied before paging; authored content is never queried outside its protected read boundary.</summary>
	public sealed class InventoryQuery
	{
		public string ItemId { get; set; }
		public string LocationId { get; set; }
		public string AssetId { get; set; }
		public string IssuedToUserId { get; set; }
		public string KitId { get; set; }
	}
}
