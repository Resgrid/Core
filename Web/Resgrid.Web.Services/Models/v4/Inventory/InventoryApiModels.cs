using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model.Inventories;

namespace Resgrid.Web.Services.Models.v4.Inventory
{
	public sealed class InventoryApiResult<T> : StandardApiResponseV4Base
	{
		public T Data { get; set; }
		public bool HasMore { get; set; }
		public int ContractVersion { get; set; } = 1;
	}
	public sealed class InventoryCategoryInput
	{
		public string Id { get; set; }
		public int Revision { get; set; }
		[Required, StringLength(250)] public string Name { get; set; }
		public string ParentCategoryId { get; set; }
	}
	public sealed class InventoryArchiveInput
	{
		[Required] public string Id { get; set; }
		[Range(1, int.MaxValue)] public int Revision { get; set; }
	}
	public sealed class InventoryCreateLotInput
	{
		[Required] public string ItemId { get; set; }
		public DateTime? ExpiresOn { get; set; }
		[Required] public InventoryLotContent Details { get; set; }
	}
	public sealed class InventoryWitnessInput
	{
		[Required] public string RequestId { get; set; }
		[Required, StringLength(4000)] public string Attestation { get; set; }
	}
	/// <summary>A positive quantity delta with an explicit direction, never an absolute replacement balance.</summary>
	public sealed class InventoryAdjustmentInput
	{
		[Required] public string RequestId { get; set; }
		[Required] public string ItemId { get; set; }
		public string AssetId { get; set; }
		public string LotId { get; set; }
		public string FromLocationId { get; set; }
		public string ToLocationId { get; set; }
		public decimal Quantity { get; set; }
		public int? ExpectedAssetRevision { get; set; }
		[StringLength(16000)] public string Note { get; set; }
	}
	/// <summary>Bulk stock visible to this caller, summed across authorized locations and lots.</summary>
	public sealed class InventoryLowStockItem
	{
		public InventoryItem Item { get; set; }
		public decimal VisibleQuantity { get; set; }
		/// <summary>The item's department-wide level (ReorderPoint, else MinLevel); null when only per-location minimums are set.</summary>
		public decimal? ReorderPoint { get; set; }
		/// <summary>The department total is at or below ReorderPoint.</summary>
		public bool DepartmentLow { get; set; }
		/// <summary>Visible locations at or below their own minimum (par level).</summary>
		public List<InventoryLowStockLocation> Locations { get; set; } = new List<InventoryLowStockLocation>();
		public string QuantityScope { get; set; } = "AuthorizedLocations";
	}
	public sealed class InventoryLowStockLocation
	{
		public string LocationId { get; set; }
		public decimal Quantity { get; set; }
		public decimal MinimumQuantity { get; set; }
	}
}
