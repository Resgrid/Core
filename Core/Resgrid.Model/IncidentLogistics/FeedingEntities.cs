using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0293 (both dialects).

	/// <summary>A meal window at a site (plan §9.8), including offsite feeding sites and voucher arrangements.</summary>
	public class IncidentMealPlan : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentMealPlanId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string IncidentFacilityId { get; set; }
		public string SiteName { get; set; }
		/// <summary><see cref="IncidentMealTypes"/>.</summary>
		public int MealType { get; set; }
		public DateTime ServiceDate { get; set; }
		public DateTime WindowStart { get; set; }
		public DateTime WindowEnd { get; set; }
		public DateTime? CutoffOn { get; set; }
		/// <summary><see cref="IncidentMealDeliveryModes"/>.</summary>
		public int DeliveryMode { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public bool IsOffsite { get; set; }
		public string VoucherTerms { get; set; }
		public int? AuthorizedHeadcount { get; set; }
		/// <summary><see cref="IncidentMealPlanStatuses"/>.</summary>
		public int Status { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentMealPlans";
		[NotMapped] public string IdName => "IncidentMealPlanId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentMealPlanId; set => IncidentMealPlanId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A versioned meal-count forecast snapshot (plan §9.8): source population, inbound and departures, bands, contingency, overrides. Dietary aggregates apply the minimum-cell rule.</summary>
	public class IncidentMealDemand : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentMealDemandId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentMealPlanId { get; set; }
		public int Version { get; set; }
		public int MetricVersion { get; set; }
		public int Population { get; set; }
		public int Inbound { get; set; }
		public int Departures { get; set; }
		public int Remote { get; set; }
		public int Guests { get; set; }
		public int Exclusions { get; set; }
		public decimal ContingencyPercent { get; set; }
		public int UnregisteredBand { get; set; }
		public int PredictedTimeoutBand { get; set; }
		public int ForecastCount { get; set; }
		public int? ManualOverride { get; set; }
		public string OverrideReason { get; set; }
		public string DietaryJson { get; set; }
		public string InputsJson { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentMealDemands";
		[NotMapped] public string IdName => "IncidentMealDemandId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentMealDemandId; set => IncidentMealDemandId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A meal order against a vendor engagement. Ordered quantities freeze; later roster changes create a variance, never a rewrite (plan §9.8).</summary>
	public class IncidentMealOrder : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentMealOrderId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentMealPlanId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public int OrderedCount { get; set; }
		public int? FrozenForecastVersion { get; set; }
		public decimal? UnitPrice { get; set; }
		public string Currency { get; set; }
		/// <summary><see cref="IncidentOrderStatuses"/>.</summary>
		public int Status { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentMealOrders";
		[NotMapped] public string IdName => "IncidentMealOrderId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentMealOrderId; set => IncidentMealOrderId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Delivered, served, returned, waste and shortage counts for a meal window (plan §9.8).</summary>
	public class IncidentMealActual : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentMealActualId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentMealPlanId { get; set; }
		public int Delivered { get; set; }
		public int Served { get; set; }
		public int Returned { get; set; }
		public int Waste { get; set; }
		public int Shortage { get; set; }
		public string ReceivingTemperature { get; set; }
		public string InspectionNotes { get; set; }
		public string Complaint { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentMealActuals";
		[NotMapped] public string IdName => "IncidentMealActualId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentMealActualId; set => IncidentMealActualId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Potable water planning at a site (plan §9.8): storage, reorder and days-of-supply thresholds, coefficient profile.</summary>
	public class IncidentWaterPlan : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentWaterPlanId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentFacilityId { get; set; }
		public string SiteName { get; set; }
		public decimal StorageCapacity { get; set; }
		public string Unit { get; set; }
		public decimal? ReorderThreshold { get; set; }
		public decimal? DaysOfSupplyThreshold { get; set; }
		public string Source { get; set; }
		public string CoefficientProfile { get; set; }
		/// <summary><see cref="IncidentMealPlanStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentWaterPlans";
		[NotMapped] public string IdName => "IncidentWaterPlanId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentWaterPlanId; set => IncidentWaterPlanId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An on-hand water measurement with original and canonical units (plan §9.8, R25).</summary>
	public class IncidentWaterStock : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentWaterStockId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentWaterPlanId { get; set; }
		/// <summary><see cref="IncidentWaterKinds"/>.</summary>
		public int Kind { get; set; }
		public decimal Quantity { get; set; }
		public string UnitOfMeasure { get; set; }
		public decimal? CanonicalQuantity { get; set; }
		public string CanonicalUnit { get; set; }
		public DateTime MeasuredOn { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentWaterStock";
		[NotMapped] public string IdName => "IncidentWaterStockId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentWaterStockId; set => IncidentWaterStockId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A scheduled or received water delivery (plan §9.8).</summary>
	public class IncidentWaterDelivery : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentWaterDeliveryId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentWaterPlanId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public decimal Quantity { get; set; }
		public string UnitOfMeasure { get; set; }
		public decimal? CanonicalQuantity { get; set; }
		public string CanonicalUnit { get; set; }
		public DateTime? ScheduledOn { get; set; }
		public DateTime? DeliveredOn { get; set; }
		public string TestResult { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentWaterDeliveries";
		[NotMapped] public string IdName => "IncidentWaterDeliveryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentWaterDeliveryId; set => IncidentWaterDeliveryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A configurable per-person demand coefficient (plan §9.8): department default when IncidentActivationId is null. Never one hard-coded universal number.</summary>
	public class IncidentDemandCoefficient : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentDemandCoefficientId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentCoefficientKinds"/>.</summary>
		public int Kind { get; set; }
		public string PopulationClass { get; set; }
		public decimal Value { get; set; }
		public string Unit { get; set; }
		public string Climate { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentDemandCoefficients";
		[NotMapped] public string IdName => "IncidentDemandCoefficientId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentDemandCoefficientId; set => IncidentDemandCoefficientId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
