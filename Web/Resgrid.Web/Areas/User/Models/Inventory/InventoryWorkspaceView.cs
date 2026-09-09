using System.Collections.Generic;
using Newtonsoft.Json;
using Resgrid.Model.Inventories;
namespace Resgrid.Web.Areas.User.Models.Inventory
{
	public sealed class InventoryChoice { public string Id { get; set; } public string Name { get; set; } }
	public sealed class InventoryWorkspaceView
	{
		public string Tab { get; set; } = "OnHand";
		public int Page { get; set; }
		public bool HasMore { get; set; }
		public bool ChoicesHaveMore { get; set; }
		public bool Migrated { get; set; }
		public bool Locked { get; set; }
		public bool CanWrite { get; set; }
		public bool CanTransfer { get; set; }
		public bool CanIssue { get; set; }
		public bool CanWitness { get; set; }
		public bool CanChooseVendors { get; set; }
		public int? UnitId { get; set; }
		public string UserId { get; set; }
		public string Id { get; set; }
		public string ItemId { get; set; }
		public string LocationId { get; set; }
		public List<InventoryRow> Rows { get; set; } = new();
		public List<InventoryItem> Items { get; set; } = new();
		public List<InventoryLocation> Locations { get; set; } = new();
		public List<InventoryCategory> Categories { get; set; } = new();
		public List<InventoryAsset> Assets { get; set; } = new();
		public List<InventoryLot> Lots { get; set; } = new();
		public List<InventoryKitItem> KitContents { get; set; } = new();
		public List<InventoryChoice> Units { get; set; } = new();
		public List<InventoryChoice> People { get; set; } = new();
		public List<InventoryChoice> Groups { get; set; } = new();
		public List<InventoryVendor> Vendors { get; set; } = new();
		public List<InventoryVendorChoice> VendorContacts { get; set; } = new();
		public InventoryPurchaseOrderDetail PurchaseOrder { get; set; }
		public InventoryValuation Valuation { get; set; }
		public InventoryCountDetail CountDetail { get; set; }
		public bool CanViewReports { get; set; }
		public InventoryReportKind ReportKind { get; set; } = InventoryReportKind.OnHand;
		public static T Details<T>(InventoryRow row) where T : new() => string.IsNullOrEmpty(row?.Content) ? new T() : JsonConvert.DeserializeObject<T>(row.Content) ?? new T();
	}
}
