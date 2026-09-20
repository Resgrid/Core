using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Workforce
{
	// Workforce & Business Operations plan, Phase E (M0220): employer identity, affiliates, establishments, labor
	// contractors, workers, employment periods and job assignments. Identifiers and addresses are ADP catalog 28
	// fields (Personnel family); dates, codes and flags are routing metadata.

	/// <summary>The department's employer identity for CRD reporting (one active profile, versioned).</summary>
	public class WorkforceEmployerProfile : IEntity
	{
		[Required]
		public string WorkforceEmployerProfileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string LegalName { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string Fein { get; set; }
		/// <summary>ADP catalog 28 (California employer account number).</summary>
		public string Sein { get; set; }
		/// <summary>ADP catalog 28 (California Secretary of State number).</summary>
		public string SosNumber { get; set; }
		public string Naics { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string EddAddress { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string HeadquartersAddress { get; set; }
		public bool IsIntegratedEnterprise { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string FilingContactName { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string FilingContactEmail { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string FilingContactPhone { get; set; }
		/// <summary><see cref="CaliforniaPayDataCoverageStatuses"/>; employer-declared, never determined by Resgrid.</summary>
		public int CoverageStatus { get; set; }
		public int? UsEmployeeCount { get; set; }
		public int? CaliforniaEmployeeCount { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public bool IsActive { get; set; } = true;
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string TableName => "WorkforceEmployerProfiles";
		[NotMapped] public string IdName => "WorkforceEmployerProfileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceEmployerProfileId; set => WorkforceEmployerProfileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An affiliated entity of an integrated enterprise.</summary>
	public class WorkforceAffiliatedEntity : IEntity
	{
		[Required]
		public string WorkforceAffiliatedEntityId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string WorkforceEmployerProfileId { get; set; }
		public string LegalName { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string Fein { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string Sein { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string SosNumber { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string HeadquartersAddress { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string TableName => "WorkforceAffiliatedEntities";
		[NotMapped] public string IdName => "WorkforceAffiliatedEntityId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceAffiliatedEntityId; set => WorkforceAffiliatedEntityId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A CRD establishment: a stable economic unit with a physical address (never an employee's home).</summary>
	public class WorkforceEstablishment : IEntity
	{
		[Required]
		public string WorkforceEstablishmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string WorkforceAffiliatedEntityId { get; set; }
		[Required]
		public string Code { get; set; }
		[Required]
		public string Name { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string PhysicalAddress { get; set; }
		public string City { get; set; }
		public string StateCode { get; set; }
		public string PostalCode { get; set; }
		public string Naics { get; set; }
		public string MajorActivity { get; set; }
		public bool IsHeadquarters { get; set; }
		public bool? WasFiledPriorYear { get; set; }
		public DateTime? ActiveFrom { get; set; }
		public DateTime? ActiveTo { get; set; }
		public string TimeZoneId { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public bool IsCalifornia => string.Equals(StateCode, "CA", StringComparison.OrdinalIgnoreCase);
		public bool IsActiveOn(DateTime asOf) => (!ActiveFrom.HasValue || ActiveFrom.Value.Date <= asOf.Date) && (!ActiveTo.HasValue || ActiveTo.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "WorkforceEstablishments";
		[NotMapped] public string IdName => "WorkforceEstablishmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceEstablishmentId; set => WorkforceEstablishmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsCalifornia" };
	}

	/// <summary>A labor contractor supplying workers to the department (the CRD labor-contractor report's client relationship).</summary>
	public class WorkforceLaborContractor : IEntity
	{
		[Required]
		public string WorkforceLaborContractorId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string LegalName { get; set; }
		public string OwnershipName { get; set; }
		public string Dba { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string Fein { get; set; }
		/// <summary>"FEIN" or an approved alternate identifier type.</summary>
		public string IdentifierType { get; set; } = "FEIN";
		/// <summary>ADP catalog 28.</summary>
		public string ContactDetails { get; set; }
		public DateTime? RelationshipStartOn { get; set; }
		public DateTime? RelationshipEndOn { get; set; }
		public string Provenance { get; set; }
		public bool IsActive { get; set; } = true;
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string TableName => "WorkforceLaborContractors";
		[NotMapped] public string IdName => "WorkforceLaborContractorId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceLaborContractorId; set => WorkforceLaborContractorId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One stable department worker identity: a Resgrid user or an external (non-login) worker.</summary>
	public class WorkforceWorker : IEntity
	{
		[Required]
		public string WorkforceWorkerId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		/// <summary>ADP catalog 28: the contractor's / payroll system's key for a non-login worker.</summary>
		public string ExternalWorkerKey { get; set; }
		/// <summary>ADP catalog 28: how a non-login worker is shown to authorized users.</summary>
		public string DisplayLabel { get; set; }
		public bool IsActive { get; set; } = true;
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string DisplayName { get; set; }

		[NotMapped] public string TableName => "WorkforceWorkers";
		[NotMapped] public string IdName => "WorkforceWorkerId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceWorkerId; set => WorkforceWorkerId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "DisplayName" };
	}

	/// <summary>A non-overlapping employment period of a worker.</summary>
	public class WorkforceEmployment : IEntity
	{
		[Required]
		public string WorkforceEmploymentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string WorkforceWorkerId { get; set; }
		public string WorkforceAffiliatedEntityId { get; set; }
		public string WorkforceLaborContractorId { get; set; }
		/// <summary><see cref="WorkerKinds"/>.</summary>
		public int WorkerKind { get; set; }
		public DateTime StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		/// <summary><see cref="EmploymentTypes"/>.</summary>
		public int EmploymentType { get; set; }
		/// <summary><see cref="ExemptionStatuses"/>.</summary>
		public int ExemptionStatus { get; set; }
		public string DefaultEstablishmentId { get; set; }
		/// <summary><see cref="CaliforniaEmployeeBases"/>.</summary>
		public int CaliforniaEmployeeBasis { get; set; }
		public int? PersonnelRoleId { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public List<WorkforceJobAssignment> Assignments { get; set; } = new List<WorkforceJobAssignment>();
		[NotMapped] public string WorkerDisplayName { get; set; }
		public bool Covers(DateTime from, DateTime to) => StartOn.Date <= to.Date && (!EndOn.HasValue || EndOn.Value.Date >= from.Date);
		public bool Overlaps(WorkforceEmployment other) => StartOn.Date <= (other.EndOn ?? DateTime.MaxValue).Date && (EndOn ?? DateTime.MaxValue).Date >= other.StartOn.Date;

		[NotMapped] public string TableName => "WorkforceEmployments";
		[NotMapped] public string IdName => "WorkforceEmploymentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceEmploymentId; set => WorkforceEmploymentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "Assignments", "WorkerDisplayName" };
	}

	/// <summary>An effective-dated job assignment: establishment, title, CRD job category and the optional Cal OES MARS classification crosswalk.</summary>
	public class WorkforceJobAssignment : IEntity
	{
		[Required]
		public string WorkforceJobAssignmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string WorkforceEmploymentId { get; set; }
		public DateTime EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string WorkforceEstablishmentId { get; set; }
		public string JobTitle { get; set; }
		public string SocCode { get; set; }
		public string SocVersion { get; set; }
		/// <summary>The CRD schema profile the job category code belongs to (e.g. CRD-RY2025).</summary>
		public string CaPayDataProfileCode { get; set; }
		/// <summary>CRD job category code within the profile (1–10 in Reporting Year 2025).</summary>
		public string JobCategoryCode { get; set; }
		public string CalOesMarsAuthorityProfileCode { get; set; }
		public string CalOesMarsClassificationCode { get; set; }
		public string MappingProvenance { get; set; }
		/// <summary><see cref="WorkModes"/>.</summary>
		public int WorkMode { get; set; }
		public string WorkCountry { get; set; }
		public string WorkSubdivision { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		public bool Covers(DateTime asOf) => EffectiveOn.Date <= asOf.Date && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date);
		public bool Overlaps(WorkforceJobAssignment other) => EffectiveOn.Date <= (other.ExpiresOn ?? DateTime.MaxValue).Date && (ExpiresOn ?? DateTime.MaxValue).Date >= other.EffectiveOn.Date;

		[NotMapped] public string TableName => "WorkforceJobAssignments";
		[NotMapped] public string IdName => "WorkforceJobAssignmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceJobAssignmentId; set => WorkforceJobAssignmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
