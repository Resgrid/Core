using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Certifications
{
	// Workforce & Business Operations plan, Phase D9 (MVC).

	public class CertificationPageView
	{
		public bool CanView { get; set; }
		public bool CanManage { get; set; }
		public bool CanSetup { get; set; }
		public string Message { get; set; }
		public bool SaveSuccess { get; set; }
	}

	public class CertificationDashboardView : CertificationPageView
	{
		public CertificationExpiryDashboard Dashboard { get; set; } = new CertificationExpiryDashboard();
		public DepartmentCertificationSettings Settings { get; set; }
		public string Scope { get; set; } = "person";
		public int? Category { get; set; }
		public int? GroupId { get; set; }
		public int? RoleId { get; set; }
		public List<DepartmentGroup> Groups { get; set; } = new List<DepartmentGroup>();
		public List<PersonnelRole> Roles { get; set; } = new List<PersonnelRole>();
		/// <summary>Subject ids allowed by the group / role filter; null = no filter.</summary>
		public HashSet<string> SubjectFilter { get; set; }
		public int Horizon { get; set; } = 60;

		public IEnumerable<IGrouping<string, CertificationDashboardCell>> PersonRows => Dashboard.PersonCells
			.Where(c => SubjectFilter == null || SubjectFilter.Contains(c.SubjectId))
			.Where(c => !Category.HasValue || c.Category == Category.Value)
			.GroupBy(c => c.SubjectId).OrderBy(g => g.First().SubjectName);

		public IEnumerable<IGrouping<string, CertificationDashboardCell>> UnitRows => Dashboard.UnitCells
			.Where(c => !Category.HasValue || c.Category == Category.Value)
			.GroupBy(c => c.SubjectId).OrderBy(g => g.First().SubjectName);

		public List<DepartmentCertificationType> VisibleTypes => (Scope == "unit" ? Dashboard.UnitTypes : Dashboard.PersonTypes)
			.Where(t => !Category.HasValue || t.Category == Category.Value).ToList();
	}

	public class CertificationTypesView : CertificationPageView
	{
		public List<DepartmentCertificationType> Types { get; set; } = new List<DepartmentCertificationType>();
		public IReadOnlyDictionary<int, int> RecordCounts { get; set; } = new Dictionary<int, int>();
		public IReadOnlyDictionary<int, int> RequirementCounts { get; set; } = new Dictionary<int, int>();
		public bool ShowInactive { get; set; }
		public HashSet<string> ExistingCodes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	public class CertificationTypeEditView : CertificationPageView
	{
		public CertificationTypeInput Type { get; set; } = new CertificationTypeInput();
		public bool CodeLocked { get; set; }
		public bool ScopeLocked { get; set; }
		public bool IsNew => Type.DepartmentCertificationTypeId <= 0;
	}

	public class CertificationTypeInput
	{
		public int DepartmentCertificationTypeId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public int Category { get; set; } = (int)CertificationCategories.Other;
		public int AppliesTo { get; set; } = (int)CertificationAppliesTo.Person;
		public string Description { get; set; }
		public string IssuingAuthority { get; set; }
		public int? DefaultValidityMonths { get; set; }
		public bool NeverExpires { get; set; }
		public decimal? RenewalCreditHoursRequired { get; set; }
		public bool RequiresVerification { get; set; }
		public bool IsActive { get; set; } = true;

		public static CertificationTypeInput From(DepartmentCertificationType t) => new CertificationTypeInput
		{
			DepartmentCertificationTypeId = t.DepartmentCertificationTypeId, Name = t.Type, Code = t.Code, Category = t.Category, AppliesTo = t.AppliesTo, Description = t.Description, IssuingAuthority = t.IssuingAuthority,
			DefaultValidityMonths = t.DefaultValidityMonths, NeverExpires = t.NeverExpires, RenewalCreditHoursRequired = t.RenewalCreditHoursRequired, RequiresVerification = t.RequiresVerification, IsActive = t.IsActive
		};

		public void ApplyTo(DepartmentCertificationType t)
		{
			t.Type = Name; t.Code = Code; t.Category = Category; t.AppliesTo = AppliesTo; t.Description = Description; t.IssuingAuthority = IssuingAuthority;
			t.DefaultValidityMonths = DefaultValidityMonths; t.NeverExpires = NeverExpires; t.RenewalCreditHoursRequired = RenewalCreditHoursRequired; t.RequiresVerification = RequiresVerification; t.IsActive = IsActive;
		}
	}

	public class CertificationSettingsView : CertificationPageView
	{
		public CertificationSettingsInput Settings { get; set; } = new CertificationSettingsInput();
		public int MandatoryRequirementCount { get; set; }
	}

	public class CertificationSettingsInput
	{
		public int EnforcementMode { get; set; }
		public int RoleRemovalGraceDays { get; set; } = DepartmentCertificationSettings.DefaultRoleRemovalGraceDays;
		public string NotifyLeadDaysCsv { get; set; } = DepartmentCertificationSettings.DefaultNotifyLeadDaysCsv;
		public bool NotifyCertificationHolder { get; set; } = true;
		public bool TreatPendingVerificationAsValid { get; set; }
		public bool SendAdminDigest { get; set; } = true;
		public bool ConfirmEnforce { get; set; }

		public static CertificationSettingsInput From(DepartmentCertificationSettings s) => new CertificationSettingsInput
		{
			EnforcementMode = s.EnforcementMode, RoleRemovalGraceDays = s.RoleRemovalGraceDays, NotifyLeadDaysCsv = s.NotifyLeadDaysCsv, NotifyCertificationHolder = s.NotifyCertificationHolder,
			TreatPendingVerificationAsValid = s.TreatPendingVerificationAsValid, SendAdminDigest = s.SendAdminDigest
		};
	}

	public class RoleRequirementsView : CertificationPageView
	{
		public PersonnelRole Role { get; set; }
		public List<DepartmentCertificationType> Types { get; set; } = new List<DepartmentCertificationType>();
		public List<PersonnelRoleCertificationRequirement> Requirements { get; set; } = new List<PersonnelRoleCertificationRequirement>();
		public List<RoleCertificationEvaluation> Evaluations { get; set; } = new List<RoleCertificationEvaluation>();
		public IReadOnlyDictionary<string, string> MemberNames { get; set; } = new Dictionary<string, string>();
		public int EnforcementMode { get; set; }
	}

	/// <summary>One posted requirement row (rows are posted as Requirements[i].Field).</summary>
	public class RoleRequirementRowInput
	{
		public int TypeId { get; set; }
		public bool IsMandatory { get; set; } = true;
		public int? AnyOfGroup { get; set; }
		public bool AllowTrainee { get; set; }
		public int? GraceDaysOverride { get; set; }
	}

	public class UnitCertificationsView : CertificationPageView
	{
		public Unit Unit { get; set; }
		public List<UnitCertification> Records { get; set; } = new List<UnitCertification>();
		public List<DepartmentCertificationType> Types { get; set; } = new List<DepartmentCertificationType>();
		public IReadOnlyDictionary<int, DepartmentCertificationType> TypeMap => Types.ToDictionary(t => t.DepartmentCertificationTypeId);
	}

	public class UnitCertificationInput
	{
		public int UnitCertificationId { get; set; }
		public int UnitId { get; set; }
		public int DepartmentCertificationTypeId { get; set; }
		public string Number { get; set; }
		public string IssuedBy { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string Notes { get; set; }
	}

	public class CertificationRecordView : CertificationPageView
	{
		public PersonnelCertification Record { get; set; }
		public DepartmentCertificationType Type { get; set; }
		public string HolderName { get; set; }
		public bool IsSelf { get; set; }
		public List<PersonnelCertificationCredit> Credits { get; set; } = new List<PersonnelCertificationCredit>();
		public decimal CreditHours => Credits.Sum(c => c.Hours);
		public int? DaysUntilExpiry { get; set; }
		public string VerifiedByName { get; set; }
	}

	public class CertificationCreditInput
	{
		public int PersonnelCertificationId { get; set; }
		public DateTime? CreditDate { get; set; }
		public decimal Hours { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
	}

	public class CertificationComplianceReportView
	{
		public Department Department { get; set; }
		public DateTime RunOn { get; set; }
		public CertificationExpiryDashboard Dashboard { get; set; } = new CertificationExpiryDashboard();
		public DepartmentCertificationSettings Settings { get; set; }
		public IEnumerable<IGrouping<string, CertificationDashboardCell>> PersonRows => Dashboard.PersonCells.GroupBy(c => c.SubjectId).OrderBy(g => g.First().SubjectName);
		public IEnumerable<IGrouping<string, CertificationDashboardCell>> UnitRows => Dashboard.UnitCells.GroupBy(c => c.SubjectId).OrderBy(g => g.First().SubjectName);
	}
}
