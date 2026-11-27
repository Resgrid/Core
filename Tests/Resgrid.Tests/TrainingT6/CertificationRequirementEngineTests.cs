using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT6
{
	/// <summary>
	/// Round 9A: the certification engine. A requirement set issues or renews only when every item is complete inside the cycle window (a
	/// supervised practical only when an instructor recorded it); a void takes back what a voided completion issued or renewed (previous expiry,
	/// start date and status restored, or the record withdrawn) unless another valid completion still covers it; a later issuance inherits
	/// the earlier state; a record changed by hand is left for an officer; suspended and revoked records are never touched; renewal hours
	/// that drop below the requirement withdraw the next cycle; "requirements due" goes once per record and stage, never to inactive members.
	/// </summary>
	[TestFixture]
	public class CertificationRequirementEngineTests
	{
		private const int Dept = CertificationWorld.Dept;
		private const string Member = CertificationWorld.Member;

		private static DateTime LocalToday(CertificationWorld world) => TrainingDueDates.LocalDate(DateTime.UtcNow, world.Department);

		private static LmsCertificationRequirementItem Course(int id, bool practical = false, string label = null) =>
			new LmsCertificationRequirementItem { Kind = (int)LmsRequirementKinds.Course, TargetRefId = id, RequireRecordedByInstructor = practical, Label = label };

		private static LmsCertificationRequirementItem Training(int id, bool practical = false, string label = null) =>
			new LmsCertificationRequirementItem { Kind = (int)LmsRequirementKinds.Training, TargetRefId = id, RequireRecordedByInstructor = practical, Label = label };

		#region Requirement sets

		[Test]
		public async Task A_set_issues_only_when_every_item_is_complete_inside_the_cycle_window()
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator");
			world.AddSet(5, null, Course(31), Course(32), Training(40, practical: true, label: "Slow speed course"));
			var now = DateTime.UtcNow;
			world.CourseFacts.Add(CertificationWorld.Fact(Member, 31, now.AddDays(-20), 1001));
			world.CourseFacts.Add(CertificationWorld.Fact(Member, 32, now.AddMonths(-14), 1002));
			world.TrainingFacts.Add(CertificationWorld.Fact(Member, 40, now.AddDays(-3), 1003, recordedByInstructor: true));

			var progress = (await world.Requirements.GetMemberProgressAsync(Dept, Member, now)).Single();
			progress.Done.Should().Be(2, "the second course was completed before the window (the last 12 months for a first issue)");
			progress.Total.Should().Be(3);
			progress.Complete.Should().BeFalse();
			progress.State.Should().Be(LmsCertificationProgressStates.NotHeld);
			progress.Items.Single(i => i.Label == "Slow speed course").LastRecordedByOther.Should().BeTrue();
			(await world.Requirements.EvaluateMembersAsync(Dept, new[] { Member }, null, now)).Issued.Should().Be(0);
			world.Records.Should().BeEmpty();

			world.CourseFacts.Add(CertificationWorld.Fact(Member, 32, now.AddMinutes(-5), 1004));
			var run = await world.Requirements.EvaluateMembersAsync(Dept, new[] { Member }, Member, now);

			run.Issued.Should().Be(1);
			var record = world.Records.Should().ContainSingle().Subject;
			record.UserId.Should().Be(Member);
			record.DepartmentCertificationTypeId.Should().Be(5);
			record.ExpiresOn.Should().Be(TrainingDueDates.LocalDate(now.AddMinutes(-5), world.Department).AddMonths(12), "rolling 12 months from the last item, on the department's date");
			var issuance = world.Ledger.Should().ContainSingle().Subject;
			issuance.Source.Should().Be((int)LmsCertificationIssueSources.RequirementSet);
			issuance.Action.Should().Be((int)LmsCertificationIssueActions.Issued);
			issuance.Basis.Should().BeEquivalentTo(new[] { 1001, 1003, 1004 });
			world.Notices.Should().ContainSingle(n => n.Message == "CertReq.Notify.Issued" && n.To.Single() == Member && n.Code == "CR:" + record.PersonnelCertificationId);
			world.WorkflowEvents.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.CertificationIssuedByTraining && e.Source == "RequirementSet");
			world.Audits.Should().Contain(a => a.Type == AuditLogTypes.CertificationIssuedByTraining);

			// Evaluating again changes nothing: the window now starts after the completions that issued it.
			(await world.Requirements.EvaluateMembersAsync(Dept, new[] { Member }, Member, now)).Issued.Should().Be(0);
			world.Ledger.Should().ContainSingle();
		}

		[Test]
		public async Task A_supervised_practical_counts_only_an_instructor_recorded_completion()
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator");
			world.AddSet(5, null, Training(40, practical: true));
			world.TrainingFacts.Add(CertificationWorld.Fact(Member, 40, DateTime.UtcNow.AddDays(-1), 2001, recordedByInstructor: false));

			(await world.Requirements.EvaluateMembersAsync(Dept, new[] { Member }, null, DateTime.UtcNow)).Issued.Should().Be(0, "the member's own completion is not a supervised practical");

			world.TrainingFacts.Add(CertificationWorld.Fact(Member, 40, DateTime.UtcNow.AddMinutes(-1), 2002, recordedByInstructor: true));
			(await world.Requirements.EvaluateMembersAsync(Dept, new[] { Member }, null, DateTime.UtcNow)).Issued.Should().Be(1);
		}

		[Test]
		public async Task Hours_and_evaluations_add_up_and_a_holder_renews_to_the_next_year_end()
		{
			var world = new CertificationWorld();
			world.AddType(6, "Hazmat technician", t => t.ValidityRule = (int)CertificationValidityRules.CalendarYearEnd);
			var today = LocalToday(world);
			var record = world.AddRecord(300, Member, 6, new DateTime(today.Year, 12, 31), received: new DateTime(today.Year - 1, 12, 1));
			world.AddSet(6, null,
				new LmsCertificationRequirementItem { Kind = (int)LmsRequirementKinds.CategoryHours, TargetRefId = 7, RequiredHours = 8m },
				new LmsCertificationRequirementItem { Kind = (int)LmsRequirementKinds.Evaluations, TargetCode = "SS-ENTRY", RequiredCount = 2 });
			var now = DateTime.UtcNow;
			world.HourFacts.Add(CertificationWorld.Fact(Member, 7, now.AddMinutes(-30), 3001, amount: 5m));
			world.EvaluationFacts.Add(CertificationWorld.Fact(Member, 0, now.AddMinutes(-20), 3002, code: "SS-ENTRY"));
			world.EvaluationFacts.Add(CertificationWorld.Fact(Member, 0, now.AddMinutes(-15), 3003, code: "ss-entry"));

			var progress = (await world.Requirements.GetMemberProgressAsync(Dept, Member, now)).Single();
			progress.Items[0].Progress.Should().Be(5m);
			progress.Items[0].Done.Should().BeFalse();
			progress.Items[1].Done.Should().BeTrue();
			progress.IsRenewal.Should().BeTrue();

			world.HourFacts.Add(CertificationWorld.Fact(Member, 7, now.AddMinutes(-10), 3004, amount: 3.5m));
			var run = await world.Requirements.EvaluateMembersAsync(Dept, new[] { Member }, null, now);

			run.Renewed.Should().Be(1);
			record.ExpiresOn.Should().Be(new DateTime(today.Year + 1, 12, 31));
			var issuance = world.Ledger.Single();
			issuance.Action.Should().Be((int)LmsCertificationIssueActions.Renewed);
			issuance.PreviousExpiresOn.Should().Be(new DateTime(today.Year, 12, 31));
			issuance.PreviousRecievedOn.Should().Be(new DateTime(today.Year - 1, 12, 1));
			world.Notices.Should().ContainSingle(n => n.Message == "CertReq.Notify.Renewed");
			world.WorkflowEvents.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.CertificationRenewedByTraining);
		}

		[Test]
		public async Task A_renew_only_set_never_issues_to_a_member_who_does_not_hold_it()
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator");
			world.AddSet(5, s => s.IssueToNonHolders = false, Course(31));
			world.CourseFacts.Add(CertificationWorld.Fact(Member, 31, DateTime.UtcNow.AddMinutes(-5), 1001));

			(await world.Requirements.EvaluateMembersAsync(Dept, new[] { Member }, null, DateTime.UtcNow)).Issued.Should().Be(0);
			world.Records.Should().BeEmpty();
			(await world.Requirements.GetMemberProgressAsync(Dept, Member, DateTime.UtcNow)).Should().BeEmpty("a renew-only set concerns holders");
		}

		[Test]
		public async Task Requirements_due_is_sent_once_per_record_and_stage_and_inactive_holders_are_skipped()
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator", t => t.RenewalGraceDays = 30);
			var today = LocalToday(world);
			var due = world.AddRecord(301, Member, 5, today.AddDays(10));
			world.AddRecord(302, CertificationWorld.Gone, 5, today.AddDays(10));
			world.AddSet(5, s => s.DueSoonDays = 30, Course(31), Course(32));
			world.CourseFacts.Add(CertificationWorld.Fact(Member, 31, DateTime.UtcNow.AddDays(-2), 1001));

			var first = await world.Requirements.RunDueAsync(DateTime.UtcNow);
			first.DueNotices.Should().Be(1);
			var notice = world.Notices.Should().ContainSingle().Subject;
			notice.To.Should().Equal(Member);
			notice.Message.Should().Be("CertReq.Notify.Due");
			notice.Args[2].Should().Be(1);
			notice.Args[3].Should().Be(2);
			world.WorkflowEvents.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.CertificationRequirementsDue && e.ItemsDone == 1 && e.ItemsTotal == 2);

			// The same day again (the claim holds), then a later run: still once for this record and stage.
			(await world.Requirements.RunDueAsync(DateTime.UtcNow)).Departments.Should().Be(0);
			world.ClaimedDay = null;
			(await world.Requirements.RunDepartmentAsync(Dept, DateTime.UtcNow)).DueNotices.Should().Be(0);
			world.Notices.Should().ContainSingle();

			// In the grace period it is a new stage: told once more.
			due.ExpiresOn = today.AddDays(-3);
			due.Status = (int)PersonnelCertificationStatuses.Expired;
			(await world.Requirements.RunDepartmentAsync(Dept, DateTime.UtcNow)).DueNotices.Should().Be(1);
			world.Notices.Last().Message.Should().Be("CertReq.Notify.DueGrace");
		}

		[Test]
		public async Task The_engine_pauses_while_the_add_on_is_not_active()
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator");
			world.AddSet(5, null, Course(31));
			world.CourseFacts.Add(CertificationWorld.Fact(Member, 31, DateTime.UtcNow.AddMinutes(-5), 1001));
			world.Access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(false);

			var run = await world.Requirements.RunDueAsync(DateTime.UtcNow);
			run.Skipped.Should().Be(1);
			world.Records.Should().BeEmpty();
		}

		[Test]
		public async Task Saving_a_set_validates_its_items()
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator");
			world.AddType(8, "Generator", t => t.AppliesTo = (int)CertificationAppliesTo.Unit);

			Func<LmsCertificationRequirementSetInput, Task> save = input => world.Requirements.SaveSetAsync(Dept, input, CertificationWorld.Officer);
			await save.Invoking(s => s(new LmsCertificationRequirementSetInput { DepartmentCertificationTypeId = 5 })).Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_cert_req_items_required");
			await save.Invoking(s => s(new LmsCertificationRequirementSetInput { DepartmentCertificationTypeId = 8, Items = { new LmsCertificationRequirementItemInput { Kind = 0, TargetRefId = 31 } } }))
				.Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_cert_req_type_invalid");
			await save.Invoking(s => s(new LmsCertificationRequirementSetInput
			{
				DepartmentCertificationTypeId = 5, Items = { new LmsCertificationRequirementItemInput { Kind = (int)LmsRequirementKinds.CategoryHours, TargetRefId = 7, RequiredHours = 4m, RequireRecordedByInstructor = true } }
			})).Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_cert_req_item_practical_kind");
			await save.Invoking(s => s(new LmsCertificationRequirementSetInput
			{
				DepartmentCertificationTypeId = 5,
				Items = { new LmsCertificationRequirementItemInput { Kind = 0, TargetRefId = 31 }, new LmsCertificationRequirementItemInput { Kind = 0, TargetRefId = 31 } }
			})).Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_cert_req_duplicate_item");
			await save.Invoking(s => s(new LmsCertificationRequirementSetInput
			{
				DepartmentCertificationTypeId = 5, IssueToNonHolders = false, RenewHolders = false, Items = { new LmsCertificationRequirementItemInput { Kind = 0, TargetRefId = 31 } }
			})).Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_cert_req_nothing_to_do");

			var saved = await world.Requirements.SaveSetAsync(Dept, new LmsCertificationRequirementSetInput
			{
				DepartmentCertificationTypeId = 5,
				Items =
				{
					new LmsCertificationRequirementItemInput { Kind = (int)LmsRequirementKinds.Course, TargetRefId = 31 },
					new LmsCertificationRequirementItemInput { Kind = (int)LmsRequirementKinds.Training, TargetRefId = 40, RequireRecordedByInstructor = true, Label = " Slow speed course " }
				}
			}, CertificationWorld.Officer);
			saved.Items.Should().HaveCount(2);
			world.Items.Single(i => i.Kind == (int)LmsRequirementKinds.Training).Label.Should().Be("Slow speed course");
			world.Audits.Should().ContainSingle(a => a.Type == AuditLogTypes.CertificationRequirementSetChanged);

			// Saving again replaces the items (no duplicates left behind).
			await world.Requirements.SaveSetAsync(Dept, new LmsCertificationRequirementSetInput
			{
				DepartmentCertificationTypeId = 5, Items = { new LmsCertificationRequirementItemInput { Kind = (int)LmsRequirementKinds.Course, TargetRefId = 32 } }
			}, CertificationWorld.Officer);
			world.Sets.Should().ContainSingle();
			world.Items.Should().ContainSingle().Which.TargetRefId.Should().Be(32);
		}

		#endregion Requirement sets

		#region Voids

		private static async Task<CertificationWorld> IssuedByCourseAsync(Action<CertificationWorld> before = null)
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator");
			before?.Invoke(world);
			world.Enrollments[77] = new LmsEnrollment { LmsEnrollmentId = 77, DepartmentId = Dept, LmsCourseId = 31, UserId = Member };
			world.CourseFacts.Add(CertificationWorld.Fact(Member, 31, DateTime.UtcNow.AddMinutes(-10), 1001));
			var result = await world.Issuance.IssueOrRenewAsync(new LmsCertificationIssueRequest
			{
				DepartmentId = Dept, UserId = Member, CertificationTypeId = 5, CompletedOnUtc = DateTime.UtcNow.AddMinutes(-10), Source = LmsCertificationIssueSources.CourseCompletion,
				SourceRefId = "lms-enrollment:77", BasisRecordIds = new[] { 1001 }, ActorUserId = Member
			});
			result.Changed.Should().BeTrue();
			return world;
		}

		[Test]
		public async Task Voiding_the_completion_behind_a_new_issue_withdraws_the_record()
		{
			var world = await IssuedByCourseAsync();
			var record = world.Records.Single();
			world.CourseFacts.Clear();

			var result = await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer);

			result.RolledBack.Should().Be(1);
			record.IsDeleted.Should().BeTrue();
			var issuance = world.Ledger.Single();
			issuance.State.Should().Be((int)LmsCertificationIssuanceStates.RolledBack);
			issuance.RolledBackByUserId.Should().Be(CertificationWorld.Officer);
			world.Notices.Last().Message.Should().Be("CertReq.Notify.Withdrawn");
			var revoked = world.WorkflowEvents.Single(e => e.Trigger == WorkflowTriggerEventType.CertificationRevokedByVoid);
			revoked.Outcome.Should().Be("Withdrawn");
			world.Audits.Should().Contain(a => a.Type == AuditLogTypes.CertificationRolledBackByVoid);

			// Another void of the same record finds nothing live: nothing more happens.
			(await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer)).RolledBack.Should().Be(0);
		}

		[Test]
		public async Task Voiding_the_completion_behind_a_renewal_restores_the_previous_expiry_start_and_status()
		{
			var today = default(DateTime);
			var world = await IssuedByCourseAsync(w =>
			{
				today = LocalToday(w);
				w.AddRecord(300, Member, 5, today.AddDays(-5), (int)PersonnelCertificationStatuses.Expired, received: today.AddMonths(-12).AddDays(-5));
			});
			var record = world.Records.Single();
			record.Status.Should().Be((int)PersonnelCertificationStatuses.Active, "the course renewed the expired record");
			world.CourseFacts.Clear();

			await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer);

			record.ExpiresOn.Should().Be(today.AddDays(-5));
			record.RecievedOn.Should().Be(today.AddMonths(-12).AddDays(-5));
			record.Status.Should().Be((int)PersonnelCertificationStatuses.Expired);
			record.IsDeleted.Should().BeFalse();
			world.Notices.Last().Message.Should().Be("CertReq.Notify.Restored");
			world.WorkflowEvents.Last().Outcome.Should().Be("Restored");
		}

		[Test]
		public async Task A_void_keeps_the_certification_when_another_valid_completion_still_covers_it()
		{
			var world = await IssuedByCourseAsync();
			var record = world.Records.Single();
			var expiry = record.ExpiresOn;
			// The member also completed the course on another enrolment, the same day.
			world.CourseFacts.Clear();
			world.CourseFacts.Add(CertificationWorld.Fact(Member, 31, DateTime.UtcNow.AddMinutes(-2), 1009));

			var result = await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer);

			result.Kept.Should().Be(1);
			result.RolledBack.Should().Be(0);
			record.IsDeleted.Should().BeFalse();
			record.ExpiresOn.Should().Be(expiry);
			world.Ledger.Single().Basis.Should().Equal(1009);
			world.Ledger.Single().IsLive.Should().BeTrue();
			world.WorkflowEvents.Should().NotContain(e => e.Trigger == WorkflowTriggerEventType.CertificationRevokedByVoid);
		}

		[Test]
		public async Task A_later_issuance_on_the_record_inherits_the_earlier_state_when_the_earlier_is_voided()
		{
			var world = await IssuedByCourseAsync();
			var record = world.Records.Single();
			var first = world.Ledger.Single();
			// A second, later renewal of the same record (another source), still standing.
			var second = await world.Issuance.RecordAsync(new LmsCertificationIssuance
			{
				DepartmentId = Dept, UserId = Member, PersonnelCertificationId = record.PersonnelCertificationId, DepartmentCertificationTypeId = 5,
				Source = (int)LmsCertificationIssueSources.RequirementSet, SourceRefId = "lms-cert-set:1", Action = (int)LmsCertificationIssueActions.Renewed,
				PreviousStatus = record.Status, PreviousExpiresOn = record.ExpiresOn, NewStatus = record.Status, NewExpiresOn = record.ExpiresOn.Value.AddYears(1),
				BasisRecordIds = "4001", IssuedOn = DateTime.UtcNow.AddMinutes(1), State = (int)LmsCertificationIssuanceStates.Live
			});
			record.ExpiresOn = second.NewExpiresOn;
			world.CourseFacts.Clear();

			var result = await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer);

			result.Unwound.Should().Be(1);
			first.State.Should().Be((int)LmsCertificationIssuanceStates.Unwound);
			record.IsDeleted.Should().BeFalse("the later issuance still stands");
			record.ExpiresOn.Should().Be(second.NewExpiresOn);
			second.Action.Should().Be((int)LmsCertificationIssueActions.Issued, "the later row now created the record: its own void withdraws it");
			second.PreviousStatus.Should().BeNull();
		}

		[Test]
		public async Task A_record_changed_by_hand_since_the_issue_is_left_for_an_officer()
		{
			var world = await IssuedByCourseAsync();
			var record = world.Records.Single();
			record.ExpiresOn = record.ExpiresOn.Value.AddMonths(6);
			world.CourseFacts.Clear();

			var result = await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer);

			result.LeftForReview.Should().Be(1);
			record.IsDeleted.Should().BeFalse();
			world.Ledger.Single().State.Should().Be((int)LmsCertificationIssuanceStates.ManualChangeKept);
			world.Audits.Should().Contain(a => a.Type == AuditLogTypes.CertificationRolledBackByVoid && a.After.Contains("ManualChangeKept"));
			world.Notices.Should().NotContain(n => n.Message == "CertReq.Notify.Withdrawn");
		}

		[Test]
		public async Task A_suspended_or_revoked_record_is_never_issued_or_renewed_over()
		{
			var world = new CertificationWorld();
			world.AddType(5, "Vehicle operator");
			var revoked = world.AddRecord(300, Member, 5, DateTime.UtcNow.Date.AddDays(-3), (int)PersonnelCertificationStatuses.Revoked);

			var result = await world.Issuance.IssueOrRenewAsync(new LmsCertificationIssueRequest
			{
				DepartmentId = Dept, UserId = Member, CertificationTypeId = 5, CompletedOnUtc = DateTime.UtcNow, Source = LmsCertificationIssueSources.CourseCompletion
			});

			result.Skipped.Should().Be("blocked");
			revoked.Status.Should().Be((int)PersonnelCertificationStatuses.Revoked);
			world.Ledger.Should().BeEmpty();
			world.Records.Should().ContainSingle();
		}

		[Test]
		public async Task Voiding_a_record_behind_the_renewal_hours_withdraws_the_next_cycle()
		{
			var world = new CertificationWorld();
			world.AddType(3, "EMT", t => { t.RenewalCreditHoursRequired = 40m; t.DefaultValidityMonths = 24; });
			var previous = world.AddRecord(1, Member, 3, DateTime.UtcNow.Date.AddDays(20));
			var next = world.AddRecord(2, Member, 3, DateTime.UtcNow.Date.AddMonths(24), received: DateTime.UtcNow.Date.AddDays(21));
			await world.Issuance.RecordAsync(new LmsCertificationIssuance
			{
				DepartmentId = Dept, UserId = Member, PersonnelCertificationId = 2, DepartmentCertificationTypeId = 3, Source = (int)LmsCertificationIssueSources.CycleAutoRenew,
				SourceRefId = LmsCertificationIssuanceRules.CycleRef(1), Action = (int)LmsCertificationIssueActions.NextCycle, NewStatus = next.Status, NewExpiresOn = next.ExpiresOn,
				BasisRecordIds = "5001,5002", State = (int)LmsCertificationIssuanceStates.Live
			});

			world.CreditHours[1] = 44m;
			(await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 5009 }, CertificationWorld.Officer)).Kept.Should().Be(1, "still 44 of 40 hours");
			next.IsDeleted.Should().BeFalse();

			world.CreditHours[1] = 36m;
			(await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 5001 }, CertificationWorld.Officer)).RolledBack.Should().Be(1);
			next.IsDeleted.Should().BeTrue();
			previous.IsDeleted.Should().BeFalse();
			world.Notices.Last().Message.Should().Be("CertReq.Notify.Withdrawn");
		}

		[Test]
		public async Task The_rollback_workflow_payload_carries_ids_dates_and_names_of_the_type_only()
		{
			var world = await IssuedByCourseAsync();
			world.CourseFacts.Clear();
			await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer);

			var evt = world.WorkflowEvents.Single(e => e.Trigger == WorkflowTriggerEventType.CertificationRevokedByVoid);
			var json = JsonConvert.SerializeObject(evt);
			json.Should().NotContain("Morgan").And.NotContain("training_completion_voided").And.NotContain("@");
			evt.CertificationTypeName.Should().Be("Vehicle operator");
			evt.UserId.Should().Be(Member);
			evt.ActorUserId.Should().Be(CertificationWorld.Officer);
			ContentWorkflowTriggers.For(evt.Trigger).Prefix.Should().Be("certification_event");
		}

		[Test]
		public async Task The_members_gdpr_export_lists_their_issuances_and_the_voids_that_took_one_back()
		{
			var world = await IssuedByCourseAsync();
			world.CourseFacts.Clear();
			await world.Issuance.OnRecordsVoidedAsync(Dept, Member, new[] { 1001 }, CertificationWorld.Officer);
			var repository = new Mock<Resgrid.Model.Repositories.ILmsCertificationIssuancesRepository>();
			repository.Setup(r => r.GetRecentForUserAsync(Dept, Member, It.IsAny<int>())).ReturnsAsync(world.Ledger.ToList());
			var section = new LmsCertificationIssuanceGdprSection(repository.Object);

			section.EntryName.Should().Be("certification_issuances.json");
			var json = JsonConvert.SerializeObject(await section.BuildAsync(Member, Dept));
			json.Should().Contain("\"State\":\"RolledBack\"").And.Contain("\"Source\":\"CourseCompletion\"").And.Contain("lms-enrollment:77");
		}

		#endregion Voids
	}
}
