using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.Lms
{
	/// <summary>Common to the Trainings → Programs pages (Training T3, LMS).</summary>
	public class LmsProgramsPage
	{
		public string Message { get; set; }
		public string Error { get; set; }
		public LmsActor Actor { get; set; }
		public TrainingManagementScope Scope { get; set; }
		public Department Department { get; set; }

		/// <summary>The add-on lapsed: the engine is paused and the pages are read-only (rule 5).</summary>
		public bool Paused => Actor?.Gate != null && Actor.Gate.IsLapsed;

		public bool CanEdit => Actor != null && Actor.MayManage && Actor.Gate != null && Actor.Gate.CanAuthor;

		/// <summary>A UTC moment as a department-local date.</summary>
		public string Date(DateTime? utc)
		{
			if (!utc.HasValue)
				return null;
			return Department == null ? utc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(Department).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		}
	}

	public class LmsOption
	{
		public LmsOption() { }

		public LmsOption(string value, string text)
		{
			Value = value;
			Text = text;
		}

		public string Value { get; set; }
		public string Text { get; set; }
	}

	public sealed class LmsRequirementsIndexView : LmsProgramsPage
	{
		public List<LmsRequirementRow> Rows { get; set; } = new List<LmsRequirementRow>();
		public bool OnboardingOnly { get; set; }
		public Dictionary<string, string> AudienceNames { get; set; } = new Dictionary<string, string>();
	}

	/// <summary>The requirement editor's posted form.</summary>
	public sealed class LmsRequirementForm
	{
		public int LmsRequirementId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int Kind { get; set; }
		public int? CourseId { get; set; }
		public int? TrainingId { get; set; }
		public int? CategoryId { get; set; }
		public int? PathId { get; set; }
		public string SkillCode { get; set; }
		public decimal? RequiredHours { get; set; }
		public int? RequiredCount { get; set; }
		public int Recurrence { get; set; }
		public int? IntervalMonths { get; set; }
		public int DueDays { get; set; } = LmsRequirement.DefaultDueDays;
		public int GraceDays { get; set; }
		public int? AtRiskDays { get; set; }
		public bool NewMembersOnly { get; set; }
		public bool AutoAssign { get; set; }
		public bool IsOnboarding { get; set; }
		public int? BacksCertificationTypeId { get; set; }
		public bool EnforcementEnabled { get; set; }
		public bool AlertSupervisors { get; set; }
		public List<string> AlertUserIds { get; set; } = new List<string>();
		public bool AlertOnCompletion { get; set; }
		public bool IsActive { get; set; } = true;
		/// <summary>A Course or Path requirement's choice for a major Academy update of a lesson it requires (LmsMajorChangePolicies).</summary>
		public int AcademyUpdatePolicy { get; set; } = (int)LmsMajorChangePolicies.Notify;
		public bool Everyone { get; set; }
		public List<int> Roles { get; set; } = new List<int>();
		public List<int> Groups { get; set; } = new List<int>();
		public List<int> CertificationTypes { get; set; } = new List<int>();
		public List<int> Shifts { get; set; } = new List<int>();
		public List<string> Members { get; set; } = new List<string>();

		public List<ContentAudienceRule> Rules()
		{
			if (Everyone)
				return new List<ContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.Everyone) };
			string Id(int value) => value.ToString(CultureInfo.InvariantCulture);
			return (Roles ?? new List<int>()).Where(v => v > 0).Select(v => new ContentAudienceRule(ContentAudienceKinds.Role, Id(v)))
				.Concat((Groups ?? new List<int>()).Where(v => v > 0).Select(v => new ContentAudienceRule(ContentAudienceKinds.Group, Id(v))))
				.Concat((CertificationTypes ?? new List<int>()).Where(v => v > 0).Select(v => new ContentAudienceRule(ContentAudienceKinds.CertificationType, Id(v))))
				.Concat((Shifts ?? new List<int>()).Where(v => v > 0).Select(v => new ContentAudienceRule(ContentAudienceKinds.Shift, Id(v))))
				.Concat((Members ?? new List<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => new ContentAudienceRule(ContentAudienceKinds.User, m.Trim())))
				.ToList();
		}

		public LmsRequirementInput ToInput() => new LmsRequirementInput
		{
			LmsRequirementId = LmsRequirementId,
			Name = Name,
			Description = Description,
			Kind = (LmsRequirementKinds)Kind,
			TargetRefId = (LmsRequirementKinds)Kind switch
			{
				LmsRequirementKinds.Course => CourseId,
				LmsRequirementKinds.Training => TrainingId,
				LmsRequirementKinds.CategoryHours => CategoryId,
				LmsRequirementKinds.Path => PathId,
				_ => null
			},
			TargetCode = SkillCode,
			RequiredHours = RequiredHours,
			RequiredCount = RequiredCount,
			Recurrence = (LmsRequirementRecurrences)Recurrence,
			IntervalMonths = IntervalMonths,
			DueDays = DueDays,
			GraceDays = GraceDays,
			AtRiskDays = AtRiskDays,
			NewMembersOnly = NewMembersOnly,
			AutoAssign = AutoAssign,
			IsOnboarding = IsOnboarding,
			BacksCertificationTypeId = BacksCertificationTypeId,
			EnforcementEnabled = EnforcementEnabled,
			AlertSupervisors = AlertSupervisors,
			AlertUserIds = AlertUserIds ?? new List<string>(),
			AlertOnCompletion = AlertOnCompletion,
			IsActive = IsActive,
			AcademyUpdatePolicy = Enum.IsDefined(typeof(LmsMajorChangePolicies), AcademyUpdatePolicy) ? (LmsMajorChangePolicies)AcademyUpdatePolicy : LmsMajorChangePolicies.Notify,
			Audience = Rules()
		};

		public static LmsRequirementForm From(LmsRequirement requirement, IEnumerable<LmsRequirementAudience> audience)
		{
			var form = new LmsRequirementForm();
			if (requirement == null)
				return form;
			form.LmsRequirementId = requirement.LmsRequirementId;
			form.Name = requirement.Name;
			form.Description = requirement.Description;
			form.Kind = requirement.Kind;
			switch ((LmsRequirementKinds)requirement.Kind)
			{
				case LmsRequirementKinds.Course: form.CourseId = requirement.TargetRefId; break;
				case LmsRequirementKinds.Training: form.TrainingId = requirement.TargetRefId; break;
				case LmsRequirementKinds.CategoryHours: form.CategoryId = requirement.TargetRefId; break;
				case LmsRequirementKinds.Path: form.PathId = requirement.TargetRefId; break;
			}
			form.SkillCode = requirement.TargetCode;
			form.RequiredHours = requirement.RequiredHours;
			form.RequiredCount = requirement.RequiredCount;
			form.Recurrence = requirement.Recurrence;
			form.IntervalMonths = requirement.IntervalMonths;
			form.DueDays = requirement.DueDays;
			form.GraceDays = requirement.GraceDays;
			form.AtRiskDays = requirement.AtRiskDays;
			form.NewMembersOnly = requirement.NewMembersOnly;
			form.AutoAssign = requirement.AutoAssign;
			form.IsOnboarding = requirement.IsOnboarding;
			form.BacksCertificationTypeId = requirement.BacksCertificationTypeId;
			form.EnforcementEnabled = requirement.EnforcementEnabled;
			form.AlertSupervisors = requirement.AlertSupervisors;
			form.AlertUserIds = (requirement.AlertUserIds ?? string.Empty).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
			form.AlertOnCompletion = requirement.AlertOnCompletion;
			form.IsActive = requirement.IsActive;
			form.AcademyUpdatePolicy = requirement.AcademyUpdatePolicy;
			foreach (var rule in audience ?? Enumerable.Empty<LmsRequirementAudience>())
			{
				var id = ContentAudienceMatcher.TryId(rule, out var value) ? value : 0;
				switch ((ContentAudienceKinds)rule.Kind)
				{
					case ContentAudienceKinds.Everyone: form.Everyone = true; break;
					case ContentAudienceKinds.Role: form.Roles.Add(id); break;
					case ContentAudienceKinds.Group: form.Groups.Add(id); break;
					case ContentAudienceKinds.CertificationType: form.CertificationTypes.Add(id); break;
					case ContentAudienceKinds.Shift: form.Shifts.Add(id); break;
					case ContentAudienceKinds.User: form.Members.Add(rule.RefId); break;
				}
			}
			return form;
		}
	}

	public sealed class LmsRequirementEditView : LmsProgramsPage
	{
		public LmsRequirementForm Form { get; set; } = new LmsRequirementForm();
		public bool IsNew => Form == null || Form.LmsRequirementId <= 0;
		public List<LmsOption> Courses { get; set; } = new List<LmsOption>();
		public List<LmsOption> Trainings { get; set; } = new List<LmsOption>();
		public List<LmsOption> Categories { get; set; } = new List<LmsOption>();
		public List<LmsOption> Paths { get; set; } = new List<LmsOption>();
		public List<LmsOption> CertificationTypes { get; set; } = new List<LmsOption>();
		public List<LmsOption> ExpiringCertificationTypes { get; set; } = new List<LmsOption>();
		public List<LmsOption> Roles { get; set; } = new List<LmsOption>();
		public List<LmsOption> Groups { get; set; } = new List<LmsOption>();
		public List<LmsOption> Shifts { get; set; } = new List<LmsOption>();
		public List<LmsOption> Members { get; set; } = new List<LmsOption>();
	}

	public sealed class LmsComplianceView : LmsProgramsPage
	{
		public LmsComplianceMatrix Matrix { get; set; } = new LmsComplianceMatrix();
		public int? RequirementId { get; set; }
		public int? GroupId { get; set; }
		public int? State { get; set; }
		public List<LmsOption> Groups { get; set; } = new List<LmsOption>();
		public bool CanChangeMembers { get; set; }
	}

	public sealed class LmsPathsIndexView : LmsProgramsPage
	{
		public List<LmsPathRow> Rows { get; set; } = new List<LmsPathRow>();
	}

	/// <summary>A path step row as the editor posts it.</summary>
	public sealed class LmsPathStepForm
	{
		public int Kind { get; set; }
		public int? CourseId { get; set; }
		public int? TrainingId { get; set; }
		public string Prerequisites { get; set; }
		public int? ElectiveGroup { get; set; }
		public int ElectiveRequired { get; set; }
		public bool Remove { get; set; }
	}

	public sealed class LmsPathForm
	{
		public int LmsPathId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int? IssuesCertificationTypeId { get; set; }
		public List<LmsPathStepForm> Steps { get; set; } = new List<LmsPathStepForm>();

		public LmsPathInput ToInput() => new LmsPathInput
		{
			LmsPathId = LmsPathId,
			Name = Name,
			Description = Description,
			IssuesCertificationTypeId = IssuesCertificationTypeId,
			Steps = (Steps ?? new List<LmsPathStepForm>()).Where(s => s != null && !s.Remove && ((LmsPathStepKinds)s.Kind == LmsPathStepKinds.Training ? s.TrainingId > 0 : s.CourseId > 0))
				.Select(s => new LmsPathStepInput
				{
					Kind = (LmsPathStepKinds)s.Kind,
					TargetRefId = (LmsPathStepKinds)s.Kind == LmsPathStepKinds.Training ? s.TrainingId ?? 0 : s.CourseId ?? 0,
					Prerequisites = s.Prerequisites,
					ElectiveGroup = s.ElectiveGroup,
					ElectiveRequired = s.ElectiveRequired
				}).ToList()
		};
	}

	public sealed class LmsPathEditView : LmsProgramsPage
	{
		public LmsPathDetail Detail { get; set; }
		public LmsPathForm Form { get; set; } = new LmsPathForm();
		public List<LmsPathProgressRow> Progress { get; set; } = new List<LmsPathProgressRow>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<LmsOption> Courses { get; set; } = new List<LmsOption>();
		public List<LmsOption> Trainings { get; set; } = new List<LmsOption>();
		public List<LmsOption> CertificationTypes { get; set; } = new List<LmsOption>();
		public List<LmsOption> Roles { get; set; } = new List<LmsOption>();
		public List<LmsOption> Groups { get; set; } = new List<LmsOption>();
		public List<LmsOption> Members { get; set; } = new List<LmsOption>();
		public bool Editing { get; set; }
	}

	/// <summary>One step row of the path editor (Index -1: the template the script clones).</summary>
	public sealed class LmsPathStepRowView
	{
		public int Index { get; set; }
		public LmsPathStepForm Step { get; set; }
		public List<LmsOption> Courses { get; set; } = new List<LmsOption>();
		public List<LmsOption> Trainings { get; set; } = new List<LmsOption>();
	}

	public sealed class LmsEnforcementView : LmsProgramsPage
	{
		public LearningRequirementSettings Settings { get; set; } = new LearningRequirementSettings();
		public LmsEnforcementPreview Preview { get; set; } = new LmsEnforcementPreview();
		public List<LmsRequirementEnforcement> History { get; set; } = new List<LmsRequirementEnforcement>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<int, string> RequirementNames { get; set; } = new Dictionary<int, string>();
	}

	public sealed class LmsSettingsForm
	{
		public bool EnforcementEnabled { get; set; }
		public int MaxRoleRemovalSharePercent { get; set; } = LearningRequirementSettings.DefaultMaxRoleRemovalSharePercent;
		public int DefaultAtRiskDays { get; set; } = LearningRequirementSettings.StandardAtRiskDays;
		public bool ConfirmPreview { get; set; }
	}

	/// <summary>The My Training panel (view component): the member's requirements and paths, with the next action first.</summary>
	public sealed class LmsMyTrainingPanel : LmsProgramsPage
	{
		public List<LmsMyRequirement> Requirements { get; set; } = new List<LmsMyRequirement>();
		public List<LmsMyPath> Paths { get; set; } = new List<LmsMyPath>();
		public bool CanSeeCompliance { get; set; }
	}
}
