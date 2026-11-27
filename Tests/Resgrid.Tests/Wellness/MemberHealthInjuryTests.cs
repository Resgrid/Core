using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Wellness;
using Resgrid.Workers.Framework.Logic;
using static Resgrid.Tests.Wellness.MemberHealthTestWorld;

namespace Resgrid.Tests.Wellness
{
	/// <summary>Injury → return to duty, the operational / protected split for supervisors, and medical clearance.</summary>
	[TestFixture]
	public class InjuryAndDutyStatusTests
	{
		private static readonly DateTime Today = DateTime.UtcNow.Date;

		[Test]
		public async Task A_member_reports_their_own_injury_without_osha_facts_and_an_officer_classifies_it()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();

			var own = await service.ReportInjuryAsync(DepartmentId, Member, new MemberInjuryInput
			{
				OccurredOn = Today.AddDays(-2).AddHours(14), Description = "Slipped on ice", InjuryDetails = "Wrist", IsRecordable = true, IsPrivacyCase = true
			});
			own.IsRecordable.Should().BeFalse("the OSHA classification is an officer's call");
			own.IsPrivacyCase.Should().BeFalse();
			own.OshaCaseNumber.Should().Be(1);
			own.OshaYear.Should().Be(Today.AddDays(-2).Year);
			world.Sent.Select(s => s.UserId).Should().BeEquivalentTo(new[] { WellnessOfficer, Designated }, "the health officers are told");
			world.Sent.Should().OnlyContain(s => s.EventCode.StartsWith("DS:") && !s.Message.Contains("Wrist"));

			var second = await service.ReportInjuryAsync(DepartmentId, WellnessOfficer, new MemberInjuryInput { UserId = Other, OccurredOn = Today.AddDays(-1), IsRecordable = true, OshaInjuryType = (int)OshaInjuryTypes.HearingLoss });
			second.IsRecordable.Should().BeTrue();
			second.OshaInjuryType.Should().Be((int)OshaInjuryTypes.HearingLoss);
			second.OshaCaseNumber.Should().Be(own.OshaYear == second.OshaYear ? 2 : 1, "case numbers run per OSHA year");

			await FluentActions.Awaiting(() => service.ReportInjuryAsync(DepartmentId, Admin, new MemberInjuryInput { UserId = Member, OccurredOn = Today }))
				.Should().ThrowAsync<UnauthorizedAccessException>("administration alone does not report for someone else");
			await FluentActions.Awaiting(() => service.UpdateInjuryAsync(DepartmentId, Member, own.RmsMemberInjuryId, new MemberInjuryInput { IsRecordable = true }, null))
				.Should().ThrowAsync<UnauthorizedAccessException>("a member does not classify their own injury");
		}

		[Test]
		public async Task An_injury_recorded_from_an_exposure_case_is_always_a_privacy_case()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var incident = world.AddIncident(Member, ExposureIncidentStages.FollowUp, Today.AddDays(-3));
			incident.CallId = 77;
			world.Calls[77] = new Call { CallId = 77, DepartmentId = DepartmentId };

			var injury = await service.ReportInjuryAsync(DepartmentId, Designated, new MemberInjuryInput
			{
				UserId = Member, ExposureIncidentId = incident.RmsExposureIncidentId, OccurredOn = Today.AddDays(-3), IsRecordable = true, IsPrivacyCase = false
			});
			injury.IsPrivacyCase.Should().BeTrue("a contaminated sharps injury is a privacy case (1904.29(b)(7))");
			injury.CallId.Should().Be(77);

			var updated = await service.UpdateInjuryAsync(DepartmentId, Designated, injury.RmsMemberInjuryId, new MemberInjuryInput { CallId = 77, IsRecordable = true, IsPrivacyCase = false }, null);
			updated.IsPrivacyCase.Should().BeTrue();
		}

		[Test]
		public async Task Duty_status_moves_through_off_and_restricted_duty_to_the_return_to_duty_clearance()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var injury = world.AddInjury(Member, Today.AddDays(-10));

			await service.SetDutyStatusAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId, (int)MemberDutyStatuses.OffDuty, Today.AddDays(-9), Today.AddDays(5), "No lifting");
			await service.SetDutyStatusAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId, (int)MemberDutyStatuses.RestrictedDuty, Today.AddDays(-4), Today.AddDays(3), null);
			(await FluentActions.Awaiting(() => service.SetDutyStatusAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId, (int)MemberDutyStatuses.OffDuty, Today.AddDays(-6), null, null))
				.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("date_before_current_status");
			(await FluentActions.Awaiting(() => service.SetDutyStatusAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId, (int)MemberDutyStatuses.FullDuty, Today, null, null))
				.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("duty_status_required");

			injury.DutyStatus.Should().Be((int)MemberDutyStatuses.RestrictedDuty);
			injury.Restrictions.Should().Be("No lifting", "a null restriction keeps the previous text");
			world.Periods.Should().HaveCount(2);
			world.Periods.Single(p => p.DutyStatus == (int)MemberDutyStatuses.OffDuty).EndsOn.Should().Be(Today.AddDays(-4));

			var cleared = await service.ClearReturnToDutyAsync(DepartmentId, Designated, injury.RmsMemberInjuryId, Today.AddDays(-1), "Cleared by physician");
			cleared.State.Should().Be((int)MemberInjuryStates.Cleared);
			cleared.DutyStatus.Should().Be((int)MemberDutyStatuses.FullDuty);
			cleared.ReturnedToDutyOn.Should().Be(Today.AddDays(-1));
			world.Periods.Should().OnlyContain(p => p.EndsOn.HasValue);
			(await FluentActions.Awaiting(() => service.ClearReturnToDutyAsync(DepartmentId, Designated, injury.RmsMemberInjuryId, Today, null))
				.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("injury_not_open");

			var (away, restricted) = OshaRules.CountDays(injury.OccurredOn, world.Periods, Today);
			away.Should().Be(5, "days 9 to 5 before today off duty");
			restricted.Should().Be(3, "days 4 to 2 before today restricted");
		}

		[Test]
		public async Task Supervisors_see_duty_and_clearance_status_only_and_only_for_the_members_they_supervise()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			world.AddInjury(Member, Today.AddDays(-3), MemberDutyStatuses.OffDuty, restrictions: "No lifting over 10 kg");
			world.AddInjury(OutsideGroup, Today.AddDays(-3), MemberDutyStatuses.RestrictedDuty);
			world.AddInjury(Hidden, Today.AddDays(-3), MemberDutyStatuses.OffDuty);
			world.Clearances.Add(new RmsMedicalClearance { RmsMedicalClearanceId = "c1", DepartmentId = DepartmentId, UserId = Other, Status = (int)MedicalClearanceStatuses.ClearedWithRestrictions,
				EvaluatedOn = Today.AddDays(-30), RestrictionDetail = "Asthma: no SCBA interior" });

			var adminBoard = await service.GetDutyBoardAsync(DepartmentId, Admin);
			adminBoard.Select(s => s.UserId).Should().BeEquivalentTo(new[] { Member, OutsideGroup, Other }, "department administrators see everyone active, not hidden members");
			var groupBoard = await service.GetDutyBoardAsync(DepartmentId, GroupAdmin);
			groupBoard.Select(s => s.UserId).Should().BeEquivalentTo(new[] { Member, Other }, "a group admin sees their own group");
			(await service.GetDutyBoardAsync(DepartmentId, Other)).Select(s => s.UserId).Should().BeEquivalentTo(new[] { Other }, "anyone else sees only themselves");
			adminBoard.Single(s => s.UserId == Other).ClearanceStatus.Should().Be(MedicalClearanceStatuses.ClearedWithRestrictions);

			await FluentActions.Awaiting(() => service.GetDutyStatusAsync(DepartmentId, GroupAdmin, OutsideGroup)).Should().ThrowAsync<UnauthorizedAccessException>();
			(await service.GetDutyStatusAsync(DepartmentId, GroupAdmin, Member)).DutyStatus.Should().Be(MemberDutyStatuses.OffDuty);

			// The projection can never carry a reason: it has no text field other than the member id.
			typeof(MemberDutyStatus).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).Should().BeEquivalentTo(new[] { "UserId" });

			// The supervisor cannot open the record behind the status.
			(await service.GetInjuryAsync(DepartmentId, GroupAdmin, world.Injuries[0].RmsMemberInjuryId)).Should().BeNull();
			(await service.GetInjuryAsync(DepartmentId, Admin, world.Injuries[0].RmsMemberInjuryId)).Should().BeNull();
			await FluentActions.Awaiting(() => service.GetClearancesForMemberAsync(DepartmentId, Admin, Other)).Should().ThrowAsync<UnauthorizedAccessException>();
			(await service.GetOpenInjuriesAsync(DepartmentId, WellnessOfficer)).Should().OnlyContain(i => i.Restrictions == null && i.Description == null, "the officer list is operational");
		}

		[Test]
		public async Task A_medical_clearance_is_a_certification_of_the_clearance_type_with_its_status_and_not_cleared_suspends_it()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();

			var restricted = await service.RecordClearanceAsync(DepartmentId, WellnessOfficer, new MedicalClearanceInput
			{
				UserId = Member, Status = (int)MedicalClearanceStatuses.ClearedWithRestrictions, EvaluatedOn = Today.AddDays(-1), RestrictionDetail = "No interior attack"
			});
			var certification = world.Certifications.Single();
			certification.DepartmentCertificationTypeId.Should().Be(ClearanceTypeId);
			certification.Status.Should().Be((int)PersonnelCertificationStatuses.Active);
			certification.ExpiresOn.Should().Be(Today.AddDays(-1).AddMonths(12), "the type's validity sets the next evaluation");
			certification.StatusReason.Should().BeNull();
			restricted.PersonnelCertificationId.Should().Be(certification.PersonnelCertificationId);
			restricted.ValidUntil.Should().Be(Today.AddDays(-1).AddMonths(12));

			await service.RecordClearanceAsync(DepartmentId, Designated, new MedicalClearanceInput { UserId = Other, Status = (int)MedicalClearanceStatuses.NotCleared, EvaluatedOn = Today });
			var suspended = world.Certifications.Last();
			suspended.Status.Should().Be((int)PersonnelCertificationStatuses.Suspended);
			suspended.StatusReason.Should().Be("not_cleared", "the reason on the certification is a code, never the restriction");

			var current = await service.GetCurrentClearancesAsync(DepartmentId, WellnessOfficer);
			current.Should().HaveCount(2).And.OnlyContain(c => c.RestrictionDetail == null);
			(await service.GetClearancesForMemberAsync(DepartmentId, Member, Member)).Single().RestrictionDetail.Should().Be("No interior attack");

			await FluentActions.Awaiting(() => service.RecordClearanceAsync(DepartmentId, Admin, new MedicalClearanceInput { UserId = Member, Status = 1, EvaluatedOn = Today }))
				.Should().ThrowAsync<UnauthorizedAccessException>();
			world.Audits.Should().NotContain(a => (a.After ?? string.Empty).Contains("No interior attack"));

			world.Settings.MedicalClearanceCertificationTypeId = 0;
			var plain = await service.RecordClearanceAsync(DepartmentId, WellnessOfficer, new MedicalClearanceInput { UserId = Member, Status = 1, EvaluatedOn = Today });
			plain.PersonnelCertificationId.Should().BeNull("without a named type the clearance stays in Wellness only");
			(await service.GetDutyStatusAsync(DepartmentId, Member, Member)).ClearanceStatus.Should().Be(MedicalClearanceStatuses.Cleared, "the latest evaluation is the current one");
		}
	}

	/// <summary>The OSHA 300 / 301 shapes: day counts, the outcome column, case numbers and privacy-case masking.</summary>
	[TestFixture]
	public class OshaLogTests
	{
		private static RmsInjuryDutyPeriod Period(MemberDutyStatuses status, DateTime start, DateTime? end) =>
			new RmsInjuryDutyPeriod { DutyStatus = (int)status, StartsOn = start, EndsOn = end };

		[Test]
		public void Days_do_not_count_the_day_of_injury_and_stop_at_180_together()
		{
			var injured = new DateTime(2026, 3, 1, 15, 0, 0);
			// Off duty from the day of the injury for ten days: the injury day itself is not counted.
			OshaRules.CountDays(injured, new[] { Period(MemberDutyStatuses.OffDuty, new DateTime(2026, 3, 1), new DateTime(2026, 3, 11)) }, new DateTime(2026, 12, 31))
				.Should().Be((9, 0));
			// An open restricted stretch counts up to the as-of date inclusive.
			OshaRules.CountDays(injured, new[] { Period(MemberDutyStatuses.RestrictedDuty, new DateTime(2026, 3, 5), null) }, new DateTime(2026, 3, 14))
				.Should().Be((0, 10));
			// 150 days away then 100 restricted: capped at 180 together.
			OshaRules.CountDays(injured, new[]
			{
				Period(MemberDutyStatuses.OffDuty, new DateTime(2026, 3, 2), new DateTime(2026, 3, 2).AddDays(150)),
				Period(MemberDutyStatuses.RestrictedDuty, new DateTime(2026, 3, 2).AddDays(150), new DateTime(2026, 3, 2).AddDays(250))
			}, new DateTime(2027, 6, 1)).Should().Be((150, 30));
			// Overlapping stretches count a day once, as away.
			OshaRules.CountDays(injured, new[]
			{
				Period(MemberDutyStatuses.OffDuty, new DateTime(2026, 3, 2), new DateTime(2026, 3, 5)),
				Period(MemberDutyStatuses.RestrictedDuty, new DateTime(2026, 3, 3), new DateTime(2026, 3, 6))
			}, new DateTime(2026, 12, 31)).Should().Be((3, 1));
		}

		[Test]
		public void The_outcome_is_the_most_serious_and_the_name_is_withheld_for_a_privacy_case()
		{
			OshaRules.Outcome(new RmsMemberInjury { DiedOn = new DateTime(2026, 2, 2) }, 3, 4).Should().Be(OshaCaseOutcomes.Death);
			OshaRules.Outcome(new RmsMemberInjury(), 1, 9).Should().Be(OshaCaseOutcomes.DaysAwayFromWork);
			OshaRules.Outcome(new RmsMemberInjury(), 0, 2).Should().Be(OshaCaseOutcomes.JobTransferOrRestriction);
			OshaRules.Outcome(new RmsMemberInjury(), 0, 0).Should().Be(OshaCaseOutcomes.OtherRecordable);
			OshaRules.LogName(new RmsMemberInjury { IsPrivacyCase = true }, "Mia Member").Should().Be(OshaRules.PrivacyCaseName);
			OshaRules.LogName(new RmsMemberInjury(), "Mia Member").Should().Be("Mia Member");
			OshaRules.CaseNumber(2026, 7).Should().Be("2026-007");
			OshaRules.OnLog(new[]
			{
				new RmsMemberInjury { IsRecordable = true, OshaYear = 2026, OshaCaseNumber = 2 },
				new RmsMemberInjury { IsRecordable = false, OshaYear = 2026, OshaCaseNumber = 1 },
				new RmsMemberInjury { IsRecordable = true, OshaYear = 2026, OshaCaseNumber = 3, State = (int)MemberInjuryStates.Voided },
				new RmsMemberInjury { IsRecordable = true, OshaYear = 2025, OshaCaseNumber = 1 }
			}, 2026).Select(i => i.OshaCaseNumber).Should().Equal(2);
		}

		[Test]
		public async Task The_year_log_masks_privacy_cases_in_every_300_export_and_the_301_keeps_the_confidential_name()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var year = DateTime.UtcNow.Year;
			var privacy = world.AddInjury(Member, new DateTime(year, 1, 10, 9, 30, 0, DateTimeKind.Utc));
			privacy.IsRecordable = true;
			privacy.IsPrivacyCase = true;
			privacy.InjuryDetails = "Needlestick, left index finger";
			var plain = world.AddInjury(Other, new DateTime(year, 1, 12, 9, 30, 0, DateTimeKind.Utc));
			plain.IsRecordable = true;
			plain.InjuryDetails = ProtectedDataEnvelope.RedactionValue;
			world.Periods.Add(new RmsInjuryDutyPeriod { DepartmentId = DepartmentId, InjuryId = plain.RmsMemberInjuryId, DutyStatus = (int)MemberDutyStatuses.OffDuty,
				StartsOn = new DateTime(year, 1, 12), EndsOn = new DateTime(year, 1, 15) });
			world.AddInjury(Other, new DateTime(year, 1, 13)).IsRecordable = false;

			var names = new Dictionary<string, string> { [Member] = "Mia Member", [Other] = "Otto Other" };
			await FluentActions.Awaiting(() => service.BuildOshaLogAsync(DepartmentId, Admin, year, id => names[id])).Should().ThrowAsync<UnauthorizedAccessException>();
			var log = await service.BuildOshaLogAsync(DepartmentId, Designated, year, id => names[id]);

			log.Rows.Should().HaveCount(2);
			log.Rows[0].EmployeeName.Should().Be(OshaRules.PrivacyCaseName);
			log.Rows[1].EmployeeName.Should().Be("Otto Other");
			log.Rows[1].DaysAway.Should().Be(2);
			log.Rows[1].Outcome.Should().Be(OshaCaseOutcomes.DaysAwayFromWork);
			log.Reports[0].EmployeeName.Should().Be("Mia Member", "the 301 is the confidential incident report");
			log.Reports[0].TimeOfEvent.Should().Be("09:30");

			var labels = new Dictionary<string, string> { ["Osha.PrivacyCaseName"] = "Cas confidentiel" };
			var csv = MemberHealthDocuments.LogCsv(log, labels);
			csv.Should().NotContain("Mia Member").And.Contain("Cas confidentiel").And.Contain("Needlestick");
			csv.Should().NotContain(ProtectedDataEnvelope.RedactionValue).And.Contain("Withheld (protected)");
			var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
			lines.Should().HaveCount(4, "header, two cases and the totals row");
			lines[3].Should().StartWith("\"Totals\"");
			var html = MemberHealthDocuments.LogHtml(log, labels, "fr", false, DateTime.UtcNow);
			html.Should().NotContain("Mia Member").And.Contain("Cas confidentiel").And.NotContain("rgdp:");
			MemberHealthDocuments.ReportsCsv(log, null).Should().Contain("Mia Member");
			MemberHealthDocuments.ReportsHtml(log, null, "en", false, DateTime.UtcNow).Should().Contain("Privacy case").And.Contain("page-break-before");
			world.Audits.Should().Contain(a => a.Type == AuditLogTypes.OshaLogExported);
		}

		[Test]
		public void Csv_cells_are_formula_safe_and_html_is_encoded()
		{
			var log = new OshaLog
			{
				Year = 2026,
				EstablishmentName = "<b>Station</b>",
				Rows = { new OshaLogRow { CaseNumber = "2026-001", EmployeeName = "=HYPERLINK(\"x\")", Description = "<script>x</script>", DateOfInjury = new DateTime(2026, 1, 1) } }
			};
			MemberHealthDocuments.LogCsv(log, null).Should().NotContain(",=HYPERLINK");
			var html = MemberHealthDocuments.LogHtml(log, null, "en", false, DateTime.UtcNow);
			html.Should().NotContain("<script>").And.NotContain("<b>Station</b>");
		}
	}

	/// <summary>Retention, legal hold, worker 91's WL1 steps, settings, ADP catalog 38 and the exclusion guards.</summary>
	[TestFixture]
	public class MemberHealthRetentionAndWorkerTests
	{
		[Test]
		public async Task Retention_erases_only_records_past_their_minimum_that_no_legal_hold_covers()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
			var oldCase = world.AddIncident(Member, ExposureIncidentStages.Closed, now.AddYears(-31));
			world.Assignments.Add(new RmsRecordWorkAssignment { RmsRecordWorkAssignmentId = "t1", DepartmentId = DepartmentId, RecordId = oldCase.RmsExposureIncidentId, Purpose = RmsWorkAssignmentPurposes.ExposureCaseFollowUp });
			var heldCase = world.AddIncident(Other, ExposureIncidentStages.Closed, now.AddYears(-31));
			var recentCase = world.AddIncident(Other, ExposureIncidentStages.Closed, now.AddYears(-29));
			var oldInjury = world.AddInjury(Member, new DateTime(2020, 6, 1));
			world.Periods.Add(new RmsInjuryDutyPeriod { DepartmentId = DepartmentId, InjuryId = oldInjury.RmsMemberInjuryId, StartsOn = new DateTime(2020, 6, 1) });
			var keptInjury = world.AddInjury(Member, new DateTime(2021, 6, 1));
			var heldInjury = world.AddInjury(Other, new DateTime(2019, 6, 1));
			world.Clearances.Add(new RmsMedicalClearance { RmsMedicalClearanceId = "old", DepartmentId = DepartmentId, UserId = Member, EvaluatedOn = now.AddYears(-31) });
			world.Clearances.Add(new RmsMedicalClearance { RmsMedicalClearanceId = "new", DepartmentId = DepartmentId, UserId = Member, EvaluatedOn = now.AddYears(-2) });
			world.Holds.Add(new RmsRecordLegalHold { DepartmentId = DepartmentId, RecordId = heldCase.RmsExposureIncidentId });
			world.Holds.Add(new RmsRecordLegalHold { DepartmentId = DepartmentId, DefinitionKey = MemberHealthRules.InjuryHoldKey, PeriodStart = new DateTime(2019, 1, 1), PeriodEnd = new DateTime(2019, 12, 31) });

			var erased = await service.EraseExpiredAsync(now);

			erased.Should().Be(3);
			world.Incidents.Select(i => i.RmsExposureIncidentId).Should().BeEquivalentTo(new[] { heldCase.RmsExposureIncidentId, recentCase.RmsExposureIncidentId });
			world.Assignments.Should().BeEmpty("the erased case's follow-up tasks go with it");
			world.Injuries.Select(i => i.RmsMemberInjuryId).Should().BeEquivalentTo(new[] { keptInjury.RmsMemberInjuryId, heldInjury.RmsMemberInjuryId },
				"a 2020 injury is past five years after its year; 2021 is not; the 2019 one is under a hold");
			world.Periods.Should().BeEmpty();
			world.Clearances.Select(c => c.RmsMedicalClearanceId).Should().BeEquivalentTo(new[] { "new" });
		}

		[Test]
		public void Injury_retention_counts_from_the_end_of_the_year_and_never_goes_below_five()
		{
			var injury = new RmsMemberInjury { OshaYear = 2020 };
			OshaRules.IsPastRetention(injury, 5, new DateTime(2025, 12, 31)).Should().BeFalse();
			OshaRules.IsPastRetention(injury, 5, new DateTime(2026, 1, 2)).Should().BeTrue();
			OshaRules.IsPastRetention(injury, 1, new DateTime(2025, 12, 31)).Should().BeFalse("the setting is clamped to the OSHA minimum");
			OshaRules.IsPastRetention(injury, 10, new DateTime(2026, 1, 2)).Should().BeFalse();
		}

		[Test]
		public async Task Due_notices_are_claimed_before_sending_skip_inactive_members_and_never_repeat()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var now = DateTime.UtcNow;
			var overdue = world.AddIncident(Member, ExposureIncidentStages.NotificationRequested, now.AddDays(-4));
			overdue.NotificationRequestedOn = now.AddDays(-3);
			overdue.NotificationDueOn = now.AddDays(-1);
			var hiddenOverdue = world.AddIncident(Hidden, ExposureIncidentStages.NotificationRequested, now.AddDays(-4));
			hiddenOverdue.NotificationDueOn = now.AddDays(-1);
			var notYet = world.AddIncident(Other, ExposureIncidentStages.NotificationRequested, now.AddDays(-1));
			notYet.NotificationDueOn = now.AddHours(20);
			var review = world.AddInjury(Other, now.AddDays(-10), MemberDutyStatuses.OffDuty);
			review.ExpectedReturnOn = now.Date.AddDays(-1);

			var sent = await service.SendDueNoticesAsync(now);

			world.Sent.Where(s => s.EventCode.StartsWith("EI:")).Select(s => s.UserId).Should().BeEquivalentTo(new[] { Designated });
			world.Sent.Where(s => s.EventCode.StartsWith("DS:")).Select(s => s.UserId).Should().BeEquivalentTo(new[] { WellnessOfficer, Designated });
			sent.Should().Be(3);
			hiddenOverdue.NotificationOverdueNoticeOn.Should().NotBeNull("claimed, then skipped: nobody is told about a hidden member");
			world.Sent.Should().NotContain(s => s.Message.Contains("Hal Hidden"));
			notYet.NotificationOverdueNoticeOn.Should().BeNull();

			world.Sent.Clear();
			(await service.SendDueNoticesAsync(now.AddMinutes(15))).Should().Be(0, "the claim keeps a second run from sending again");
			world.Sent.Should().BeEmpty();

			// A new expected return (set after the last review) asks again.
			review.ExpectedReturnOn = now.AddHours(2);
			(await service.SendDueNoticesAsync(now.AddHours(3))).Should().Be(2);
		}

		[Test]
		public async Task Worker_91_runs_the_wl1_steps_from_due_work_queries_only_and_counts_a_failure_without_stopping()
		{
			var world = new MemberHealthTestWorld();
			var wellness = new Mock<IMemberWellnessService>();
			wellness.Setup(w => w.ProcessPendingFlagsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WellnessRunResult());
			var now = DateTime.UtcNow;

			var result = await WellnessLogic.Process(wellness.Object, world.Service(), now);

			result.Failures.Should().Be(0);
			world.IncidentsRepository.Verify(r => r.GetOverdueNotificationsAsync(now, It.IsAny<int>()), Times.Once);
			world.InjuriesRepository.Verify(r => r.GetDueReturnReviewsAsync(now, It.IsAny<int>()), Times.Once);
			world.IncidentsRepository.Verify(r => r.GetDepartmentsWithCasesBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Once);
			world.InjuriesRepository.Verify(r => r.GetDepartmentsWithInjuriesBeforeYearAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
			world.ClearancesRepository.Verify(r => r.GetDepartmentsWithClearancesBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Once);
			world.Departments.Verify(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never, "an idle run sweeps no department");
			world.Wellness.Verify(w => w.GetSettingsAsync(It.IsAny<int>()), Times.Never);

			var failing = new Mock<IMemberHealthService>();
			failing.Setup(h => h.SendDueNoticesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
			failing.Setup(h => h.EraseExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);
			var second = await WellnessLogic.Process(wellness.Object, failing.Object, now);
			second.Failures.Should().Be(1);
			second.HealthRecordsErased.Should().Be(4);
		}

		[Test]
		public void The_wl1_settings_survive_a_protobuf_round_trip_including_the_five_year_default()
		{
			var stored = ObjectSerialization.Serialize(new WellnessSettings { DesignatedOfficerRoleId = 3, BbpTrainingCertificationTypeId = 8, MedicalClearanceCertificationTypeId = 9, InjuryRetentionYears = 12 });
			var read = ObjectSerialization.Deserialize<WellnessSettings>(stored);
			read.DesignatedOfficerRoleId.Should().Be(3);
			read.BbpTrainingCertificationTypeId.Should().Be(8);
			read.MedicalClearanceCertificationTypeId.Should().Be(9);
			read.InjuryRetentionYears.Should().Be(12);
			ObjectSerialization.Deserialize<WellnessSettings>(ObjectSerialization.Serialize(new WellnessSettings())).InjuryRetentionYears.Should().Be(5);
			new WellnessSettings { InjuryRetentionYears = 2 }.Normalized().InjuryRetentionYears.Should().Be(5);
		}

		[Test]
		public void Catalog_38_protects_the_narratives_and_never_the_operational_columns()
		{
			var catalog = new ProtectedFieldCatalog();
			foreach (var table in new[] { "RmsExposureIncidents", "RmsMemberInjuries", "RmsMedicalClearances" })
			{
				var columns = catalog.GetForTable(table).Select(f => f.ColumnName).ToList();
				columns.Should().NotBeEmpty();
				columns.Should().NotIntersectWith(MemberHealthProtectedFields.OperationalColumns[table], "member, dates, stage, duty and clearance status stay operational (§25.4 item 4c)");
				catalog.GetForTable(table).Should().OnlyContain(f => f.AddedInCatalogVersion == MemberHealthProtectedFields.CatalogVersion && f.Family == "Personnel");
				AdpTableBindings.V1.Should().ContainSingle(b => b.TableName == table);
			}
			catalog.GetForTable("RmsInjuryDutyPeriods").Should().BeEmpty("a duty stretch is operational");
			MemberHealthProtectedFields.ExposureIncidents.Keys.Concat(MemberHealthProtectedFields.Injuries.Keys).Concat(MemberHealthProtectedFields.Clearances.Keys)
				.Should().BeEquivalentTo(MemberHealthProtectedFields.All().Select(f => f.Table.ToLowerInvariant() + "." + f.Column.ToLowerInvariant()));
		}

		[Test]
		public void Unified_search_indexes_no_health_record()
		{
			SearchEntityTypes.Indexed.Should().NotContain(t => t.IndexOf("Injur", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Clearance", StringComparison.OrdinalIgnoreCase) >= 0
				|| t.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Osha", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		[Test]
		public async Task The_gdpr_export_holds_only_the_members_own_records()
		{
			var world = new MemberHealthTestWorld();
			world.AddIncident(Member, ExposureIncidentStages.Reported, DateTime.UtcNow.AddDays(-2));
			world.AddIncident(Other, ExposureIncidentStages.Reported, DateTime.UtcNow.AddDays(-2));
			world.AddInjury(Member, DateTime.UtcNow.AddDays(-2));
			world.AddInjury(Other, DateTime.UtcNow.AddDays(-2)).Description = "OTHER-ONLY";

			var json = Newtonsoft.Json.JsonConvert.SerializeObject(await world.Service().BuildGdprExportAsync(Member, DepartmentId));

			json.Should().Contain("Fell from the tailboard").And.NotContain("OTHER-ONLY");
			Newtonsoft.Json.Linq.JObject.Parse(json)["ExposureCases"]!.Count().Should().Be(1);
		}

		[Test]
		public void The_nfpa_1582_clearance_template_exists()
		{
			var template = Resgrid.Model.Certifications.CertificationTypeTemplateCatalog.GetById("nfpa-1582");
			template.Should().NotBeNull();
			template.Category.Should().Be(Resgrid.Model.CertificationCategories.Medical);
			template.DefaultValidityMonths.Should().Be(12);
		}
	}
}
