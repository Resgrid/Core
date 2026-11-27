using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT3
{
	/// <summary>
	/// Training T3 part 2 (round 4C): the pure rules. Plan against actual and the forecast (R-C5), the competency catalog carrying no
	/// standards text, CASE 1.1 parsing, the gap arithmetic (R-C7), request cost totals, report periods, schedules and CSV, certification
	/// cycles, credential-status audiences and the program settings' protobuf round trip.
	/// </summary>
	[TestFixture]
	public class LmsProgramRulesTests
	{
		#region Annual plan (R-C5)

		private static LmsTrainingPlanCategoryRow Row(decimal planned, decimal[] actual, decimal? target)
		{
			var row = new LmsTrainingPlanCategoryRow { AnnualTarget = target, PlannedByMonth = Enumerable.Repeat(planned, 12).ToArray(), ActualByMonth = new decimal[12] };
			for (var i = 0; i < actual.Length; i++)
				row.ActualByMonth[i] = actual[i];
			return row;
		}

		[Test]
		public void A_mid_year_shortfall_against_the_ISO_company_target_is_visible_in_June()
		{
			// 16 h per member per month planned (192 h a year); January to May delivered 12 h a month, June so far 4 h.
			var row = Row(16m, new[] { 12m, 12m, 12m, 12m, 12m, 4m }, 192m);

			LmsTrainingPlanMath.Compute(row, 2027, new DateTime(2027, 6, 15));

			row.PlannedYear.Should().Be(192m);
			row.ActualToDate.Should().Be(64m);
			row.Forecast.Should().Be(64m + 6 * 16m + 12m, "the rest of June's plan and every later month are still to come");
			row.Shortfall.Should().Be(20m);
			row.AtRisk.Should().BeTrue();
			row.PlanShortOfTarget.Should().BeFalse("the plan itself reaches the target");
			row.RunRate.Should().BeApproximately(64m / (166m / 365m), 0.5m);
		}

		[Test]
		public void A_plan_on_track_is_not_at_risk_and_a_month_already_over_delivered_adds_nothing_more()
		{
			var row = Row(10m, new[] { 10m, 10m, 25m }, 120m);
			LmsTrainingPlanMath.Compute(row, 2027, new DateTime(2027, 3, 31));

			row.ActualToDate.Should().Be(45m);
			row.Forecast.Should().Be(45m + 9 * 10m, "March delivered more than planned, so only April to December remain");
			row.AtRisk.Should().BeFalse();
			row.Shortfall.Should().Be(0m);
		}

		[Test]
		public void Before_the_year_the_forecast_is_the_plan_and_after_it_the_actual()
		{
			var before = Row(10m, new decimal[0], 150m);
			LmsTrainingPlanMath.Compute(before, 2028, new DateTime(2027, 12, 1));
			before.Forecast.Should().Be(120m);
			before.ActualToDate.Should().Be(0m);
			before.RunRate.Should().BeNull();
			before.PlanShortOfTarget.Should().BeTrue();

			var after = Row(10m, Enumerable.Repeat(11m, 12).ToArray(), 120m);
			LmsTrainingPlanMath.Compute(after, 2026, new DateTime(2027, 2, 1));
			after.Forecast.Should().Be(132m);
			after.AtRisk.Should().BeFalse();
		}

		[Test]
		public void Annual_targets_come_from_per_year_and_per_month_categories_only()
		{
			LmsTrainingPlanMath.AnnualTarget(new TrainingCategory { TargetHours = 192m, TargetPeriod = (int)TrainingTargetPeriods.PerYear }).Should().Be(192m);
			LmsTrainingPlanMath.AnnualTarget(new TrainingCategory { TargetHours = 16m, TargetPeriod = (int)TrainingTargetPeriods.PerMonth }).Should().Be(192m);
			LmsTrainingPlanMath.AnnualTarget(new TrainingCategory { TargetHours = 60m, TargetPeriod = (int)TrainingTargetPeriods.Once }).Should().BeNull();
			LmsTrainingPlanMath.AnnualTarget(new TrainingCategory { TargetHours = null, TargetPeriod = (int)TrainingTargetPeriods.PerYear }).Should().BeNull();
		}

		[Test]
		public void Planned_hours_add_up_sessions_per_category_and_month()
		{
			var planned = LmsTrainingPlanMath.PlannedByCategory(new[]
			{
				new LmsTrainingPlanItem { Month = 3, CategoryId = 7, PlannedHours = 3m, PlannedSessions = 2 },
				new LmsTrainingPlanItem { Month = 3, CategoryId = 7, PlannedHours = 1.5m, PlannedSessions = 1 },
				new LmsTrainingPlanItem { Month = 4, CategoryId = null, PlannedHours = 2m, PlannedSessions = 1 },
				new LmsTrainingPlanItem { Month = 13, CategoryId = 7, PlannedHours = 99m, PlannedSessions = 1 }
			});

			planned[7][2].Should().Be(7.5m);
			planned[7].Sum().Should().Be(7.5m, "an invalid month is ignored");
			planned[0][3].Should().Be(2m, "uncategorized lines are kept under 0");
		}

		#endregion Annual plan (R-C5)

		#region Competency frameworks (R-C7)

		// Requirement prose of a standard ("given ..., so that ...", "shall"), sentences and long text never belong in Resgrid's seeds.
		private static readonly Regex StandardProse = new Regex(@"\b(given|so that|shall|must|the candidate|the member|in accordance|performance outcome|requisite)\b",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		[Test]
		public void The_catalog_carries_structure_and_identifiers_never_a_standards_text()
		{
			LmsCompetencyFrameworkCatalog.All.Should().NotBeEmpty();
			foreach (var framework in LmsCompetencyFrameworkCatalog.All)
			{
				framework.Items.Should().NotBeEmpty(framework.Key);
				framework.Items.Select(i => i.Key).Should().OnlyHaveUniqueItems(framework.Key);
				foreach (var item in framework.Items)
				{
					item.Label.Length.Should().BeLessThanOrEqualTo(LmsCompetencyFrameworkCatalog.MaxSeedLabelLength, item.Key);
					item.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeLessThanOrEqualTo(7, "a name, not a sentence: " + item.Label);
					StandardProse.IsMatch(item.Label).Should().BeFalse(item.Label);
					item.Label.TrimEnd().Should().NotEndWith(".", item.Label);
					if (item.Code != null)
						item.Code.Length.Should().BeLessThanOrEqualTo(20, "codes are identifiers");
				}
			}

			// The NFPA frameworks ship their qualification levels only: chapter and JPR numbers come from the department's own edition.
			LmsCompetencyFrameworkCatalog.Find(LmsCompetencyFrameworkCatalog.NfpaFirefighterKey).Items.Should().OnlyContain(i => i.ItemType == "Level" && i.Code == null);
			LmsCompetencyFrameworkCatalog.Find(LmsCompetencyFrameworkCatalog.NwcgPositionsKey).Items.Select(i => i.Code).Should().Contain(new[] { "FFT2", "ICT4", "ENGB" });
		}

		private const string CaseSample = @"{
  ""CFDocument"": { ""identifier"": ""c1f0a1d2-0000-4000-8000-000000000001"", ""uri"": ""https://case.example.org/doc/1"", ""title"": ""State Firefighter Competencies"",
    ""creator"": ""State Fire Academy"", ""version"": ""2026"", ""language"": ""en"" },
  ""CFItems"": [
    { ""identifier"": ""i-1"", ""humanCodingScheme"": ""FF1"", ""abbreviatedStatement"": ""Firefighter I"", ""fullStatement"": ""The Firefighter I level."", ""CFItemType"": ""Level"" },
    { ""identifier"": ""i-2"", ""humanCodingScheme"": ""FF1.1"", ""fullStatement"": ""Don and doff protective equipment"", ""CFItemType"": ""Competency"", ""listEnumeration"": ""1"" },
    { ""identifier"": ""i-3"", ""humanCodingScheme"": ""FF1.2"", ""fullStatement"": ""Raise a ground ladder"", ""CFItemTypeURI"": { ""title"": ""Competency"" }, ""listEnumeration"": ""2"" },
    { ""identifier"": ""i-3"", ""humanCodingScheme"": ""DUPLICATE"" }
  ],
  ""CFAssociations"": [
    { ""associationType"": ""isChildOf"", ""originNodeURI"": { ""identifier"": ""i-1"" }, ""destinationNodeURI"": { ""identifier"": ""c1f0a1d2-0000-4000-8000-000000000001"" }, ""sequenceNumber"": 1 },
    { ""associationType"": ""isChildOf"", ""originNodeURI"": { ""identifier"": ""i-2"" }, ""destinationNodeURI"": { ""identifier"": ""i-1"" }, ""sequenceNumber"": 2 },
    { ""associationType"": ""isChildOf"", ""originNodeURI"": { ""identifier"": ""i-3"" }, ""destinationNodeURI"": { ""identifier"": ""i-1"" }, ""sequenceNumber"": 1 },
    { ""associationType"": ""exactMatchOf"", ""originNodeURI"": { ""identifier"": ""i-3"" }, ""destinationNodeURI"": { ""identifier"": ""i-2"" } }
  ]
}";

		public static byte[] CaseBytes(string json = null) => Encoding.UTF8.GetBytes(json ?? CaseSample);

		[Test]
		public void A_CASE_package_reads_its_document_items_and_parents()
		{
			var package = LmsCaseParser.Parse(CaseBytes(), out var error);

			error.Should().BeNull();
			package.DocumentId.Should().Be("c1f0a1d2-0000-4000-8000-000000000001");
			package.Title.Should().Be("State Firefighter Competencies");
			package.Publisher.Should().Be("State Fire Academy", "the creator stands in for a missing publisher");
			package.Items.Should().HaveCount(3, "a repeated identifier is read once");
			package.Items.Single(i => i.Identifier == "i-1").ParentIdentifier.Should().BeNull("an item under the document is top level");
			var ladder = package.Items.Single(i => i.Identifier == "i-3");
			ladder.ParentIdentifier.Should().Be("i-1");
			ladder.Code.Should().Be("FF1.2");
			ladder.ItemType.Should().Be("Competency");
			ladder.SortOrder.Should().Be(1, "the association's sequence number orders siblings");
			package.Items.Single(i => i.Identifier == "i-1").Label.Should().Be("Firefighter I", "the abbreviated statement is preferred");
		}

		[Test]
		public void A_wrapped_package_reads_and_bad_files_are_refused_with_a_reason()
		{
			LmsCaseParser.Parse(CaseBytes("{\"CFPackage\":" + CaseSample + "}"), out _).Items.Should().HaveCount(3);
			LmsCaseParser.Parse(Array.Empty<byte>(), out var empty).Should().BeNull();
			empty.Should().Be("lms_program_case_empty");
			LmsCaseParser.Parse(CaseBytes("not json"), out var unreadable).Should().BeNull();
			unreadable.Should().Be("lms_program_case_unreadable");
			LmsCaseParser.Parse(CaseBytes("{\"CFItems\":[]}"), out var noDocument).Should().BeNull();
			noDocument.Should().Be("lms_program_case_no_document");
			var many = "{\"CFDocument\":{\"identifier\":\"d\",\"title\":\"t\"},\"CFItems\":[" + string.Join(",", Enumerable.Range(0, LmsCaseParser.MaxItems + 1).Select(i => "{\"identifier\":\"x" + i + "\"}")) + "]}";
			LmsCaseParser.Parse(CaseBytes(many), out var tooMany).Should().BeNull();
			tooMany.Should().Be("lms_program_case_too_many_items");
		}

		[Test]
		public void The_gap_list_shows_current_aged_missing_and_untagged_items_in_tree_order()
		{
			var framework = new LmsCompetencyFramework { LmsCompetencyFrameworkId = 1, EvidenceMaxAgeMonths = 24 };
			var items = new List<LmsCompetencyItem>
			{
				new LmsCompetencyItem { LmsCompetencyItemId = 10, Label = "Level", SortOrder = 1 },
				new LmsCompetencyItem { LmsCompetencyItemId = 11, ParentItemId = 10, HumanCodingScheme = "A", Label = "Current", SortOrder = 1 },
				new LmsCompetencyItem { LmsCompetencyItemId = 12, ParentItemId = 10, HumanCodingScheme = "B", Label = "Aged", SortOrder = 2 },
				new LmsCompetencyItem { LmsCompetencyItemId = 13, ParentItemId = 10, HumanCodingScheme = "C", Label = "None", SortOrder = 3 },
				new LmsCompetencyItem { LmsCompetencyItemId = 14, ParentItemId = 10, HumanCodingScheme = "D", Label = "Deleted", IsDeleted = true }
			};
			var alignments = new List<LmsCompetencyAlignment>
			{
				new LmsCompetencyAlignment { LmsCompetencyItemId = 11, SubjectKind = (int)LmsCompetencySubjectKinds.Course, SubjectId = "5" },
				new LmsCompetencyAlignment { LmsCompetencyItemId = 11, SubjectKind = (int)LmsCompetencySubjectKinds.Category, SubjectId = "9" },
				new LmsCompetencyAlignment { LmsCompetencyItemId = 12, SubjectKind = (int)LmsCompetencySubjectKinds.Training, SubjectId = "3" },
				new LmsCompetencyAlignment { LmsCompetencyItemId = 13, SubjectKind = (int)LmsCompetencySubjectKinds.CertificationType, SubjectId = "4" }
			};
			var now = new DateTime(2027, 6, 1);
			var evidence = new List<LmsCompetencyEvidence>
			{
				new LmsCompetencyEvidence { Kind = LmsCompetencySubjectKinds.Course, SubjectId = "5", On = now.AddMonths(-30), Title = "Old course" },
				new LmsCompetencyEvidence { Kind = LmsCompetencySubjectKinds.Category, SubjectId = "9", On = now.AddDays(-10), Title = "Drill night" },
				new LmsCompetencyEvidence { Kind = LmsCompetencySubjectKinds.Training, SubjectId = "3", On = now.AddMonths(-30), Title = "Ladders" },
				new LmsCompetencyEvidence { Kind = LmsCompetencySubjectKinds.Category, SubjectId = "99", On = now, Title = "Unrelated" }
			};

			var rows = LmsCompetencyGapMath.Build(framework, items, alignments, evidence, now);

			rows.Select(r => r.LmsCompetencyItemId).Should().Equal(10, 11, 12, 13);
			rows[0].State.Should().Be(LmsCompetencyEvidenceStates.NotTagged);
			rows[1].State.Should().Be(LmsCompetencyEvidenceStates.Current, "the newest of its evidence counts");
			rows[1].EvidenceTitle.Should().Be("Drill night");
			rows[1].AgeDays.Should().Be(10);
			rows[1].Depth.Should().Be(1);
			rows[2].State.Should().Be(LmsCompetencyEvidenceStates.Aged);
			rows[3].State.Should().Be(LmsCompetencyEvidenceStates.None);

			framework.EvidenceMaxAgeMonths = 0;
			LmsCompetencyGapMath.Build(framework, items, alignments, evidence, now)[2].State.Should().Be(LmsCompetencyEvidenceStates.Current, "0 means evidence never ages");
		}

		[Test]
		public void A_parent_cycle_in_imported_items_loses_no_item()
		{
			var items = new List<LmsCompetencyItem>
			{
				new LmsCompetencyItem { LmsCompetencyItemId = 1, ParentItemId = 2 },
				new LmsCompetencyItem { LmsCompetencyItemId = 2, ParentItemId = 1 },
				new LmsCompetencyItem { LmsCompetencyItemId = 3 }
			};
			LmsCompetencyGapMath.Ordered(items).Select(i => i.Item.LmsCompetencyItemId).Should().BeEquivalentTo(new[] { 1, 2, 3 });
		}

		#endregion Competency frameworks (R-C7)

		#region Training request costs

		[Test]
		public void Cost_totals_count_committed_spend_with_actual_cost_and_pending_requests_apart()
		{
			var requests = new List<LmsTrainingRequest>
			{
				new LmsTrainingRequest { UserId = "u1", Title = "Rope rescue", Provider = "Academy", CostTuition = 400m, CostTravel = 100m, Hours = 16m, Status = (int)LmsTrainingRequestStatuses.Approved, SubmittedOn = new DateTime(2027, 2, 1) },
				new LmsTrainingRequest { UserId = "U1", Title = "Rope rescue", Provider = "Academy", CostTuition = 400m, ActualCost = 380m, Hours = 16m, Status = (int)LmsTrainingRequestStatuses.Completed, SubmittedOn = new DateTime(2027, 3, 1) },
				new LmsTrainingRequest { UserId = "u2", Title = "PALS", CourseCode = "PALS-1", CostTuition = 250m, Hours = 14m, Status = (int)LmsTrainingRequestStatuses.Submitted, SubmittedOn = new DateTime(2026, 12, 1), StartsOn = new DateTime(2027, 1, 5) },
				new LmsTrainingRequest { UserId = "u2", Title = "Withdrawn", CostTuition = 999m, Status = (int)LmsTrainingRequestStatuses.Withdrawn, SubmittedOn = new DateTime(2027, 1, 1) },
				new LmsTrainingRequest { UserId = "u3", Title = "Rejected", CostTuition = 999m, Status = (int)LmsTrainingRequestStatuses.Rejected, SubmittedOn = new DateTime(2027, 1, 1) }
			};

			var byMember = LmsTrainingRequestCostMath.Group(requests, LmsTrainingRequestCostGroupings.Member, id => id.ToLowerInvariant() == "u1" ? "Alex" : "Sam");
			byMember.Should().HaveCount(2, "member ids compare case-insensitively and withdrawn or rejected requests never count");
			var alex = byMember.Single(r => r.Label == "Alex");
			alex.Committed.Should().Be(500m + 380m, "a completed request counts what it actually cost");
			alex.Hours.Should().Be(32m);
			byMember.Single(r => r.Label == "Sam").Pending.Should().Be(250m);

			var byCourse = LmsTrainingRequestCostMath.Group(requests, LmsTrainingRequestCostGroupings.Course, id => id);
			byCourse.Should().HaveCount(2);
			byCourse.Single(r => r.Requests == 2).Committed.Should().Be(880m);

			var byYear = LmsTrainingRequestCostMath.Group(requests, LmsTrainingRequestCostGroupings.Year, id => id);
			byYear.Select(r => r.Key).Should().BeEquivalentTo(new[] { "2027" }, "the class's start date decides the year when it is known");
		}

		#endregion Training request costs

		#region Reports

		[Test]
		public void Report_periods_resolve_relative_to_the_department_day()
		{
			var today = new DateTime(2027, 5, 17);
			LmsReportSchedule.ResolvePeriod(new LmsReportParameters { Period = LmsReportPeriods.MonthToDate }, today).Should().Be((new DateTime(2027, 5, 1), new DateTime(2027, 5, 18)));
			LmsReportSchedule.ResolvePeriod(new LmsReportParameters { Period = LmsReportPeriods.LastMonth }, today).Should().Be((new DateTime(2027, 4, 1), new DateTime(2027, 5, 1)));
			LmsReportSchedule.ResolvePeriod(new LmsReportParameters { Period = LmsReportPeriods.QuarterToDate }, today).Should().Be((new DateTime(2027, 4, 1), new DateTime(2027, 5, 18)));
			LmsReportSchedule.ResolvePeriod(new LmsReportParameters { Period = LmsReportPeriods.YearToDate }, today).Should().Be((new DateTime(2027, 1, 1), new DateTime(2027, 5, 18)));
			LmsReportSchedule.ResolvePeriod(new LmsReportParameters { Period = LmsReportPeriods.LastYear }, today).Should().Be((new DateTime(2026, 1, 1), new DateTime(2027, 1, 1)));
			LmsReportSchedule.ResolvePeriod(new LmsReportParameters { Period = LmsReportPeriods.Custom, From = new DateTime(2027, 3, 9), To = new DateTime(2027, 3, 2) }, today)
				.Should().Be((new DateTime(2027, 3, 2), new DateTime(2027, 3, 10)), "reversed dates are swapped and To is inclusive");
		}

		// A department two hours behind UTC.
		private static readonly Func<DateTime, DateTime> ToLocal = utc => utc.AddHours(-2);
		private static readonly Func<DateTime, DateTime> ToUtc = local => local.AddHours(2);

		[Test]
		public void Schedules_run_at_the_local_hour_strictly_after_now()
		{
			var daily = new LmsReportDefinition { Frequency = (int)LmsReportFrequencies.Daily, HourLocal = 6 };
			LmsReportSchedule.NextRunUtc(daily, new DateTime(2027, 5, 17, 7, 0, 0), ToLocal, ToUtc).Should().Be(new DateTime(2027, 5, 17, 8, 0, 0), "06:00 local is 08:00 UTC");
			LmsReportSchedule.NextRunUtc(daily, new DateTime(2027, 5, 17, 8, 0, 0), ToLocal, ToUtc).Should().Be(new DateTime(2027, 5, 18, 8, 0, 0), "a run exactly at the due time moves to the next day");

			var weekly = new LmsReportDefinition { Frequency = (int)LmsReportFrequencies.Weekly, DayOfWeek = (int)DayOfWeek.Monday, HourLocal = 7 };
			LmsReportSchedule.NextRunUtc(weekly, new DateTime(2027, 5, 17, 12, 0, 0), ToLocal, ToUtc).Should().Be(new DateTime(2027, 5, 24, 9, 0, 0), "2027-05-17 is a Monday already past 07:00");

			var monthly = new LmsReportDefinition { Frequency = (int)LmsReportFrequencies.Monthly, DayOfMonth = 1, HourLocal = 0 };
			LmsReportSchedule.NextRunUtc(monthly, new DateTime(2027, 1, 31, 23, 0, 0), ToLocal, ToUtc).Should().Be(new DateTime(2027, 2, 1, 2, 0, 0));
			var lastDay = new LmsReportDefinition { Frequency = (int)LmsReportFrequencies.Monthly, DayOfMonth = 31, HourLocal = 6 };
			LmsReportSchedule.NextRunUtc(lastDay, new DateTime(2027, 2, 1), d => d, d => d).Should().Be(new DateTime(2027, 2, 28, 6, 0, 0), "a short month runs on its last day");

			LmsReportSchedule.NextRunUtc(new LmsReportDefinition { Frequency = (int)LmsReportFrequencies.None }, DateTime.UtcNow, ToLocal, ToUtc).Should().BeNull();
			LmsReportSchedule.NextRunUtc(new LmsReportDefinition { Frequency = (int)LmsReportFrequencies.Daily, IsDeleted = true }, DateTime.UtcNow, ToLocal, ToUtc).Should().BeNull();
		}

		[Test]
		public void Csv_quotes_separators_and_defuses_formulas_but_keeps_negative_numbers()
		{
			var csv = LmsReportSchedule.ToCsv(new LmsReportTable
			{
				Columns = { "Member", "Note" },
				Rows = { new List<string> { "Smith, Jo", "=HYPERLINK(\"x\")" }, new List<string> { "-12.5", "say \"hi\"" } }
			});
			csv.Should().Be("Member,Note\r\n\"Smith, Jo\",\"'=HYPERLINK(\"\"x\"\")\"\r\n-12.5,\"say \"\"hi\"\"\"\r\n");
		}

		[Test]
		public void The_printable_report_encodes_every_value()
		{
			var html = LmsReportCatalogService.ToHtml(new LmsReportTable { Title = "<b>T</b>", Columns = { "<i>c</i>" }, Rows = { new List<string> { "<script>alert(1)</script>" } } }, "Dept & Co", null);
			html.Should().NotContain("<script>").And.NotContain("<b>T</b>").And.Contain("&lt;script&gt;").And.Contain("Dept &amp; Co");
		}

		#endregion Reports

		#region Certification cycles

		private static DepartmentCertificationType Emt => new DepartmentCertificationType
		{
			DepartmentCertificationTypeId = 3, DepartmentId = 1, Type = "EMT", RenewalCreditHoursRequired = 40m, DefaultValidityMonths = 24
		};

		private static PersonnelCertification Cycle(int id, DateTime received, DateTime expires, PersonnelCertificationStatuses status = PersonnelCertificationStatuses.Active) =>
			new PersonnelCertification { PersonnelCertificationId = id, DepartmentId = 1, UserId = "u1", DepartmentCertificationTypeId = 3, RecievedOn = received, ExpiresOn = expires, Status = (int)status };

		[Test]
		public void Auto_renew_starts_the_next_cycle_once_the_hours_are_met()
		{
			var today = new DateTime(2027, 3, 1);
			var current = Cycle(1, new DateTime(2025, 6, 1), new DateTime(2027, 5, 31));
			var onCompletion = new LmsCertificationCycleRule { AutoRenewMode = (int)LmsAutoRenewModes.OnCompletion };
			var onExpiry = new LmsCertificationCycleRule { AutoRenewMode = (int)LmsAutoRenewModes.OnExpiry };
			var mine = new List<PersonnelCertification> { current };

			LmsCertificationCycleMath.ShouldAutoRenew(onCompletion, Emt, current, 39.5m, mine, today).Should().BeFalse("the renewal hours are not met");
			LmsCertificationCycleMath.ShouldAutoRenew(onCompletion, Emt, current, 40m, mine, today).Should().BeTrue();
			LmsCertificationCycleMath.ShouldAutoRenew(onExpiry, Emt, current, 40m, mine, today).Should().BeFalse("on expiry waits for the expiry date");
			LmsCertificationCycleMath.ShouldAutoRenew(onExpiry, Emt, current, 40m, mine, new DateTime(2027, 6, 2)).Should().BeTrue();
			LmsCertificationCycleMath.ShouldAutoRenew(new LmsCertificationCycleRule(), Emt, current, 40m, mine, today).Should().BeFalse("off");

			var (start, end) = LmsCertificationCycleMath.NextCycle(current, Emt);
			start.Should().Be(new DateTime(2027, 6, 1), "the next cycle starts the day after this one ends");
			end.Should().Be(new DateTime(2029, 5, 31));

			mine.Add(Cycle(2, start, end));
			LmsCertificationCycleMath.ShouldAutoRenew(onCompletion, Emt, current, 40m, mine, today).Should().BeFalse("the member already holds the next cycle");
			LmsCertificationCycleMath.ShouldAutoRenew(onCompletion, Emt, Cycle(3, current.RecievedOn.Value, current.ExpiresOn.Value, PersonnelCertificationStatuses.Revoked), 40m,
				new List<PersonnelCertification>(), today).Should().BeFalse("a revoked record never renews");
		}

		[Test]
		public void Auto_renew_needs_renewal_hours_and_a_validity()
		{
			LmsCertificationCycleMath.CanAutoRenew(Emt).Should().BeTrue();
			LmsCertificationCycleMath.CanAutoRenew(new DepartmentCertificationType { RenewalCreditHoursRequired = 40m }).Should().BeFalse();
			LmsCertificationCycleMath.CanAutoRenew(new DepartmentCertificationType { DefaultValidityMonths = 24 }).Should().BeFalse();
			LmsCertificationCycleMath.CanAutoRenew(new DepartmentCertificationType { RenewalCreditHoursRequired = 40m, DefaultValidityMonths = 24, NeverExpires = true }).Should().BeFalse();
		}

		[Test]
		public void An_earlier_cycle_replaced_by_a_later_live_one_is_superseded()
		{
			var earlier = Cycle(1, new DateTime(2025, 6, 1), new DateTime(2027, 5, 31));
			var later = Cycle(2, new DateTime(2027, 6, 1), new DateTime(2029, 5, 31));
			CertificationCycleRules.IsSupersededByLaterCycle(earlier, new[] { earlier, later }).Should().BeTrue();
			CertificationCycleRules.IsSupersededByLaterCycle(later, new[] { earlier, later }).Should().BeFalse();
			later.Status = (int)PersonnelCertificationStatuses.Revoked;
			CertificationCycleRules.IsSupersededByLaterCycle(earlier, new[] { earlier, later }).Should().BeFalse("a revoked later record replaces nothing");
			later.Status = (int)PersonnelCertificationStatuses.Active;
			later.UserId = "someone-else";
			CertificationCycleRules.IsSupersededByLaterCycle(earlier, new[] { earlier, later }).Should().BeFalse();
		}

		#endregion Certification cycles

		#region Credential-status audiences

		[Test]
		public void Credential_status_matches_expired_expiring_and_missing_by_cycles()
		{
			var today = new DateTime(2027, 3, 1);
			var horizon = today.AddDays(30);
			var type = Emt;

			CredentialStatusAudienceService.Matches(new List<PersonnelCertification>(), type, CredentialAudienceStatuses.Missing, today, horizon).Should().BeTrue();
			CredentialStatusAudienceService.Matches(new[] { Cycle(1, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1), PersonnelCertificationStatuses.Expired) }, type,
				CredentialAudienceStatuses.Missing, today, horizon).Should().BeFalse("they held it once");

			var expired = new[] { Cycle(1, new DateTime(2025, 1, 1), new DateTime(2027, 1, 1), PersonnelCertificationStatuses.Expired) };
			CredentialStatusAudienceService.Matches(expired, type, CredentialAudienceStatuses.Expired, today, horizon).Should().BeTrue();
			CredentialStatusAudienceService.Matches(expired, type, CredentialAudienceStatuses.ExpiringWithin, today, horizon).Should().BeFalse();

			var expiring = new[] { Cycle(1, new DateTime(2025, 3, 20), new DateTime(2027, 3, 20)) };
			CredentialStatusAudienceService.Matches(expiring, type, CredentialAudienceStatuses.ExpiringWithin, today, horizon).Should().BeTrue();
			CredentialStatusAudienceService.Matches(expiring, type, CredentialAudienceStatuses.ExpiringWithin, today, today.AddDays(10)).Should().BeFalse("outside the window");
			CredentialStatusAudienceService.Matches(expiring, type, CredentialAudienceStatuses.Expired, today, horizon).Should().BeFalse();

			var renewed = expiring.Concat(new[] { Cycle(2, new DateTime(2027, 3, 21), new DateTime(2029, 3, 20)) }).ToList();
			CredentialStatusAudienceService.Matches(renewed, type, CredentialAudienceStatuses.ExpiringWithin, today, horizon).Should().BeFalse("the next cycle already carries them past it");
		}

		#endregion Credential-status audiences

		#region Settings

		[Test]
		public void Program_settings_keep_a_false_through_protobuf_and_normalize_the_currency()
		{
			var stored = new LmsProgramSettings
			{
				RequestSupervisorApproval = false,
				RequestTrainingOfficerApproval = true,
				RequestCurrency = "eur",
				FamiliarizationEnabled = true,
				FamiliarizationCategoryId = 12,
				FamiliarizationFromInspections = false,
				FamiliarizationFromPreplanReviews = false
			};

			var read = ObjectSerialization.Deserialize<LmsProgramSettings>(ObjectSerialization.Serialize(stored)).Normalized();

			read.RequestSupervisorApproval.Should().BeFalse("[DefaultValue(true)] keeps a false from reading back as true");
			read.FamiliarizationFromInspections.Should().BeFalse();
			read.FamiliarizationFromPreplanReviews.Should().BeFalse();
			read.RequestTrainingOfficerApproval.Should().BeTrue();
			read.RequestCurrency.Should().Be("EUR");
			read.FamiliarizationMinutes.Should().Be(LmsProgramSettings.DefaultFamiliarizationMinutes);
			read.RequestSteps().Should().Equal(LmsTrainingRequestSteps.TrainingOfficer);

			new LmsProgramSettings { RequestCurrency = "12$" }.Normalized().RequestCurrency.Should().Be("USD");
			new LmsProgramSettings().Normalized().RequestSteps().Should().Equal(LmsTrainingRequestSteps.Supervisor, LmsTrainingRequestSteps.TrainingOfficer);
		}

		#endregion Settings
	}
}
