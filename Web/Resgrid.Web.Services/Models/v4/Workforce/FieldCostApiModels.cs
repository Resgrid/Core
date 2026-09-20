using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Workforce
{
	// Workforce & Business Operations plan, Phase E (E6): the narrow mobile surface. ViewInternalCosts (74) sees
	// aggregate cost summaries per deployment / call — categories, totals, revenue and margin, never a line, a rate or
	// a person; rostered members file their own resource usage readings (odometer / engine meter / fuel) for a
	// deployment they are seated on. Nothing here returns a protected value.

	public class FieldCostAccessResult : StandardApiResponseV4Base
	{
		public FieldCostAccessData Data { get; set; }
	}

	public class FieldCostAccessData
	{
		/// <summary>Workforce.InternalCosting entitlement (paid add-on + flag).</summary>
		public bool Enabled { get; set; }
		public bool CanViewInternalCosts { get; set; }
		public bool CanRecordUsage { get; set; }
	}

	public class FieldCostSummaryResult : StandardApiResponseV4Base
	{
		public FieldCostSummaryData Data { get; set; }
	}

	public class FieldCostSummariesResult : StandardApiResponseV4Base
	{
		public List<FieldCostSummaryData> Data { get; set; } = new List<FieldCostSummaryData>();
	}

	public class FieldCostSummaryData
	{
		public string RunId { get; set; }
		public int ContextType { get; set; }
		public string DeploymentId { get; set; }
		public string BidId { get; set; }
		public int? CallId { get; set; }
		public int RunType { get; set; }
		public int Status { get; set; }
		public DateTime? ThroughDate { get; set; }
		public string Currency { get; set; }
		public decimal PersonnelTotal { get; set; }
		public decimal ResourceTotal { get; set; }
		public decimal ConsumableTotal { get; set; }
		public decimal ExpenseTotal { get; set; }
		public decimal OverheadTotal { get; set; }
		public decimal TotalLoadedCost { get; set; }
		public int RevenueSource { get; set; }
		public decimal? RevenueAmount { get; set; }
		public decimal? ContributionMargin { get; set; }
		public decimal? ContributionMarginPercent { get; set; }
		public decimal BreakEvenRevenue { get; set; }
		public int MissingInputCount { get; set; }
		public DateTime? FrozenOn { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	public class AddResourceUsageInput
	{
		public string DeploymentId { get; set; }
		public int? CallId { get; set; }
		public int UnitId { get; set; }
		public DateTime UsageDate { get; set; }
		/// <summary>UsagePhases.</summary>
		public int Phase { get; set; }
		public decimal? StartOdometer { get; set; }
		public decimal? EndOdometer { get; set; }
		/// <summary>"mi" or "km"; the server keeps the reading and stores canonical miles.</summary>
		public string DistanceUnit { get; set; }
		public decimal? Distance { get; set; }
		public decimal? StartEngineMeter { get; set; }
		public decimal? EndEngineMeter { get; set; }
		public decimal? EngineHours { get; set; }
		public decimal? OperatingHours { get; set; }
		public decimal? IdleHours { get; set; }
		public decimal? FuelQuantity { get; set; }
		public string FuelUnit { get; set; }
		public decimal? FuelActualCost { get; set; }
		public string ExternalId { get; set; }
	}

	public class ResourceUsageResult : StandardApiResponseV4Base
	{
		public ResourceUsageData Data { get; set; }
	}

	public class ResourceUsagesResult : StandardApiResponseV4Base
	{
		public List<ResourceUsageData> Data { get; set; } = new List<ResourceUsageData>();
	}

	public class ResourceUsageData
	{
		public string Id { get; set; }
		public string DeploymentId { get; set; }
		public int? CallId { get; set; }
		public int UnitId { get; set; }
		public DateTime UsageDate { get; set; }
		public int Phase { get; set; }
		public string DistanceUnit { get; set; }
		public decimal? OriginalDistance { get; set; }
		public decimal? CanonicalDistanceMiles { get; set; }
		public decimal? EngineHours { get; set; }
		public decimal? OperatingHours { get; set; }
		public decimal? IdleHours { get; set; }
		public decimal? FuelQuantity { get; set; }
		public string FuelUnit { get; set; }
		public decimal? FuelActualCost { get; set; }
		public int Source { get; set; }
		public bool NeedsReview { get; set; }
		public string ReviewReason { get; set; }
		public bool IsApproved { get; set; }
	}
}
