using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0293 (both dialects).

	/// <summary>An incident facility (plan §9.6): ICP, base, camp, helibase, staging, warehouse, feeding or shower site, hotel and more. Location soft-references a Poi; capacity and agreement live here, inspections are records.</summary>
	public class IncidentFacility : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentFacilityId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentFacilityTypes"/>.</summary>
		public int FacilityType { get; set; }
		public int? PoiId { get; set; }
		public string MapLayerId { get; set; }
		public string IndoorMapId { get; set; }
		public string Address { get; set; }
		public string OperatingHours { get; set; }
		public string OwnerOperator { get; set; }
		public string ContactId { get; set; }
		public string ContactPhone { get; set; }
		/// <summary><see cref="IncidentFacilityStatuses"/>.</summary>
		public int Status { get; set; }
		public string SafetyNotes { get; set; }
		public string AccessNotes { get; set; }
		public DateTime? OpenedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFacilities";
		[NotMapped] public string IdName => "IncidentFacilityId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFacilityId; set => IncidentFacilityId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Maximum, usable, used, reserved and out-of-service capacity of one service dimension of a facility, with its alert threshold (plan §9.6).</summary>
	public class IncidentFacilityCapacity : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentFacilityCapacityId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentFacilityId { get; set; }
		/// <summary><see cref="IncidentFacilityServiceKinds"/>.</summary>
		public int ServiceKind { get; set; }
		public decimal MaxCapacity { get; set; }
		public decimal UsableCapacity { get; set; }
		public decimal CurrentUse { get; set; }
		public decimal Reserved { get; set; }
		public decimal OutOfService { get; set; }
		public string Unit { get; set; }
		public decimal? ThresholdPercent { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFacilityCapacities";
		[NotMapped] public string IdName => "IncidentFacilityCapacityId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFacilityCapacityId; set => IncidentFacilityCapacityId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The land-use, lease or direct-bill arrangement behind a facility; the signed document is an RMS record.</summary>
	public class IncidentFacilityAgreement : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentFacilityAgreementId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentFacilityId { get; set; }
		public string VendorAgreementId { get; set; }
		/// <summary><see cref="IncidentFacilityAgreementKinds"/>.</summary>
		public int AgreementKind { get; set; }
		public string RecordId { get; set; }
		public DateTime? StartsOn { get; set; }
		public DateTime? EndsOn { get; set; }
		public string Terms { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFacilityAgreements";
		[NotMapped] public string IdName => "IncidentFacilityAgreementId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFacilityAgreementId; set => IncidentFacilityAgreementId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A service a facility provides and its readiness (plan §9.6).</summary>
	public class IncidentFacilityService : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentFacilityServiceId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentFacilityId { get; set; }
		/// <summary><see cref="IncidentFacilityServiceKinds"/>.</summary>
		public int ServiceKind { get; set; }
		/// <summary><see cref="IncidentServiceReadiness"/>.</summary>
		public int Status { get; set; }
		public string Schedule { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentFacilityServices";
		[NotMapped] public string IdName => "IncidentFacilityServiceId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentFacilityServiceId; set => IncidentFacilityServiceId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
