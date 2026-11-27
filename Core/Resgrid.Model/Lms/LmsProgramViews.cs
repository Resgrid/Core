using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model.Lms
{
	#region Annual training plan

	/// <summary>One category's line on the plan dashboard: planned and actual hours per member by month, the target and the forecast.</summary>
	public class LmsTrainingPlanCategoryRow
	{
		/// <summary>The category; null for planned lines without one.</summary>
		public int? CategoryId { get; set; }
		public string CategoryName { get; set; }

		/// <summary>The annual target per member (a per-year target, or a per-month target times twelve); null without one.</summary>
		public decimal? AnnualTarget { get; set; }

		/// <summary>Planned hours per member, January (index 0) to December.</summary>
		public decimal[] PlannedByMonth { get; set; } = new decimal[12];

		/// <summary>Verified hours per member of the plan's audience, January to December.</summary>
		public decimal[] ActualByMonth { get; set; } = new decimal[12];

		public decimal PlannedYear { get; set; }
		public decimal ActualToDate { get; set; }

		/// <summary>Actual to date, plus what is still planned: the rest of this month's plan and every later month.</summary>
		public decimal Forecast { get; set; }

		/// <summary>Actual to date projected over the whole year at the pace so far; null before the year starts.</summary>
		public decimal? RunRate { get; set; }

		/// <summary>Target minus forecast, when the forecast falls short (0 otherwise).</summary>
		public decimal Shortfall { get; set; }

		/// <summary>The forecast falls short of the target: visible in June, not at the audit (R-C5).</summary>
		public bool AtRisk { get; set; }

		/// <summary>Even if every planned session happens, the plan does not reach the target.</summary>
		public bool PlanShortOfTarget { get; set; }
	}

	/// <summary>The plan dashboard (§13 T3 item 3a): planned against actual, the forecast against the category targets.</summary>
	public class LmsTrainingPlanDashboard
	{
		public LmsTrainingPlan Plan { get; set; }
		public List<LmsTrainingPlanItem> Items { get; set; } = new List<LmsTrainingPlanItem>();
		public List<LmsTrainingPlanCategoryRow> Rows { get; set; } = new List<LmsTrainingPlanCategoryRow>();

		/// <summary>Active members of the plan's audience (the divisor of the per-member actuals).</summary>
		public int MemberCount { get; set; }

		/// <summary>The department-local date the dashboard was computed for.</summary>
		public DateTime AsOfLocal { get; set; }

		/// <summary>Months of the plan year elapsed: 0 before the year, 1–12 inside it (the current month counts), 13 after it.</summary>
		public int AsOfMonth { get; set; }

		public decimal PlannedYearTotal => Rows.Sum(r => r.PlannedYear);
		public decimal ActualToDateTotal => Rows.Sum(r => r.ActualToDate);
		public int AtRiskCount => Rows.Count(r => r.AtRisk);
	}

	/// <summary>The plan-against-actual arithmetic (R-C5), kept pure so it can be tested.</summary>
	public static class LmsTrainingPlanMath
	{
		/// <summary>The month position of <paramref name="asOfLocal"/> in <paramref name="year"/>: 0 before, 1–12 inside, 13 after.</summary>
		public static int AsOfMonth(int year, DateTime asOfLocal)
		{
			if (asOfLocal.Year < year)
				return 0;
			if (asOfLocal.Year > year)
				return 13;
			return asOfLocal.Month;
		}

		/// <summary>The annual per-member target of a category: per-year targets as they are, per-month ones times twelve, others none.</summary>
		public static decimal? AnnualTarget(TrainingCategory category)
		{
			if (category?.TargetHours == null || category.TargetHours.Value <= 0)
				return null;
			switch ((TrainingTargetPeriods)category.TargetPeriod)
			{
				case TrainingTargetPeriods.PerYear:
					return category.TargetHours.Value;
				case TrainingTargetPeriods.PerMonth:
					return category.TargetHours.Value * 12m;
				default:
					return null;
			}
		}

		/// <summary>Fills the totals, forecast, run rate and risk flags of a row from its monthly arrays.</summary>
		public static void Compute(LmsTrainingPlanCategoryRow row, int year, DateTime asOfLocal)
		{
			if (row == null)
				return;
			row.PlannedByMonth ??= new decimal[12];
			row.ActualByMonth ??= new decimal[12];
			var month = AsOfMonth(year, asOfLocal);

			row.PlannedYear = Round(row.PlannedByMonth.Sum());
			if (month == 0)
			{
				row.ActualToDate = 0m;
				row.Forecast = row.PlannedYear;
				row.RunRate = null;
			}
			else if (month == 13)
			{
				row.ActualToDate = Round(row.ActualByMonth.Sum());
				row.Forecast = row.ActualToDate;
				row.RunRate = row.ActualToDate;
			}
			else
			{
				row.ActualToDate = Round(row.ActualByMonth.Take(month).Sum());
				var stillPlanned = row.PlannedByMonth.Skip(month).Sum() + Math.Max(0m, row.PlannedByMonth[month - 1] - row.ActualByMonth[month - 1]);
				row.Forecast = Round(row.ActualToDate + stillPlanned);

				var daysInYear = DateTime.IsLeapYear(year) ? 366m : 365m;
				var elapsed = (decimal)asOfLocal.DayOfYear / daysInYear;
				row.RunRate = elapsed <= 0m ? (decimal?)null : Round(row.ActualToDate / elapsed);
			}

			row.Shortfall = row.AnnualTarget.HasValue && row.Forecast < row.AnnualTarget.Value ? Round(row.AnnualTarget.Value - row.Forecast) : 0m;
			row.AtRisk = row.AnnualTarget.HasValue && row.Forecast < row.AnnualTarget.Value;
			row.PlanShortOfTarget = row.AnnualTarget.HasValue && row.PlannedYear < row.AnnualTarget.Value;
		}

		/// <summary>The planned hours per member of each category by month, from a plan's lines.</summary>
		public static Dictionary<int, decimal[]> PlannedByCategory(IEnumerable<LmsTrainingPlanItem> items)
		{
			var result = new Dictionary<int, decimal[]>();
			foreach (var item in items ?? Enumerable.Empty<LmsTrainingPlanItem>())
			{
				if (item == null || item.Month < 1 || item.Month > 12)
					continue;
				var key = item.CategoryId ?? 0;
				if (!result.TryGetValue(key, out var months))
					result[key] = months = new decimal[12];
				months[item.Month - 1] += item.PlannedHoursTotal;
			}
			return result;
		}

		public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
	}

	#endregion Annual training plan

	#region Competency frameworks

	/// <summary>A framework in Resgrid's catalog: structure and public identifiers only (R-C7), applied to a department on request.</summary>
	public class LmsCompetencyCatalogFramework
	{
		public string Key { get; set; }
		public string Name { get; set; }
		public string Publisher { get; set; }
		public string Edition { get; set; }
		public string Description { get; set; }
		public List<LmsCompetencyCatalogItem> Items { get; set; } = new List<LmsCompetencyCatalogItem>();
	}

	public class LmsCompetencyCatalogItem
	{
		/// <summary>Unique in the framework; stored as the item's Identifier.</summary>
		public string Key { get; set; }
		public string ParentKey { get; set; }
		/// <summary>The public identifier (a position code, a chapter or JPR number); null for a pure grouping.</summary>
		public string Code { get; set; }
		/// <summary>A short label: a position title or a skill name, never a standard's requirement text.</summary>
		public string Label { get; set; }
		public string ItemType { get; set; }
	}

	/// <summary>What a CASE 1.1 import did.</summary>
	public class LmsCaseImportResult
	{
		public LmsCompetencyFramework Framework { get; set; }
		public int ItemsAdded { get; set; }
		public int ItemsUpdated { get; set; }
		public int ItemsRemoved { get; set; }
		/// <summary>True when the package updated a framework imported earlier (same CFDocument identifier).</summary>
		public bool Updated { get; set; }
		/// <summary>A resource key when the file could not be read; nothing was written.</summary>
		public string Error { get; set; }
	}

	/// <summary>One item of a member's gap report: what evidences it, how old the newest evidence is, or that none does.</summary>
	public class LmsCompetencyGapRow
	{
		public int LmsCompetencyItemId { get; set; }
		public int? ParentItemId { get; set; }
		public int Depth { get; set; }
		public string Code { get; set; }
		public string Label { get; set; }
		public string ItemType { get; set; }
		public LmsCompetencyEvidenceStates State { get; set; }
		public DateTime? EvidenceOn { get; set; }
		public int? AgeDays { get; set; }
		/// <summary>What the newest evidence was (a course, a training, a record's title, a certification).</summary>
		public string EvidenceTitle { get; set; }
		/// <summary>How many tagged subjects the item has.</summary>
		public int Tagged { get; set; }
	}

	public class LmsCompetencyGapReport
	{
		public LmsCompetencyFramework Framework { get; set; }
		public string UserId { get; set; }
		public string MemberName { get; set; }
		public List<LmsCompetencyGapRow> Rows { get; set; } = new List<LmsCompetencyGapRow>();
		public DateTime GeneratedOn { get; set; }
		public int Current => Rows.Count(r => r.State == LmsCompetencyEvidenceStates.Current);
		public int Aged => Rows.Count(r => r.State == LmsCompetencyEvidenceStates.Aged);
		public int Missing => Rows.Count(r => r.State == LmsCompetencyEvidenceStates.None);
	}

	/// <summary>A piece of evidence for one subject (the gap report's input).</summary>
	public class LmsCompetencyEvidence
	{
		public LmsCompetencySubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public DateTime On { get; set; }
		public string Title { get; set; }
	}

	/// <summary>The gap-report arithmetic, pure so it can be tested.</summary>
	public static class LmsCompetencyGapMath
	{
		public static List<LmsCompetencyGapRow> Build(LmsCompetencyFramework framework, IEnumerable<LmsCompetencyItem> items, IEnumerable<LmsCompetencyAlignment> alignments,
			IEnumerable<LmsCompetencyEvidence> evidence, DateTime utcNow)
		{
			var live = (items ?? Enumerable.Empty<LmsCompetencyItem>()).Where(i => i != null && !i.IsDeleted).ToList();
			var byItem = (alignments ?? Enumerable.Empty<LmsCompetencyAlignment>()).Where(a => a != null)
				.GroupBy(a => a.LmsCompetencyItemId).ToDictionary(g => g.Key, g => g.ToList());
			var found = (evidence ?? Enumerable.Empty<LmsCompetencyEvidence>()).Where(e => e != null && e.SubjectId != null)
				.GroupBy(e => (e.Kind, e.SubjectId.Trim().ToUpperInvariant()))
				.ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.On).First());
			var maxAge = framework != null && framework.EvidenceMaxAgeMonths > 0 ? framework.EvidenceMaxAgeMonths : 0;

			var rows = new List<LmsCompetencyGapRow>();
			foreach (var item in Ordered(live))
			{
				var row = new LmsCompetencyGapRow
				{
					LmsCompetencyItemId = item.Item.LmsCompetencyItemId,
					ParentItemId = item.Item.ParentItemId,
					Depth = item.Depth,
					Code = item.Item.HumanCodingScheme,
					Label = item.Item.Label,
					ItemType = item.Item.ItemType,
					State = LmsCompetencyEvidenceStates.NotTagged
				};

				if (byItem.TryGetValue(item.Item.LmsCompetencyItemId, out var tags) && tags.Count > 0)
				{
					row.Tagged = tags.Count;
					LmsCompetencyEvidence newest = null;
					foreach (var tag in tags)
						if (found.TryGetValue(((LmsCompetencySubjectKinds)tag.SubjectKind, (tag.SubjectId ?? string.Empty).Trim().ToUpperInvariant()), out var hit) &&
						    (newest == null || hit.On > newest.On))
							newest = hit;

					if (newest == null)
						row.State = LmsCompetencyEvidenceStates.None;
					else
					{
						row.EvidenceOn = newest.On;
						row.EvidenceTitle = newest.Title;
						row.AgeDays = Math.Max(0, (int)(utcNow.Date - newest.On.Date).TotalDays);
						row.State = maxAge > 0 && newest.On < utcNow.AddMonths(-maxAge) ? LmsCompetencyEvidenceStates.Aged : LmsCompetencyEvidenceStates.Current;
					}
				}

				rows.Add(row);
			}

			return rows;
		}

		/// <summary>Items in tree order (parents before children, siblings by sort order then code), with their depth.</summary>
		public static List<(LmsCompetencyItem Item, int Depth)> Ordered(IReadOnlyCollection<LmsCompetencyItem> items)
		{
			var list = items?.Where(i => i != null).ToList() ?? new List<LmsCompetencyItem>();
			var ids = new HashSet<int>(list.Select(i => i.LmsCompetencyItemId));
			var children = list.GroupBy(i => i.ParentItemId.HasValue && ids.Contains(i.ParentItemId.Value) ? i.ParentItemId.Value : 0)
				.ToDictionary(g => g.Key, g => g.OrderBy(i => i.SortOrder).ThenBy(i => i.HumanCodingScheme, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.LmsCompetencyItemId).ToList());
			var result = new List<(LmsCompetencyItem, int)>();
			var seen = new HashSet<int>();

			void Walk(int parent, int depth)
			{
				if (!children.TryGetValue(parent, out var kids) || depth > 20)
					return;
				foreach (var kid in kids)
				{
					if (!seen.Add(kid.LmsCompetencyItemId))
						continue;
					result.Add((kid, depth));
					Walk(kid.LmsCompetencyItemId, depth + 1);
				}
			}

			Walk(0, 0);
			// A cycle in imported parents leaves items unreached: list them at the top level rather than lose them.
			foreach (var item in list.Where(i => !seen.Contains(i.LmsCompetencyItemId)))
				result.Add((item, 0));
			return result;
		}
	}

	#endregion Competency frameworks

	#region Training requests

	/// <summary>A training request with its decisions and who may act on it now.</summary>
	public class LmsTrainingRequestView
	{
		public LmsTrainingRequest Request { get; set; }
		public List<LmsTrainingRequestDecision> Decisions { get; set; } = new List<LmsTrainingRequestDecision>();
		public bool CanDecide { get; set; }
		public bool CanWithdraw { get; set; }
		public bool CanComplete { get; set; }
		public bool CanCancel { get; set; }
	}

	/// <summary>Cost totals of training requests (approved, completed and still waiting), grouped by member, course or year.</summary>
	public class LmsTrainingRequestCostRow
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Requests { get; set; }
		/// <summary>Approved and completed requests: the committed spend (actual cost where reported).</summary>
		public decimal Committed { get; set; }
		/// <summary>Requests still waiting for a decision.</summary>
		public decimal Pending { get; set; }
		public decimal Hours { get; set; }
	}

	public enum LmsTrainingRequestCostGroupings
	{
		Member = 0,
		Course = 1,
		Year = 2
	}

	public class LmsTrainingRequestCostSummary
	{
		public LmsTrainingRequestCostGroupings GroupBy { get; set; }
		public string Currency { get; set; }
		public List<LmsTrainingRequestCostRow> Rows { get; set; } = new List<LmsTrainingRequestCostRow>();
		public decimal Committed => Rows.Sum(r => r.Committed);
		public decimal Pending => Rows.Sum(r => r.Pending);
	}

	/// <summary>The cost arithmetic, pure so it can be tested.</summary>
	public static class LmsTrainingRequestCostMath
	{
		public static bool IsCommitted(LmsTrainingRequest request) =>
			request != null && (request.Status == (int)LmsTrainingRequestStatuses.Approved || request.Status == (int)LmsTrainingRequestStatuses.Completed);

		public static List<LmsTrainingRequestCostRow> Group(IEnumerable<LmsTrainingRequest> requests, LmsTrainingRequestCostGroupings groupBy, Func<string, string> memberName)
		{
			var counted = (requests ?? Enumerable.Empty<LmsTrainingRequest>())
				.Where(r => r != null && (IsCommitted(r) || r.Status == (int)LmsTrainingRequestStatuses.Submitted)).ToList();

			string KeyOf(LmsTrainingRequest r)
			{
				switch (groupBy)
				{
					case LmsTrainingRequestCostGroupings.Member:
						return (r.UserId ?? string.Empty).ToUpperInvariant();
					case LmsTrainingRequestCostGroupings.Course:
						return string.IsNullOrWhiteSpace(r.CourseCode)
							? ((r.Title ?? string.Empty).Trim() + "|" + (r.Provider ?? string.Empty).Trim()).ToUpperInvariant()
							: r.CourseCode.Trim().ToUpperInvariant();
					default:
						return (r.StartsOn ?? r.SubmittedOn).Year.ToString(CultureInfo.InvariantCulture);
				}
			}

			string LabelOf(LmsTrainingRequest r)
			{
				switch (groupBy)
				{
					case LmsTrainingRequestCostGroupings.Member:
						return memberName?.Invoke(r.UserId) ?? r.UserId;
					case LmsTrainingRequestCostGroupings.Course:
						var title = string.IsNullOrWhiteSpace(r.CourseCode) ? r.Title : r.CourseCode + " " + r.Title;
						return string.IsNullOrWhiteSpace(r.Provider) ? title : title + " (" + r.Provider + ")";
					default:
						return (r.StartsOn ?? r.SubmittedOn).Year.ToString(CultureInfo.InvariantCulture);
				}
			}

			return counted.GroupBy(KeyOf).Select(g => new LmsTrainingRequestCostRow
			{
				Key = g.Key,
				Label = LabelOf(g.First()),
				Requests = g.Count(),
				Committed = g.Where(IsCommitted).Sum(r => r.EffectiveCost),
				Pending = g.Where(r => r.Status == (int)LmsTrainingRequestStatuses.Submitted).Sum(r => r.RequestedCost),
				Hours = g.Where(IsCommitted).Sum(r => r.Hours)
			}).OrderBy(r => groupBy == LmsTrainingRequestCostGroupings.Year ? r.Key : r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
		}
	}

	#endregion Training requests

	#region Reports

	/// <summary>The parameters of a catalog report (stored as JSON on a saved report).</summary>
	public class LmsReportParameters
	{
		public LmsReportPeriods Period { get; set; } = LmsReportPeriods.YearToDate;
		/// <summary>Department-local dates of a custom period (To inclusive).</summary>
		public DateTime? From { get; set; }
		public DateTime? To { get; set; }
		public int? CategoryId { get; set; }
		/// <summary>Hours report grouping (<see cref="TrainingHoursGroupings"/>), cost grouping (<see cref="LmsTrainingRequestCostGroupings"/>).</summary>
		public int GroupBy { get; set; }
		/// <summary>The member of a per-member report (NCCP export, gap report).</summary>
		public string UserId { get; set; }
		public int? PlanId { get; set; }
		public int? FrameworkId { get; set; }
		public bool IncludeFormerMembers { get; set; }
		/// <summary>The "expiring within" window of the certification exception report.</summary>
		public int WithinDays { get; set; } = 60;
	}

	/// <summary>What the catalog says about a report.</summary>
	public class LmsReportDescriptor
	{
		public string Key { get; set; }
		/// <summary>Trainings resource keys.</summary>
		public string NameKey { get; set; }
		public string DescriptionKey { get; set; }
		public LmsReportTiers Tier { get; set; }
		public bool NeedsMember { get; set; }
		public bool NeedsPlan { get; set; }
		public bool NeedsFramework { get; set; }
		public bool UsesPeriod { get; set; }
		public bool UsesCategory { get; set; }
		/// <summary>The grouping choices (resource keys by value); empty when the report has none.</summary>
		public Dictionary<int, string> Groupings { get; set; } = new Dictionary<int, string>();
	}

	/// <summary>A request to build one report.</summary>
	public class LmsReportRequest
	{
		public int DepartmentId { get; set; }
		/// <summary>Who asked (the saved report's owner on a scheduled run).</summary>
		public string UserId { get; set; }
		public LmsReportParameters Parameters { get; set; } = new LmsReportParameters();
		/// <summary>The resolved period as UTC (From inclusive, To exclusive).</summary>
		public DateTime FromUtc { get; set; }
		public DateTime ToUtc { get; set; }
		public DateTime UtcNow { get; set; }
		/// <summary>The culture the column headings and values are written in.</summary>
		public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
		public Department Department { get; set; }
	}

	/// <summary>A built report: headings and rows of text, the same for HTML, CSV and PDF.</summary>
	public class LmsReportTable
	{
		public string Title { get; set; }
		public string Subtitle { get; set; }
		public List<string> Columns { get; set; } = new List<string>();
		public List<List<string>> Rows { get; set; } = new List<List<string>>();
		/// <summary>Lines printed under the table (totals, notes).</summary>
		public List<string> Footer { get; set; } = new List<string>();
		/// <summary>A resource key when the report cannot be built for these parameters (no member, plan or framework).</summary>
		public string Problem { get; set; }
	}

	/// <summary>Report periods and schedules, pure so they can be tested.</summary>
	public static class LmsReportSchedule
	{
		/// <summary>The department-local period [from, to) a report covers on <paramref name="localToday"/>.</summary>
		public static (DateTime From, DateTime To) ResolvePeriod(LmsReportParameters parameters, DateTime localToday)
		{
			var today = localToday.Date;
			var p = parameters ?? new LmsReportParameters();
			switch (p.Period)
			{
				case LmsReportPeriods.MonthToDate:
					return (new DateTime(today.Year, today.Month, 1), today.AddDays(1));
				case LmsReportPeriods.LastMonth:
					var firstOfMonth = new DateTime(today.Year, today.Month, 1);
					return (firstOfMonth.AddMonths(-1), firstOfMonth);
				case LmsReportPeriods.QuarterToDate:
					var quarterMonth = (today.Month - 1) / 3 * 3 + 1;
					return (new DateTime(today.Year, quarterMonth, 1), today.AddDays(1));
				case LmsReportPeriods.Last12Months:
					return (today.AddMonths(-12).AddDays(1), today.AddDays(1));
				case LmsReportPeriods.LastYear:
					return (new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year, 1, 1));
				case LmsReportPeriods.Custom:
					var from = (p.From ?? new DateTime(today.Year, 1, 1)).Date;
					var to = (p.To ?? today).Date;
					if (to < from)
						(from, to) = (to, from);
					return (from, to.AddDays(1));
				default:
					return (new DateTime(today.Year, 1, 1), today.AddDays(1));
			}
		}

		/// <summary>
		/// The next time (UTC) a scheduled report is due strictly after <paramref name="afterUtc"/>, at its department-local hour;
		/// null when it is not scheduled. <paramref name="toLocal"/> and <paramref name="toUtc"/> convert with the department's zone.
		/// </summary>
		public static DateTime? NextRunUtc(LmsReportDefinition definition, DateTime afterUtc, Func<DateTime, DateTime> toLocal, Func<DateTime, DateTime> toUtc)
		{
			if (definition == null || definition.IsDeleted || definition.Frequency == (int)LmsReportFrequencies.None ||
			    !Enum.IsDefined(typeof(LmsReportFrequencies), definition.Frequency))
				return null;

			toLocal ??= d => d;
			toUtc ??= d => d;
			var hour = Math.Clamp(definition.HourLocal, 0, 23);
			var afterLocal = toLocal(afterUtc);
			var day = afterLocal.Date;

			for (var i = 0; i < 400; i++, day = day.AddDays(1))
			{
				bool due;
				switch ((LmsReportFrequencies)definition.Frequency)
				{
					case LmsReportFrequencies.Daily:
						due = true;
						break;
					case LmsReportFrequencies.Weekly:
						due = (int)day.DayOfWeek == Math.Clamp(definition.DayOfWeek, 0, 6);
						break;
					default:
						var target = Math.Min(Math.Clamp(definition.DayOfMonth, 1, 31), DateTime.DaysInMonth(day.Year, day.Month));
						due = day.Day == target;
						break;
				}

				if (!due)
					continue;
				var candidateLocal = day.AddHours(hour);
				if (candidateLocal <= afterLocal)
					continue;
				var candidateUtc = toUtc(candidateLocal);
				if (candidateUtc > afterUtc)
					return DateTime.SpecifyKind(candidateUtc, DateTimeKind.Utc);
			}

			return null;
		}

		/// <summary>RFC 4180 CSV of a table (UTF-8 text; the caller adds the byte order mark).</summary>
		public static string ToCsv(LmsReportTable table)
		{
			var builder = new System.Text.StringBuilder();
			void Line(IEnumerable<string> cells) => builder.Append(string.Join(",", cells.Select(Cell))).Append("\r\n");
			if (table == null)
				return string.Empty;
			Line(table.Columns);
			foreach (var row in table.Rows)
				Line(row);
			return builder.ToString();
		}

		private static string Cell(string value)
		{
			var text = value ?? string.Empty;
			// Formula injection: a cell that a spreadsheet would evaluate is written as text.
			if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0 && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
				text = "'" + text;
			return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
		}
	}

	#endregion Reports

	#region Certification cycles

	/// <summary>Counts from one run of the certification cycle sweep.</summary>
	public class LmsCertificationCycleRunResult
	{
		public int Departments { get; set; }
		public int Renewed { get; set; }
		public int Alerts { get; set; }
		public int Skipped { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>The cycle rules, pure so they can be tested.</summary>
	public static class LmsCertificationCycleMath
	{
		/// <summary>A record that is in force or about to be: active, waiting for verification, or a trainee.</summary>
		public static bool IsLive(PersonnelCertification record) =>
			record != null && !record.IsDeleted && (record.Status == (int)PersonnelCertificationStatuses.Active ||
			record.Status == (int)PersonnelCertificationStatuses.PendingVerification || record.Status == (int)PersonnelCertificationStatuses.Trainee);

		/// <summary>The cycle's renewal hours are met (types without renewal hours never complete this way).</summary>
		public static bool HoursMet(DepartmentCertificationType type, decimal creditedHours) =>
			type?.RenewalCreditHoursRequired != null && type.RenewalCreditHoursRequired.Value > 0 && creditedHours >= type.RenewalCreditHoursRequired.Value;

		/// <summary>The member holds a later cycle of the same type (it starts after this one and ends later, or never).</summary>
		public static bool HasLaterCycle(PersonnelCertification record, IEnumerable<PersonnelCertification> sameMemberRecords) =>
			CertificationCycleRules.IsSupersededByLaterCycle(record, sameMemberRecords);

		/// <summary>Whether auto-renew should create the next cycle of <paramref name="record"/> today.</summary>
		public static bool ShouldAutoRenew(LmsCertificationCycleRule rule, DepartmentCertificationType type, PersonnelCertification record, decimal creditedHours,
			IEnumerable<PersonnelCertification> sameMemberRecords, DateTime localToday)
		{
			if (rule == null || type == null || record == null || record.IsDeleted || !record.ExpiresOn.HasValue || type.NeverExpires)
				return false;
			if (!CanAutoRenew(type) || rule.AutoRenewMode == (int)LmsAutoRenewModes.Off)
				return false;
			if (record.Status == (int)PersonnelCertificationStatuses.Revoked || record.Status == (int)PersonnelCertificationStatuses.Suspended)
				return false;
			if (!HoursMet(type, creditedHours) || HasLaterCycle(record, sameMemberRecords))
				return false;

			switch ((LmsAutoRenewModes)rule.AutoRenewMode)
			{
				case LmsAutoRenewModes.OnCompletion:
					// A cycle that ended more than a year ago is history, not something to renew now.
					return record.ExpiresOn.Value.Date >= localToday.Date.AddYears(-1);
				case LmsAutoRenewModes.OnExpiry:
					return record.ExpiresOn.Value.Date <= localToday.Date && record.ExpiresOn.Value.Date >= localToday.Date.AddYears(-1);
				default:
					return false;
			}
		}

		/// <summary>
		/// Auto-renew needs renewal hours to complete a cycle and a validity to size the next one: rolling months, or (round 9A) a calendar or
		/// fiscal year end. A fixed-date type has no next cycle.
		/// </summary>
		public static bool CanAutoRenew(DepartmentCertificationType type)
		{
			if (type == null || type.IsDeleted || type.NeverExpires || !type.RenewalCreditHoursRequired.HasValue || type.RenewalCreditHoursRequired.Value <= 0)
				return false;
			switch (Certifications.CertificationValidity.RuleOf(type))
			{
				case Certifications.CertificationValidityRules.CalendarYearEnd:
				case Certifications.CertificationValidityRules.FiscalYearEnd:
					return true;
				case Certifications.CertificationValidityRules.FixedDate:
					return false;
				default:
					return type.DefaultValidityMonths.HasValue && type.DefaultValidityMonths.Value > 0;
			}
		}

		/// <summary>
		/// The next cycle's dates: it starts the day after this one ends and lasts the type's validity (rolling months), or (round 9A) runs to
		/// the following year end.
		/// </summary>
		public static (DateTime Start, DateTime End) NextCycle(PersonnelCertification record, DepartmentCertificationType type)
		{
			var start = record.ExpiresOn.Value.Date.AddDays(1);
			var rule = Certifications.CertificationValidity.RuleOf(type);
			if (rule == Certifications.CertificationValidityRules.CalendarYearEnd || rule == Certifications.CertificationValidityRules.FiscalYearEnd)
			{
				var (month, day) = Certifications.CertificationValidity.YearEnd(type);
				return (start, Certifications.CertificationValidity.YearEndOnOrAfter(start, month, day));
			}
			var end = start.AddMonths(type.DefaultValidityMonths.Value).AddDays(-1);
			return (start, end);
		}
	}

	#endregion Certification cycles

	#region Familiarization credit

	/// <summary>A Records visit that earns building-familiarization credit (an inspection or a pre-plan review).</summary>
	public class LmsFamiliarizationVisit
	{
		/// <summary>The ledger source key (idempotent per visit).</summary>
		public string SourceRefId { get; set; }
		public string UserId { get; set; }
		public DateTime CompletedOn { get; set; }
		public DateTime? StartedOn { get; set; }
		public string Reference { get; set; }
		/// <summary>The visit was cancelled or deleted: its record is voided.</summary>
		public bool Withdrawn { get; set; }
		/// <summary>Inspection, OccupancyReview or PreplanReview.</summary>
		public string Kind { get; set; }
		public DateTime ModifiedOn { get; set; }
	}

	/// <summary>Counts from one familiarization run.</summary>
	public class LmsFamiliarizationRunResult
	{
		public int Departments { get; set; }
		public int Written { get; set; }
		public int Voided { get; set; }
		public int Unchanged { get; set; }
		public int Failures { get; set; }
	}

	#endregion Familiarization credit
}
