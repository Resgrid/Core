using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Workforce
{
	// Workforce & Business Operations plan, Phase E (M0222): resource (unit / asset / external) cost profiles and
	// components, usage entries and the internal field-cost runs. Resource costs are operational facts gated by
	// ViewInternalCosts (74); personnel lines snapshot their pay detail into an ADP catalog 28 column so a run's
	// aggregate is visible without exposing an individual's rate.

	/// <summary>Acquisition, salvage, useful life and allocation basis for one unit / asset / external resource.</summary>
	public class ResourceCostProfile : IEntity
	{
		[Required]
		public string ResourceCostProfileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="ResourceSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public int? UnitId { get; set; }
		public string InventoryAssetId { get; set; }
		public string ExternalResourceKey { get; set; }
		public string Name { get; set; }
		public DateTime EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string Currency { get; set; } = "USD";
		public decimal? AcquisitionCost { get; set; }
		public DateTime? AcquisitionDate { get; set; }
		public DateTime? InServiceDate { get; set; }
		public decimal? SalvageValue { get; set; }
		/// <summary><see cref="DepreciationMethods"/>.</summary>
		public int DepreciationMethod { get; set; }
		/// <summary><see cref="AllocationBases"/>.</summary>
		public int AllocationBasis { get; set; }
		/// <summary>Useful life in the allocation basis unit (miles, kilometres, hours or days).</summary>
		public decimal? UsefulLifeQuantity { get; set; }
		public int? UsefulLifeMonths { get; set; }
		/// <summary>Expected annual utilization in the allocation basis unit (for time-life and fixed-annual allocation).</summary>
		public decimal? ExpectedAnnualUtilization { get; set; }
		public string Source { get; set; }
		public bool IsApproved { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<ResourceCostComponent> Components { get; set; } = new List<ResourceCostComponent>();
		[NotMapped] public string SubjectName { get; set; }
		public bool Covers(DateTime asOf) => EffectiveOn.Date <= asOf.Date && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "ResourceCostProfiles";
		[NotMapped] public string IdName => "ResourceCostProfileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => ResourceCostProfileId; set => ResourceCostProfileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Components", "SubjectName" };
	}

	/// <summary>A variable or fixed cost component of a resource profile.</summary>
	public class ResourceCostComponent : IEntity
	{
		[Required]
		public string ResourceCostComponentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string ResourceCostProfileId { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary><see cref="ResourceCostCategories"/>.</summary>
		public int Category { get; set; }
		/// <summary><see cref="ResourceCostBases"/>.</summary>
		public int Basis { get; set; }
		/// <summary>Direct rate / amount for the basis; null when the component is derived (consumption × unit price, or acquisition-calculated depreciation).</summary>
		public decimal? Rate { get; set; }
		/// <summary>Fuel / energy consumption per basis unit (e.g. gallons per mile).</summary>
		public decimal? ConsumptionQuantity { get; set; }
		public string ConsumptionUnit { get; set; }
		/// <summary>Commodity unit price (e.g. $ per gallon).</summary>
		public decimal? UnitPrice { get; set; }
		/// <summary><see cref="ResourceCostSources"/>.</summary>
		public int Source { get; set; }
		public DateTime? SourceWindowStart { get; set; }
		public DateTime? SourceWindowEnd { get; set; }
		public decimal? SourceMeterStart { get; set; }
		public decimal? SourceMeterEnd { get; set; }
		public bool IsApproved { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		public bool Covers(DateTime asOf) => (!EffectiveOn.HasValue || EffectiveOn.Value.Date <= asOf.Date) && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "ResourceCostComponents";
		[NotMapped] public string IdName => "ResourceCostComponentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => ResourceCostComponentId; set => ResourceCostComponentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One usage observation of a resource (distance, engine / operating / idle hours, days, fuel) against a Call / Deployment / DTR.</summary>
	public class ResourceUsageEntry : IEntity
	{
		[Required]
		public string ResourceUsageEntryId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="ResourceSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public int? UnitId { get; set; }
		public string InventoryAssetId { get; set; }
		public string ExternalResourceKey { get; set; }
		public int? CallId { get; set; }
		public string DeploymentId { get; set; }
		public string DeploymentTimeReportId { get; set; }
		public DateTime UsageDate { get; set; }
		/// <summary><see cref="UsagePhases"/>.</summary>
		public int Phase { get; set; }
		public decimal? StartOdometer { get; set; }
		public decimal? EndOdometer { get; set; }
		/// <summary>"mi" or "km" of the odometer / distance as recorded.</summary>
		public string DistanceUnit { get; set; } = "mi";
		public decimal? OriginalDistance { get; set; }
		/// <summary>Distance in miles (canonical).</summary>
		public decimal? CanonicalDistanceMiles { get; set; }
		public decimal? StartEngineMeter { get; set; }
		public decimal? EndEngineMeter { get; set; }
		public decimal? EngineHours { get; set; }
		public decimal? OperatingHours { get; set; }
		public decimal? IdleHours { get; set; }
		public decimal? DeployedDays { get; set; }
		public decimal? StandbyDays { get; set; }
		public decimal? FuelQuantity { get; set; }
		public string FuelUnit { get; set; }
		public decimal? FuelActualCost { get; set; }
		/// <summary><see cref="UsageSources"/>.</summary>
		public int Source { get; set; }
		public string ExternalId { get; set; }
		public bool IsApproved { get; set; }
		public bool NeedsReview { get; set; }
		public string ReviewReason { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "ResourceUsageEntries";
		[NotMapped] public string IdName => "ResourceUsageEntryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => ResourceUsageEntryId; set => ResourceUsageEntryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A frozen-able internal cost run for a Bid, Call or Deployment (estimate or actual), with revenue / recovery comparison.</summary>
	public class FieldCostRun : IEntity
	{
		[Required]
		public string FieldCostRunId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="FieldCostContextTypes"/>.</summary>
		public int ContextType { get; set; }
		public string BidId { get; set; }
		public int? CallId { get; set; }
		public string DeploymentId { get; set; }
		/// <summary><see cref="FieldCostRunTypes"/>.</summary>
		public int RunType { get; set; }
		public DateTime? ThroughDate { get; set; }
		public string Currency { get; set; } = "USD";
		/// <summary><see cref="FieldCostRunStatuses"/>.</summary>
		public int Status { get; set; }
		/// <summary>Version cutoffs: "profileId:rowVersion,…" of every compensation / resource profile the run consumed.</summary>
		public string InputVersions { get; set; }
		/// <summary><see cref="RevenueSources"/>.</summary>
		public int RevenueSource { get; set; }
		public decimal? RevenueAmount { get; set; }
		public string RevenueSourceId { get; set; }
		public string RevenueSourceVersion { get; set; }
		public decimal PersonnelTotal { get; set; }
		public decimal ResourceTotal { get; set; }
		public decimal ConsumableTotal { get; set; }
		public decimal ExpenseTotal { get; set; }
		public decimal OverheadTotal { get; set; }
		public decimal TotalLoadedCost { get; set; }
		public decimal? ContributionMargin { get; set; }
		public decimal? ContributionMarginPercent { get; set; }
		public decimal BreakEvenRevenue { get; set; }
		public int MissingInputCount { get; set; }
		public string SupersedesRunId { get; set; }
		public string FrozenByUserId { get; set; }
		public DateTime? FrozenOn { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<FieldCostLine> Lines { get; set; } = new List<FieldCostLine>();
		[NotMapped] public bool IsFrozen => Status == (int)FieldCostRunStatuses.Frozen || Status == (int)FieldCostRunStatuses.Superseded;

		[NotMapped] public string TableName => "FieldCostRuns";
		[NotMapped] public string IdName => "FieldCostRunId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => FieldCostRunId; set => FieldCostRunId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Lines", "IsFrozen" };
	}

	/// <summary>One cost line of a run. Personnel lines keep their rate / multiplier detail in the protected column.</summary>
	public class FieldCostLine : IEntity
	{
		[Required]
		public string FieldCostLineId { get; set; }
		[Required]
		public string FieldCostRunId { get; set; }
		public int DepartmentId { get; set; }
		public DateTime? LineDate { get; set; }
		/// <summary><see cref="FieldCostCategories"/>.</summary>
		public int Category { get; set; }
		/// <summary>"Employment", "Unit", "InventoryAsset", "External", "Expense", "Consumable", "Overhead".</summary>
		public string SubjectType { get; set; }
		public string SubjectId { get; set; }
		public string SubjectLabel { get; set; }
		public string Component { get; set; }
		public decimal Quantity { get; set; }
		public string Unit { get; set; }
		/// <summary>Snapshotted rate for resource / expense lines; null for personnel lines (their detail is protected).</summary>
		public decimal? Rate { get; set; }
		public decimal Amount { get; set; }
		/// <summary>ADP catalog 28: JSON with the personnel rate, multiplier, component ids and versions.</summary>
		public string ProtectedDetailJson { get; set; }
		public string SourceType { get; set; }
		public string SourceId { get; set; }
		public bool IsEstimated { get; set; }
		public bool IsFallback { get; set; }
		public string ReviewReason { get; set; }
		public int SortOrder { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string TableName => "FieldCostLines";
		[NotMapped] public string IdName => "FieldCostLineId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => FieldCostLineId; set => FieldCostLineId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
