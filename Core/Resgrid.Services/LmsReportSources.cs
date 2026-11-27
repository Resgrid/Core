using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	// The LMS report catalog (§13a.3): each report is one ILmsReportSource. Free reports (hours) are listed so the catalog is complete
	// and can be saved and scheduled with the add-on; their own pages stay free in Trainings → Records. Another stream registers its own
	// source the same way (4B: "compliance-matrix").

	internal static class LmsReportCells
	{
		public static string Hours(decimal value, CultureInfo culture) => value.ToString("0.##", culture);
		public static string Money(decimal value, CultureInfo culture) => value.ToString("N2", culture);
		public static string Date(DateTime? value, CultureInfo culture) => value.HasValue ? value.Value.ToString("d", culture) : string.Empty;

		public static Dictionary<string, string> Names(IEnumerable<PersonName> names) =>
			(names ?? Enumerable.Empty<PersonName>()).Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>Hours by category, member or month (T1, free).</summary>
	public class LmsHoursReportSource : ILmsReportSource
	{
		private readonly ILearningRecordService _learningRecords;

		public LmsHoursReportSource(ILearningRecordService learningRecords) { _learningRecords = learningRecords; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "training-hours", NameKey = "Prg.Report.Hours", DescriptionKey = "Prg.Report.HoursHelp", Tier = LmsReportTiers.Free, UsesPeriod = true, UsesCategory = true,
			Groupings = new Dictionary<int, string> { { 0, "Prg.GroupBy.Category" }, { 1, "Prg.GroupBy.Member" }, { 2, "Prg.GroupBy.Month" } }
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var groupBy = Enum.IsDefined(typeof(TrainingHoursGroupings), request.Parameters.GroupBy) ? (TrainingHoursGroupings)request.Parameters.GroupBy : TrainingHoursGroupings.Category;
			var report = await _learningRecords.GetHoursReportAsync(new TrainingHoursReportQuery
			{
				DepartmentId = request.DepartmentId,
				From = request.FromUtc,
				To = request.ToUtc,
				GroupBy = groupBy,
				CategoryIds = request.Parameters.CategoryId.HasValue ? new List<int> { request.Parameters.CategoryId.Value } : null,
				IncludeFormerMembers = request.Parameters.IncludeFormerMembers
			}) ?? new TrainingHoursReport();

			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text(groupBy == TrainingHoursGroupings.Member ? "Prg.Col.Member" : groupBy == TrainingHoursGroupings.Month ? "Prg.Col.Month" : "Prg.Col.Category"),
					text("Prg.Col.Records"), text("Prg.Col.Members"), text("Prg.Col.Hours"), text("Prg.Col.CeHours"), text("Prg.Col.Target") }
			};
			foreach (var row in report.Rows)
				table.Rows.Add(new List<string>
				{
					string.IsNullOrEmpty(row.Label) ? text("Prg.Uncategorized") : row.Label, row.Records.ToString(request.Culture), row.Members.ToString(request.Culture),
					LmsReportCells.Hours(row.Hours, request.Culture), LmsReportCells.Hours(row.CeHours, request.Culture),
					row.TargetHours.HasValue ? LmsReportCells.Hours(row.TargetHours.Value, request.Culture) : string.Empty
				});
			table.Footer.Add(string.Format(request.Culture, text("Prg.Footer.TotalHours"), LmsReportCells.Hours(report.TotalHours, request.Culture), report.TotalRecords));
			return table;
		}
	}

	/// <summary>The formal ISO FSRS training report (T1, LMS).</summary>
	public class LmsIsoReportSource : ILmsReportSource
	{
		private readonly ILmsTrainingService _lmsTraining;

		public LmsIsoReportSource(ILmsTrainingService lmsTraining) { _lmsTraining = lmsTraining; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "iso-fsrs", NameKey = "Prg.Report.Iso", DescriptionKey = "Prg.Report.IsoHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var report = await _lmsTraining.GetIsoFsrsReportAsync(request.DepartmentId, request.FromUtc, request.ToUtc, request.Parameters.IncludeFormerMembers) ?? new IsoFsrsReport();
			var table = new LmsReportTable { Title = text(Descriptor.NameKey) };
			table.Columns.Add(text("Prg.Col.Member"));
			foreach (var category in report.Categories)
				table.Columns.Add(string.IsNullOrWhiteSpace(category.Code) ? category.Name : category.Code);
			table.Columns.Add(text("Prg.Col.Hours"));
			foreach (var row in report.Rows)
			{
				var cells = new List<string> { row.NotLicensed ? row.Name + " (" + text("Prg.NotLicensed") + ")" : row.Name };
				foreach (var category in report.Categories)
					cells.Add(LmsReportCells.Hours(row.HoursByCode.TryGetValue(category.Code ?? category.Name, out var h) ? h : 0m, request.Culture));
				cells.Add(LmsReportCells.Hours(row.HoursByCode.Values.Sum(), request.Culture));
				table.Rows.Add(cells);
			}
			foreach (var category in report.Categories.Where(c => c.TargetHours.HasValue))
				table.Footer.Add((category.Code ?? category.Name) + ": " + string.Format(request.Culture, text("Prg.Footer.Target"), LmsReportCells.Hours(category.TargetHours.Value, request.Culture), category.TargetAppliesTo));
			return table;
		}
	}

	/// <summary>NREMT NCCP progress per member and cycle (T1, LMS).</summary>
	public class LmsNccpProgressReportSource : ILmsReportSource
	{
		private readonly ILmsTrainingService _lmsTraining;

		public LmsNccpProgressReportSource(ILmsTrainingService lmsTraining) { _lmsTraining = lmsTraining; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "nccp-progress", NameKey = "Prg.Report.Nccp", DescriptionKey = "Prg.Report.NccpHelp", Tier = LmsReportTiers.Lms
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var report = await _lmsTraining.GetNccpProgressAsync(request.DepartmentId, request.Parameters.IncludeFormerMembers, request.UtcNow) ?? new NccpProgressReport();
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text("Prg.Col.Member"), text("Prg.Col.Certification"), text("Prg.Col.CycleStart"), text("Prg.Col.CycleEnd"), text("Prg.Col.NccpNational"),
					text("Prg.Col.NccpLocal"), text("Prg.Col.NccpIndividual") }
			};
			if (!report.CategoriesApplied)
				table.Problem = "Prg.Problem.NccpCategories";
			foreach (var row in report.Rows)
				table.Rows.Add(new List<string>
				{
					row.NotLicensed ? row.Name + " (" + text("Prg.NotLicensed") + ")" : row.Name, row.CertificationName,
					LmsReportCells.Date(row.CycleStart, request.Culture), LmsReportCells.Date(row.CycleEnd, request.Culture),
					Of(row.National, row.Target?.National, request.Culture), Of(row.Local, row.Target?.Local, request.Culture), Of(row.Individual, row.Target?.Individual, request.Culture)
				});
			return table;
		}

		private static string Of(decimal value, decimal? target, CultureInfo culture) =>
			target.HasValue ? LmsReportCells.Hours(value, culture) + " / " + LmsReportCells.Hours(target.Value, culture) : LmsReportCells.Hours(value, culture);
	}

	/// <summary>
	/// The per-member NCCP export (§13a.1, LMS): every verified record in the member's NCCP categories in their current cycle, with the CE
	/// provider and course number, ready for the member's own NREMT recertification entry.
	/// </summary>
	public class LmsNccpMemberExportSource : ILmsReportSource
	{
		private readonly ILmsTrainingService _lmsTraining;
		private readonly ILearningRecordService _learningRecords;
		private readonly IDepartmentsService _departments;

		public LmsNccpMemberExportSource(ILmsTrainingService lmsTraining, ILearningRecordService learningRecords, IDepartmentsService departments)
		{
			_lmsTraining = lmsTraining;
			_learningRecords = learningRecords;
			_departments = departments;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "nccp-member", NameKey = "Prg.Report.NccpMember", DescriptionKey = "Prg.Report.NccpMemberHelp", Tier = LmsReportTiers.Lms, NeedsMember = true, UsesPeriod = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text("Prg.Col.Component"), text("Prg.Col.Date"), text("Prg.Col.Title"), text("Prg.Col.Hours"), text("Prg.Col.CeHours"), text("Prg.Col.CeProvider"),
					text("Prg.Col.CeCourseNumber"), text("Prg.Col.CourseCode") }
			};
			var userId = request.Parameters.UserId;
			if (string.IsNullOrWhiteSpace(userId))
			{
				table.Problem = "Prg.Problem.NeedsMember";
				return table;
			}

			var categories = await _learningRecords.GetCategoriesAsync(request.DepartmentId) ?? new List<TrainingCategory>();
			var components = new Dictionary<int, string>();
			foreach (var (code, key) in new[] { (TrainingCategoryTemplateCatalog.NccpNationalCode, "Prg.Col.NccpNational"), (TrainingCategoryTemplateCatalog.NccpLocalCode, "Prg.Col.NccpLocal"),
				         (TrainingCategoryTemplateCatalog.NccpIndividualCode, "Prg.Col.NccpIndividual") })
			{
				var category = categories.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
				if (category != null)
					components[category.TrainingCategoryId] = text(key);
			}
			if (components.Count == 0)
			{
				table.Problem = "Prg.Problem.NccpCategories";
				return table;
			}

			// The member's current cycle bounds the export when they hold an NCCP-level certification; otherwise the chosen period does.
			var from = (DateTime?)request.FromUtc;
			var to = (DateTime?)request.ToUtc;
			var progress = await _lmsTraining.GetNccpProgressAsync(request.DepartmentId, true, request.UtcNow) ?? new NccpProgressReport();
			var cycle = progress.Rows.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.CycleEnd ?? DateTime.MaxValue).FirstOrDefault();
			if (cycle != null && cycle.CycleStart.HasValue)
			{
				from = cycle.CycleStart.Value.Date;
				to = cycle.CycleEnd?.Date.AddDays(1);
				table.Subtitle = cycle.CertificationName + ": " + LmsReportCells.Date(cycle.CycleStart, request.Culture) + " - " + LmsReportCells.Date(cycle.CycleEnd, request.Culture);
			}

			var records = (await _learningRecords.GetTranscriptAsync(request.DepartmentId, userId, from, to, components.Keys, null, false) ?? new List<LearningRecord>())
				.Where(r => r.Counts && r.CategoryId.HasValue && components.ContainsKey(r.CategoryId.Value) && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.OrderBy(r => r.CompletedOn).ToList();
			foreach (var record in records)
				table.Rows.Add(new List<string>
				{
					components[record.CategoryId.Value], LmsReportCells.Date(record.CompletedOn, request.Culture), record.Title, LmsReportCells.Hours(record.Hours, request.Culture),
					record.CeHours.HasValue ? LmsReportCells.Hours(record.CeHours.Value, request.Culture) : string.Empty, record.CeProvider, record.CeCourseNumber, record.CourseCode
				});

			var names = LmsReportCells.Names(await _departments.GetAllPersonnelNamesForDepartmentAsync(request.DepartmentId));
			table.Title += " - " + (names.TryGetValue(userId, out var name) ? name : userId);
			foreach (var component in components)
				table.Footer.Add(component.Value + ": " + LmsReportCells.Hours(records.Where(r => r.CategoryId == component.Key).Sum(r => r.Hours), request.Culture));
			return table;
		}
	}

	/// <summary>The annual plan against actual (R-C5, LMS).</summary>
	public class LmsTrainingPlanReportSource : ILmsReportSource
	{
		private readonly ILmsTrainingPlanService _plans;

		public LmsTrainingPlanReportSource(ILmsTrainingPlanService plans) { _plans = plans; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "training-plan", NameKey = "Prg.Report.Plan", DescriptionKey = "Prg.Report.PlanHelp", Tier = LmsReportTiers.Lms, NeedsPlan = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text("Prg.Col.Category"), text("Prg.Col.Target"), text("Prg.Col.PlannedYear"), text("Prg.Col.ActualToDate"), text("Prg.Col.Forecast"),
					text("Prg.Col.Shortfall"), text("Prg.Col.Status") }
			};
			if (!request.Parameters.PlanId.HasValue || await _plans.GetPlanAsync(request.DepartmentId, request.Parameters.PlanId.Value) == null)
			{
				table.Problem = "Prg.Problem.NeedsPlan";
				return table;
			}

			var dashboard = await _plans.GetDashboardAsync(request.DepartmentId, request.Parameters.PlanId.Value, request.UtcNow);
			table.Title += " - " + dashboard.Plan.Name + " " + dashboard.Plan.Year.ToString(CultureInfo.InvariantCulture);
			table.Subtitle = string.Format(request.Culture, text("Prg.Footer.Audience"), dashboard.MemberCount);
			foreach (var row in dashboard.Rows)
				table.Rows.Add(new List<string>
				{
					row.CategoryName ?? text("Prg.Uncategorized"), row.AnnualTarget.HasValue ? LmsReportCells.Hours(row.AnnualTarget.Value, request.Culture) : string.Empty,
					LmsReportCells.Hours(row.PlannedYear, request.Culture), LmsReportCells.Hours(row.ActualToDate, request.Culture), LmsReportCells.Hours(row.Forecast, request.Culture),
					row.Shortfall > 0 ? LmsReportCells.Hours(row.Shortfall, request.Culture) : string.Empty,
					row.AtRisk ? text("Prg.AtRisk") : row.AnnualTarget.HasValue ? text("Prg.OnTrack") : string.Empty
				});
			return table;
		}
	}

	/// <summary>A member's competency gap list (R-C7, LMS).</summary>
	public class LmsCompetencyGapReportSource : ILmsReportSource
	{
		private readonly ILmsCompetencyService _competencies;

		public LmsCompetencyGapReportSource(ILmsCompetencyService competencies) { _competencies = competencies; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "competency-gaps", NameKey = "Prg.Report.Gaps", DescriptionKey = "Prg.Report.GapsHelp", Tier = LmsReportTiers.Lms, NeedsFramework = true, NeedsMember = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text("Prg.Col.Code"), text("Prg.Col.Competency"), text("Prg.Col.Status"), text("Prg.Col.EvidenceDate"), text("Prg.Col.AgeDays"), text("Prg.Col.Evidence") }
			};
			if (!request.Parameters.FrameworkId.HasValue || await _competencies.GetFrameworkAsync(request.DepartmentId, request.Parameters.FrameworkId.Value) == null)
			{
				table.Problem = "Prg.Problem.NeedsFramework";
				return table;
			}
			if (string.IsNullOrWhiteSpace(request.Parameters.UserId))
			{
				table.Problem = "Prg.Problem.NeedsMember";
				return table;
			}

			var report = await _competencies.GetMemberGapsAsync(request.DepartmentId, request.Parameters.FrameworkId.Value, request.Parameters.UserId, request.UtcNow);
			table.Title += " - " + report.MemberName + " - " + report.Framework.Name;
			foreach (var row in report.Rows)
				table.Rows.Add(new List<string>
				{
					row.Code, new string(' ', Math.Min(row.Depth, 6) * 2) + row.Label, text("Prg.Evidence." + row.State), LmsReportCells.Date(row.EvidenceOn, request.Culture),
					row.AgeDays?.ToString(request.Culture) ?? string.Empty, row.EvidenceTitle
				});
			table.Footer.Add(string.Format(request.Culture, text("Prg.Footer.Gaps"), report.Current, report.Aged, report.Missing));
			return table;
		}
	}

	/// <summary>Training request cost totals per member, course or year (§13a.2, R-C10, LMS).</summary>
	public class LmsTrainingRequestCostReportSource : ILmsReportSource
	{
		private readonly ILmsTrainingRequestService _requests;

		public LmsTrainingRequestCostReportSource(ILmsTrainingRequestService requests) { _requests = requests; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "training-request-costs", NameKey = "Prg.Report.Costs", DescriptionKey = "Prg.Report.CostsHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true,
			Groupings = new Dictionary<int, string> { { 0, "Prg.GroupBy.Member" }, { 1, "Prg.GroupBy.Course" }, { 2, "Prg.GroupBy.Year" } }
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var groupBy = Enum.IsDefined(typeof(LmsTrainingRequestCostGroupings), request.Parameters.GroupBy) ? (LmsTrainingRequestCostGroupings)request.Parameters.GroupBy : LmsTrainingRequestCostGroupings.Member;
			var summary = await _requests.GetCostSummaryAsync(request.DepartmentId, request.FromUtc, request.ToUtc, groupBy);
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text(groupBy == LmsTrainingRequestCostGroupings.Member ? "Prg.Col.Member" : groupBy == LmsTrainingRequestCostGroupings.Course ? "Prg.Col.Course" : "Prg.Col.Year"),
					text("Prg.Col.Requests"), text("Prg.Col.Committed"), text("Prg.Col.Pending"), text("Prg.Col.Hours") }
			};
			foreach (var row in summary.Rows)
				table.Rows.Add(new List<string>
				{
					row.Label, row.Requests.ToString(request.Culture), LmsReportCells.Money(row.Committed, request.Culture), LmsReportCells.Money(row.Pending, request.Culture),
					LmsReportCells.Hours(row.Hours, request.Culture)
				});
			table.Footer.Add(string.Format(request.Culture, text("Prg.Footer.Costs"), LmsReportCells.Money(summary.Committed, request.Culture), LmsReportCells.Money(summary.Pending, request.Culture), summary.Currency));
			return table;
		}
	}

	/// <summary>Certification exceptions: expired, expiring, suspended or revoked, and unverified records of active members (§13a.1, LMS).</summary>
	public class LmsCertificationExceptionReportSource : ILmsReportSource
	{
		private readonly ICertificationService _certifications;
		private readonly IDepartmentsService _departments;

		public LmsCertificationExceptionReportSource(ICertificationService certifications, IDepartmentsService departments)
		{
			_certifications = certifications;
			_departments = departments;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "certification-exceptions", NameKey = "Prg.Report.CertExceptions", DescriptionKey = "Prg.Report.CertExceptionsHelp", Tier = LmsReportTiers.Lms
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text("Prg.Col.Member"), text("Prg.Col.Certification"), text("Prg.Col.Status"), text("Prg.Col.Expires"), text("Prg.Col.Days") }
			};
			var dashboard = await _certifications.GetExpiryDashboardAsync(request.DepartmentId, request.UtcNow.Date) ?? new CertificationExpiryDashboard();
			var active = request.Parameters.IncludeFormerMembers ? null : await _departments.GetActiveMemberUserIdsAsync(request.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var within = Math.Clamp(request.Parameters.WithinDays, 0, 365);
			foreach (var cell in dashboard.PersonCells.Where(c => active == null || active.Contains(c.SubjectId ?? string.Empty))
				         .OrderBy(c => c.SubjectName, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.TypeName, StringComparer.CurrentCultureIgnoreCase))
			{
				string state = null;
				if (cell.Status == (int)PersonnelCertificationStatuses.Expired || (cell.DaysUntilExpiry.HasValue && cell.DaysUntilExpiry < 0))
					state = "Prg.Cert.Expired";
				else if (cell.Status == (int)PersonnelCertificationStatuses.Suspended || cell.Status == (int)PersonnelCertificationStatuses.Revoked)
					state = cell.Status == (int)PersonnelCertificationStatuses.Suspended ? "Prg.Cert.Suspended" : "Prg.Cert.Revoked";
				else if (cell.Status == (int)PersonnelCertificationStatuses.PendingVerification)
					state = "Prg.Cert.Pending";
				else if (cell.DaysUntilExpiry.HasValue && cell.DaysUntilExpiry.Value <= within)
					state = "Prg.Cert.Expiring";
				if (state == null)
					continue;
				table.Rows.Add(new List<string>
				{
					cell.SubjectName, cell.TypeName, text(state), LmsReportCells.Date(cell.ExpiresOn, request.Culture), cell.DaysUntilExpiry?.ToString(request.Culture) ?? string.Empty
				});
			}
			return table;
		}
	}
}
