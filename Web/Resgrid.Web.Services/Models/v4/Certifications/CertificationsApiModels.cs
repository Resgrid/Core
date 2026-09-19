using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Certifications
{
	// Workforce & Business Operations plan, Phase D9. Every data row carries UpdatedOn for mobile delta-sync.

	public class CertificationTypesResult : StandardApiResponseV4Base
	{
		public List<CertificationTypeData> Data { get; set; } = new List<CertificationTypeData>();
	}

	public class CertificationTypeResult : StandardApiResponseV4Base
	{
		public CertificationTypeData Data { get; set; }
	}

	public class CertificationTypeData
	{
		public int Id { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		/// <summary>CertificationCategories value.</summary>
		public int Category { get; set; }
		/// <summary>CertificationAppliesTo value: 0 person, 1 unit.</summary>
		public int AppliesTo { get; set; }
		public string Description { get; set; }
		public string IssuingAuthority { get; set; }
		public int? DefaultValidityMonths { get; set; }
		public bool NeverExpires { get; set; }
		public decimal? RenewalCreditHoursRequired { get; set; }
		public bool RequiresVerification { get; set; }
		public bool IsActive { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class SaveCertificationTypeInput
	{
		public int Id { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public int Category { get; set; }
		public int AppliesTo { get; set; }
		public string Description { get; set; }
		public string IssuingAuthority { get; set; }
		public int? DefaultValidityMonths { get; set; }
		public bool NeverExpires { get; set; }
		public decimal? RenewalCreditHoursRequired { get; set; }
		public bool RequiresVerification { get; set; }
		public bool IsActive { get; set; } = true;
	}

	public class CertificationTemplatesResult : StandardApiResponseV4Base
	{
		public List<CertificationTemplateData> Data { get; set; } = new List<CertificationTemplateData>();
	}

	public class CertificationTemplateData
	{
		public string Id { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public int Category { get; set; }
		public int AppliesTo { get; set; }
		public string IssuingAuthority { get; set; }
		public int? DefaultValidityMonths { get; set; }
		public bool NeverExpires { get; set; }
		public bool RequiresVerification { get; set; }
		public decimal? RenewalCreditHoursRequired { get; set; }
		public string Description { get; set; }
	}

	public class CertificationsResult : StandardApiResponseV4Base
	{
		public List<CertificationData> Data { get; set; } = new List<CertificationData>();
	}

	public class CertificationResult : StandardApiResponseV4Base
	{
		public CertificationData Data { get; set; }
	}

	/// <summary>A personnel certification record. Name/Number/Area/IssuedBy are ADP catalog 6 fields and read REDACTED in a protected department without a grant.</summary>
	public class CertificationData
	{
		public int Id { get; set; }
		public string UserId { get; set; }
		public int? TypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public string Name { get; set; }
		public string Number { get; set; }
		public string Type { get; set; }
		public string Area { get; set; }
		public string IssuedBy { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public DateTime? ReceivedOn { get; set; }
		/// <summary>PersonnelCertificationStatuses value.</summary>
		public int Status { get; set; }
		public string StatusReason { get; set; }
		public DateTime? VerifiedOn { get; set; }
		public string VerifiedByUserId { get; set; }
		public int? DaysUntilExpiry { get; set; }
		public bool HasFile { get; set; }
		public decimal CreditHours { get; set; }
		public decimal? CreditHoursRequired { get; set; }
		public bool IsProtected { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class SaveCertificationInput
	{
		public int Id { get; set; }
		/// <summary>Self when empty; another member needs Certifications_Create/Update.</summary>
		public string UserId { get; set; }
		public int? TypeId { get; set; }
		public string Name { get; set; }
		public string Number { get; set; }
		public string Type { get; set; }
		public string Area { get; set; }
		public string IssuedBy { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public DateTime? ReceivedOn { get; set; }
		/// <summary>Base64 file bytes; null keeps the stored file.</summary>
		public string FileData { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
	}

	public class SetCertificationStatusInput
	{
		public int Id { get; set; }
		/// <summary>PersonnelCertificationStatuses value (Expired is worker-only).</summary>
		public int Status { get; set; }
		public string Reason { get; set; }
	}

	public class RenewCertificationInput
	{
		public int Id { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string Number { get; set; }
	}

	public class AddCertificationCreditInput
	{
		public int CertificationId { get; set; }
		public DateTime? CreditDate { get; set; }
		public decimal Hours { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public string FileData { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
	}

	public class CertificationCreditsResult : StandardApiResponseV4Base
	{
		public List<CertificationCreditData> Data { get; set; } = new List<CertificationCreditData>();
	}

	public class CertificationCreditData
	{
		public int Id { get; set; }
		public int CertificationId { get; set; }
		public DateTime CreditDate { get; set; }
		public decimal Hours { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public bool HasFile { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class ExpiringCertificationsResult : StandardApiResponseV4Base
	{
		public List<ExpiringCertificationData> Data { get; set; } = new List<ExpiringCertificationData>();
	}

	/// <summary>One expiring or expired record, person or unit (Scope discriminator).</summary>
	public class ExpiringCertificationData
	{
		/// <summary>"Person" or "Unit".</summary>
		public string Scope { get; set; }
		public int RecordId { get; set; }
		public string SubjectId { get; set; }
		public string SubjectName { get; set; }
		public int TypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public int Category { get; set; }
		public int Status { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int? DaysUntilExpiry { get; set; }
	}

	public class UnitCertificationsResult : StandardApiResponseV4Base
	{
		public List<UnitCertificationData> Data { get; set; } = new List<UnitCertificationData>();
	}

	public class UnitCertificationResult : StandardApiResponseV4Base
	{
		public UnitCertificationData Data { get; set; }
	}

	/// <summary>A unit certification record. Number/IssuedBy/Notes are ADP catalog 27 fields and read REDACTED in a protected department without a grant.</summary>
	public class UnitCertificationData
	{
		public int Id { get; set; }
		public int UnitId { get; set; }
		public int TypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public string Number { get; set; }
		public string IssuedBy { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>UnitCertificationStatuses value.</summary>
		public int Status { get; set; }
		public string StatusReason { get; set; }
		public string Notes { get; set; }
		public bool HasFile { get; set; }
		public string FileName { get; set; }
		public int? DaysUntilExpiry { get; set; }
		public bool IsProtected { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class SaveUnitCertificationInput
	{
		public int Id { get; set; }
		public int UnitId { get; set; }
		public int TypeId { get; set; }
		public string Number { get; set; }
		public string IssuedBy { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string Notes { get; set; }
		public string FileData { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
	}

	public class SetUnitCertificationStatusInput
	{
		public int Id { get; set; }
		/// <summary>UnitCertificationStatuses value (Expired is worker-only).</summary>
		public int Status { get; set; }
		public string Reason { get; set; }
	}

	public class RoleRequirementsResult : StandardApiResponseV4Base
	{
		public List<RoleRequirementData> Data { get; set; } = new List<RoleRequirementData>();
	}

	public class RoleRequirementData
	{
		public int Id { get; set; }
		public int RoleId { get; set; }
		public int TypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public bool IsMandatory { get; set; }
		public int? AnyOfGroup { get; set; }
		public bool AllowTrainee { get; set; }
		public int? GraceDaysOverride { get; set; }
	}

	public class SaveRoleRequirementsInput
	{
		public int RoleId { get; set; }
		public List<RoleRequirementInput> Requirements { get; set; } = new List<RoleRequirementInput>();
	}

	public class RoleRequirementInput
	{
		public int TypeId { get; set; }
		public bool IsMandatory { get; set; } = true;
		public int? AnyOfGroup { get; set; }
		public bool AllowTrainee { get; set; }
		public int? GraceDaysOverride { get; set; }
	}

	public class CertificationSettingsResult : StandardApiResponseV4Base
	{
		public CertificationSettingsData Data { get; set; }
	}

	public class CertificationSettingsData
	{
		/// <summary>CertificationEnforcementModes value: 0 off, 1 warn only, 2 enforce.</summary>
		public int EnforcementMode { get; set; }
		public int RoleRemovalGraceDays { get; set; }
		public string NotifyLeadDaysCsv { get; set; }
		public bool NotifyCertificationHolder { get; set; }
		public bool TreatPendingVerificationAsValid { get; set; }
		public bool SendAdminDigest { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class RoleEligibilityResult : StandardApiResponseV4Base
	{
		public List<RoleEligibilityData> Data { get; set; } = new List<RoleEligibilityData>();
	}

	public class RoleEligibilityData
	{
		public string UserId { get; set; }
		public bool Qualified { get; set; }
		public bool WarningsOnly { get; set; }
		public DateTime? RemovalDueOn { get; set; }
		public List<RoleRequirementOutcomeData> Violations { get; set; } = new List<RoleRequirementOutcomeData>();
	}

	public class RoleRequirementOutcomeData
	{
		public int TypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public bool IsMandatory { get; set; }
		public int? AnyOfGroup { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public DateTime? ViolationStartedOn { get; set; }
		public int GraceDays { get; set; }
	}
}
