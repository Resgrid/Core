using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Vertical a certification type belongs to (Workforce &amp; Business Operations plan, Phase D1.1). Display grouping only.</summary>
	public enum CertificationCategories
	{
		Fire = 0,
		EMS = 1,
		SAR = 2,
		Wildland = 3,
		EmergencyManagement = 4,
		Industrial = 5,
		Security = 6,
		Medical = 7,
		Driver = 8,
		Vehicle = 9,
		Other = 10
	}

	/// <summary>Whether records of a type are held by a person or by a unit (plan D1.1: one catalog, two record scopes).</summary>
	public enum CertificationAppliesTo
	{
		Person = 0,
		Unit = 1
	}

	/// <summary>Lifecycle of a personnel certification record (plan D1.3).</summary>
	public enum PersonnelCertificationStatuses
	{
		Active = 0,
		Expired = 1,
		Suspended = 2,
		Revoked = 3,
		PendingVerification = 4,
		Trainee = 5
	}

	/// <summary>Lifecycle of a unit certification record (plan D1.8): units do not train and nobody signs them off.</summary>
	public enum UnitCertificationStatuses
	{
		Active = 0,
		Expired = 1,
		Suspended = 2
	}

	/// <summary>How role certification requirements are applied (plan decision 26, D1.6).</summary>
	public enum CertificationEnforcementModes
	{
		Off = 0,
		WarnOnly = 1,
		Enforce = 2
	}

	/// <summary>
	/// Which personnel roles require which certification types (plan D1.5). Rows with the same non-null
	/// <see cref="AnyOfGroup"/> form an OR-set; null rows are AND requirements. Only Person-scoped types may
	/// be referenced (service-enforced).
	/// </summary>
	[Table("PersonnelRoleCertificationRequirements")]
	public class PersonnelRoleCertificationRequirement : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PersonnelRoleCertificationRequirementId { get; set; }

		[Required]
		public int PersonnelRoleId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int DepartmentCertificationTypeId { get; set; }

		/// <summary>Mandatory blocks role membership under Enforce and drives removal; optional only warns and reports.</summary>
		public bool IsMandatory { get; set; }

		/// <summary>Same non-null value on several rows = any one of them satisfies ("EMT OR AEMT OR Paramedic").</summary>
		public int? AnyOfGroup { get; set; }

		/// <summary>A Trainee-status record satisfies this requirement (wildland trainee seats with an open task book).</summary>
		public bool AllowTrainee { get; set; }

		/// <summary>Overrides the department's RoleRemovalGraceDays for this requirement.</summary>
		public int? GraceDaysOverride { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PersonnelRoleCertificationRequirementId; }
			set { PersonnelRoleCertificationRequirementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "PersonnelRoleCertificationRequirements";

		[NotMapped]
		public string IdName => "PersonnelRoleCertificationRequirementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Per-department certification enforcement and notification settings (plan D1.6). One row per department, keyed by DepartmentId.</summary>
	[Table("DepartmentCertificationSettings")]
	public class DepartmentCertificationSettings : IEntity
	{
		public const string DefaultNotifyLeadDaysCsv = "60,30,14,7,1";
		public const int DefaultRoleRemovalGraceDays = 30;

		[Key]
		[Required]
		public int DepartmentId { get; set; }

		/// <summary><see cref="CertificationEnforcementModes"/>; Off until a department opts in.</summary>
		public int EnforcementMode { get; set; }

		public int RoleRemovalGraceDays { get; set; } = DefaultRoleRemovalGraceDays;

		/// <summary>Days before expiry on which the expiring notification fires, e.g. "60,30,14,7,1".</summary>
		public string NotifyLeadDaysCsv { get; set; } = DefaultNotifyLeadDaysCsv;

		/// <summary>Notify the holder directly from the worker, independent of the department notification rows.</summary>
		public bool NotifyCertificationHolder { get; set; } = true;

		/// <summary>A PendingVerification record counts as valid for role requirements.</summary>
		public bool TreatPendingVerificationAsValid { get; set; }

		/// <summary>One nightly summary (expired / expiring / in grace / removed) to the department admins.</summary>
		public bool SendAdminDigest { get; set; } = true;

		public DateTime UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		/// <summary>
		/// Department-local date of the last claimed expiry sweep (worker 34). The worker claims the date atomically before it
		/// runs, so a repeated or skipped local hour (clock drift, daylight-saving transitions, an overlapping tick) can neither
		/// send a day's expiring notifications twice nor miss the day. Owned by the worker; the settings page never writes it.
		/// </summary>
		public DateTime? LastSweepLocalDate { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentId; }
			set { DepartmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentCertificationSettings";

		[NotMapped]
		public string IdName => "DepartmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };

		/// <summary>Parsed, de-duplicated, positive lead days in descending order; the default schedule when the CSV is empty or malformed.</summary>
		public IReadOnlyList<int> GetNotifyLeadDays() => ParseLeadDays(NotifyLeadDaysCsv);

		public static IReadOnlyList<int> ParseLeadDays(string csv)
		{
			var days = new SortedSet<int>();
			foreach (var part in (csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
				if (int.TryParse(part.Trim(), out var day) && day > 0 && day <= 3650)
					days.Add(day);
			if (days.Count == 0)
				foreach (var part in DefaultNotifyLeadDaysCsv.Split(','))
					days.Add(int.Parse(part));
			var list = new List<int>(days);
			list.Reverse();
			return list;
		}
	}

	/// <summary>
	/// A unit-scoped typed credential or document with an expiry (plan D1.8): DOT annual inspection, registration,
	/// insurance, ambulance permit, pump/aerial test. Shares the type catalog, the expiry worker, notifications and the
	/// dashboard with personnel records; takes no part in role eligibility, verification or credit hours.
	/// Number, IssuedBy, Notes and Data are ADP catalog 27 protected fields (Unit family).
	/// </summary>
	[Table("UnitCertifications")]
	public class UnitCertification : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitCertificationId { get; set; }

		[Required]
		public int UnitId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int DepartmentCertificationTypeId { get; set; }

		public string Number { get; set; }

		public string IssuedBy { get; set; }

		public DateTime? IssuedOn { get; set; }

		public DateTime? ExpiresOn { get; set; }

		/// <summary><see cref="UnitCertificationStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime? StatusChangedOn { get; set; }

		public string StatusChangedByUserId { get; set; }

		public string StatusReason { get; set; }

		public string Notes { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }

		public int? FileSize { get; set; }

		public byte[] Data { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime? EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		public bool IsProtected { get; set; }

		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitCertificationId; }
			set { UnitCertificationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UnitCertifications";

		[NotMapped]
		public string IdName => "UnitCertificationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A continuing-education / CEU entry against a personnel certification (plan D1.4). Hours roll up against the
	/// type's RenewalCreditHoursRequired. Description and Data are ADP catalog 27 protected fields (Personnel family).
	/// </summary>
	[Table("PersonnelCertificationCredits")]
	public class PersonnelCertificationCredit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PersonnelCertificationCreditId { get; set; }

		[Required]
		public int PersonnelCertificationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public DateTime CreditDate { get; set; }

		public decimal Hours { get; set; }

		public string Category { get; set; }

		public string Description { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }

		public byte[] Data { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime AddedOn { get; set; }

		public bool IsProtected { get; set; }

		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PersonnelCertificationCreditId; }
			set { PersonnelCertificationCreditId = (int)value; }
		}

		[NotMapped]
		public string TableName => "PersonnelCertificationCredits";

		[NotMapped]
		public string IdName => "PersonnelCertificationCreditId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	// ---- Read models -----------------------------------------------------------------------------------

	/// <summary>One requirement's outcome for one member (plan D1.7).</summary>
	public sealed class CertificationRequirementOutcome
	{
		public int PersonnelRoleCertificationRequirementId { get; set; }
		public int DepartmentCertificationTypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public bool IsMandatory { get; set; }
		public int? AnyOfGroup { get; set; }
		public bool Satisfied { get; set; }
		/// <summary>The record that satisfied it, when one did.</summary>
		public int? PersonnelCertificationId { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>When the violation began: the record's expiry, or null when the member never held the type.</summary>
		public DateTime? ViolationStartedOn { get; set; }
		public int GraceDays { get; set; }
	}

	/// <summary>Evaluation of one member against one role (plan D4).</summary>
	public sealed class RoleCertificationEvaluation
	{
		public int PersonnelRoleId { get; set; }
		public string UserId { get; set; }
		public bool Qualified { get; set; }
		/// <summary>True when only optional requirements fail.</summary>
		public bool WarningsOnly { get; set; }
		public List<CertificationRequirementOutcome> Outcomes { get; set; } = new List<CertificationRequirementOutcome>();
		public List<CertificationRequirementOutcome> Violations { get; set; } = new List<CertificationRequirementOutcome>();
		/// <summary>The earliest date on which a mandatory violation's grace runs out; null when qualified or never qualified for a requirement with no grace anchor.</summary>
		public DateTime? RemovalDueOn { get; set; }
	}

	/// <summary>A row of the person × type or unit × type expiry matrix (plan D9).</summary>
	public sealed class CertificationDashboardCell
	{
		public string SubjectId { get; set; }
		public string SubjectName { get; set; }
		public int DepartmentCertificationTypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public int Category { get; set; }
		public int RecordId { get; set; }
		public int Status { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int? DaysUntilExpiry { get; set; }
		public bool NeverExpires { get; set; }
	}

	public sealed class CertificationExpiryDashboard
	{
		public List<DepartmentCertificationType> PersonTypes { get; set; } = new List<DepartmentCertificationType>();
		public List<DepartmentCertificationType> UnitTypes { get; set; } = new List<DepartmentCertificationType>();
		public List<CertificationDashboardCell> PersonCells { get; set; } = new List<CertificationDashboardCell>();
		public List<CertificationDashboardCell> UnitCells { get; set; } = new List<CertificationDashboardCell>();
		/// <summary>The "expiring within" window in days (the longest notification lead) the counts below use.</summary>
		public int Horizon { get; set; }
		public int ExpiredCount { get; set; }
		public int ExpiringCount { get; set; }
		public int SuspendedCount { get; set; }
		public int PendingVerificationCount { get; set; }

		/// <summary>
		/// Recomputes the four totals from the cells that are present, so a caller that trims the cells (the compliance
		/// report applies the personnel visibility matrix) does not keep department-wide numbers over a filtered matrix.
		/// </summary>
		public void RecountTotals()
		{
			var all = PersonCells.Concat(UnitCells).ToList();
			ExpiredCount = all.Count(c => c.Status == (int)PersonnelCertificationStatuses.Expired || (c.DaysUntilExpiry.HasValue && c.DaysUntilExpiry < 0));
			ExpiringCount = all.Count(c => c.Status == (int)PersonnelCertificationStatuses.Active && c.DaysUntilExpiry.HasValue && c.DaysUntilExpiry >= 0 && c.DaysUntilExpiry <= Horizon);
			SuspendedCount = PersonCells.Count(c => c.Status == (int)PersonnelCertificationStatuses.Suspended || c.Status == (int)PersonnelCertificationStatuses.Revoked) + UnitCells.Count(c => c.Status == (int)UnitCertificationStatuses.Suspended);
			PendingVerificationCount = PersonCells.Count(c => c.Status == (int)PersonnelCertificationStatuses.PendingVerification);
		}
	}

	/// <summary>A seed template for a department certification type (plan D1.2).</summary>
	public sealed class CertificationTypeTemplate
	{
		public string Id { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public CertificationCategories Category { get; set; }
		public CertificationAppliesTo AppliesTo { get; set; }
		public string IssuingAuthority { get; set; }
		public int? DefaultValidityMonths { get; set; }
		public bool NeverExpires { get; set; }
		public bool RequiresVerification { get; set; }
		public decimal? RenewalCreditHoursRequired { get; set; }
		public string Description { get; set; }
		public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();
	}
}
