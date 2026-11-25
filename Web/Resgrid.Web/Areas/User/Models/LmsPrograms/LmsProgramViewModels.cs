using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.LmsPrograms
{
	// Trainings → Programs (Training T3 part 2, LMS add-on): view models of LmsProgramsController. LMS layer (§17.3): only LMS pages use them.

	/// <summary>What every Programs page shows: the gate, the person's rights, names, categories and the one-time notice.</summary>
	public abstract class LmsProgramPageView
	{
		/// <summary>The add-on is active: new plans, requests, schedules and tags may be made (lapse rule 3).</summary>
		public bool CanAuthor { get; set; }
		public bool IsLapsed { get; set; }
		/// <summary>Department-wide training records manager or department admin: the officer pages.</summary>
		public bool IsOfficer { get; set; }
		/// <summary>May edit certification types (the cycle rules page).</summary>
		public bool CanManageCertifications { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();
		public Department Department { get; set; }
		public Func<DateTime?, string> Date { get; set; } = d => d?.ToShortDateString();

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		public string CategoryName(int? categoryId)
		{
			if (!categoryId.HasValue)
				return null;
			var category = Categories.Find(c => c.TrainingCategoryId == categoryId.Value);
			return category?.Name;
		}
	}

	public sealed class LmsProgramsHomeView : LmsProgramPageView
	{
		public int Plans { get; set; }
		public int Frameworks { get; set; }
		public int MyOpenRequests { get; set; }
		public int AwaitingMe { get; set; }
		public int SavedReports { get; set; }
		public int CycleRules { get; set; }
	}

	public sealed class LmsPlansView : LmsProgramPageView
	{
		public List<LmsTrainingPlan> Plans { get; set; } = new List<LmsTrainingPlan>();
		public List<DepartmentGroup> Groups { get; set; } = new List<DepartmentGroup>();
		public List<Shift> Shifts { get; set; } = new List<Shift>();
		public int DefaultYear { get; set; }
	}

	public sealed class LmsPlanView : LmsProgramPageView
	{
		public LmsTrainingPlanDashboard Dashboard { get; set; }
		public string ScopeName { get; set; }
		/// <summary>The line being edited, or a new one.</summary>
		public LmsTrainingPlanItem EditItem { get; set; }
	}

	public sealed class LmsFrameworksView : LmsProgramPageView
	{
		public List<LmsCompetencyFramework> Frameworks { get; set; } = new List<LmsCompetencyFramework>();
		public IReadOnlyList<LmsCompetencyCatalogFramework> Catalog { get; set; } = Array.Empty<LmsCompetencyCatalogFramework>();
		public LmsCaseImportResult Import { get; set; }
	}

	/// <summary>Something an item can be tagged on, for the picker.</summary>
	public sealed class LmsSubjectOption
	{
		public LmsCompetencySubjectKinds Kind { get; set; }
		public string Id { get; set; }
		public string Name { get; set; }
	}

	public sealed class LmsFrameworkView : LmsProgramPageView
	{
		public LmsCompetencyFramework Framework { get; set; }
		public List<(LmsCompetencyItem Item, int Depth)> Items { get; set; } = new List<(LmsCompetencyItem, int)>();
		public Dictionary<int, List<LmsCompetencyAlignment>> Tags { get; set; } = new Dictionary<int, List<LmsCompetencyAlignment>>();
		public List<LmsSubjectOption> Subjects { get; set; } = new List<LmsSubjectOption>();

		public string SubjectName(LmsCompetencyAlignment tag)
		{
			var option = Subjects.Find(s => (int)s.Kind == tag.SubjectKind && string.Equals(s.Id, tag.SubjectId, StringComparison.OrdinalIgnoreCase));
			return option?.Name ?? tag.SubjectId;
		}
	}

	public sealed class LmsGapsView : LmsProgramPageView
	{
		public List<LmsCompetencyFramework> Frameworks { get; set; } = new List<LmsCompetencyFramework>();
		public List<KeyValuePair<string, string>> Members { get; set; } = new List<KeyValuePair<string, string>>();
		public int? FrameworkId { get; set; }
		public string UserId { get; set; }
		public LmsCompetencyGapReport Report { get; set; }
	}

	public sealed class LmsRequestsView : LmsProgramPageView
	{
		public List<LmsTrainingRequest> Mine { get; set; } = new List<LmsTrainingRequest>();
		public List<LmsTrainingRequest> AwaitingMe { get; set; } = new List<LmsTrainingRequest>();
		/// <summary>Officers: every request of the year.</summary>
		public List<LmsTrainingRequest> All { get; set; } = new List<LmsTrainingRequest>();
		public LmsTrainingRequestCostSummary Costs { get; set; }
		public int Year { get; set; }
		public string Currency { get; set; }
	}

	public sealed class LmsRequestView : LmsProgramPageView
	{
		public LmsTrainingRequestView Request { get; set; }
	}

	public sealed class LmsReportsView : LmsProgramPageView
	{
		public IReadOnlyList<LmsReportDescriptor> Catalog { get; set; } = Array.Empty<LmsReportDescriptor>();
		public List<LmsReportFolder> Folders { get; set; } = new List<LmsReportFolder>();
		public List<LmsReportDefinition> Definitions { get; set; } = new List<LmsReportDefinition>();
	}

	public sealed class LmsReportRunView : LmsProgramPageView
	{
		public LmsReportDescriptor Descriptor { get; set; }
		public LmsReportParameters Parameters { get; set; } = new LmsReportParameters();
		public LmsReportTable Table { get; set; }
		/// <summary>The saved report being edited (or null for an ad-hoc run).</summary>
		public LmsReportDefinition Definition { get; set; }
		public List<LmsReportFolder> Folders { get; set; } = new List<LmsReportFolder>();
		public List<LmsReportRun> Runs { get; set; } = new List<LmsReportRun>();
		public List<LmsTrainingPlan> Plans { get; set; } = new List<LmsTrainingPlan>();
		public List<LmsCompetencyFramework> Frameworks { get; set; } = new List<LmsCompetencyFramework>();
		public List<KeyValuePair<string, string>> Members { get; set; } = new List<KeyValuePair<string, string>>();
		/// <summary>Who may receive scheduled reports (records managers and admins).</summary>
		public List<KeyValuePair<string, string>> Recipients { get; set; } = new List<KeyValuePair<string, string>>();
	}

	public sealed class LmsCyclesView : LmsProgramPageView
	{
		public List<DepartmentCertificationType> Types { get; set; } = new List<DepartmentCertificationType>();
		public Dictionary<int, LmsCertificationCycleRule> Rules { get; set; } = new Dictionary<int, LmsCertificationCycleRule>();
		public List<KeyValuePair<string, string>> Members { get; set; } = new List<KeyValuePair<string, string>>();
	}

	public sealed class LmsProgramSettingsView : LmsProgramPageView
	{
		public LmsProgramSettings Settings { get; set; }
	}

	/// <summary>A new or edited training request (the member's form).</summary>
	public sealed class LmsTrainingRequestInput
	{
		public string Title { get; set; }
		public string Provider { get; set; }
		public string Location { get; set; }
		public string CourseCode { get; set; }
		public DateTime? StartsOn { get; set; }
		public DateTime? EndsOn { get; set; }
		public decimal Hours { get; set; }
		public int? CategoryId { get; set; }
		public decimal CostTuition { get; set; }
		public decimal CostTravel { get; set; }
		public decimal CostOther { get; set; }
		public string Justification { get; set; }
	}

	public sealed class LmsPlanItemInput
	{
		public int Id { get; set; }
		public int PlanId { get; set; }
		public int Month { get; set; }
		public int? CategoryId { get; set; }
		public string Title { get; set; }
		public decimal PlannedHours { get; set; }
		public int PlannedSessions { get; set; } = 1;
		public DateTime? PlannedOn { get; set; }
		public string Notes { get; set; }
	}

	public sealed class LmsReportDefinitionInput
	{
		public int Id { get; set; }
		public string ReportKey { get; set; }
		public string Name { get; set; }
		public int? FolderId { get; set; }
		public int Format { get; set; }
		public int Frequency { get; set; }
		public int DayOfWeek { get; set; }
		public int DayOfMonth { get; set; } = 1;
		public int HourLocal { get; set; } = 6;
		public string[] Recipients { get; set; }
		public LmsReportParameters Parameters { get; set; } = new LmsReportParameters();
	}

	public sealed class LmsCycleRuleInput
	{
		public int TypeId { get; set; }
		public int AutoRenewMode { get; set; }
		public bool AlertOnCompletion { get; set; }
		public bool AlertOnRenewal { get; set; }
		public bool AlertOnExpired { get; set; }
		public string AlertLeadDays { get; set; }
		public bool NotifySupervisors { get; set; }
		public bool NotifyAdmins { get; set; }
		public string[] Named { get; set; }
	}
}
