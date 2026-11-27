using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using static Resgrid.Tests.Protocols.ProtocolAcknowledgmentHarness;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// P2 distribution and acknowledgment: assignment on publish and on entering the audience (R-P3), withdrawal on leaving (signed rows
	/// kept), minor carries / major re-assigns, future-effective versions acknowledgeable from publish, read-and-sign (must open first;
	/// L1 bound to the content hash; L2 when the department asks), the offline rule (R-P11), quiz-gated completion (Passed, not
	/// Complete), reminders and escalation without a billing gate, LMS parts failing closed, and the transcript record.
	/// </summary>
	[TestFixture]
	public class ProtocolAcknowledgmentServiceTests
	{
		private ProtocolAcknowledgmentHarness _h;

		[SetUp]
		public void SetUp() => _h = new ProtocolAcknowledgmentHarness();

		#region Assignment

		[Test]
		public async Task Publishing_assigns_the_active_members_of_the_audience_with_a_due_date_and_a_notice()
		{
			var (protocol, version) = _h.Published(1, 101, dueDays: 14);
			var now = DateTime.UtcNow;

			var assigned = await _h.Service.OnVersionActivatedAsync(protocol, version, now);

			assigned.Should().Be(2, "Alice and Bob are in group 5; Carol is not, Dave is not an active member");
			_h.Acks.Select(a => a.UserId).Should().BeEquivalentTo(Alice, Bob);
			_h.Acks.Should().OnlyContain(a => a.Status == (int)ProtocolAcknowledgmentStatuses.Open && a.Source == (int)ProtocolAcknowledgmentSources.Publish);
			_h.Acks.Should().OnlyContain(a => a.DueOn.Value.Date == now.AddDays(14).Date, "the protocol's own due days count from the publish");
			_h.Notifications.Verify(x => x.NotifyAssignedAsync(DepartmentId, It.Is<IReadOnlyCollection<string>>(u => u.Count == 2), It.IsAny<DispatchProtocol>(),
				It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Assigning_twice_is_idempotent()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			var again = await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);

			again.Should().Be(0);
			_h.Acks.Should().HaveCount(2);
		}

		[Test]
		public async Task A_version_that_needs_no_acknowledgment_assigns_nobody()
		{
			var (protocol, version) = _h.Published(1, 101, requiresAcknowledgment: false);
			(await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow)).Should().Be(0);
			_h.Acks.Should().BeEmpty();
		}

		[Test]
		public async Task A_member_who_enters_the_audience_is_assigned_with_a_due_date_counted_from_that_day()
		{
			var (protocol, version) = _h.Published(1, 101, dueDays: 10);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow.AddDays(-20));

			// Carol moves into group 5 three weeks after the publish (R-P3): she owes it from today, not from the publish.
			_h.MemberGroups[Carol] = new HashSet<int> { GroupFive };
			var now = DateTime.UtcNow;
			var outcome = await _h.Service.ReevaluateMemberAsync(DepartmentId, Carol, now);

			outcome.Item1.Should().Be(1);
			var row = _h.Row(101, Carol);
			row.Source.Should().Be((int)ProtocolAcknowledgmentSources.AudienceEntry);
			row.EnteredAudienceOn.Should().BeCloseTo(now, TimeSpan.FromSeconds(5));
			row.DueOn.Value.Should().BeCloseTo(now.AddDays(10), TimeSpan.FromSeconds(5));
			_h.Notifications.Verify(x => x.NotifyAssignedAsync(DepartmentId, It.Is<IReadOnlyCollection<string>>(u => u.Contains(Carol)), It.IsAny<DispatchProtocol>(),
				It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_new_member_gets_one_onboarding_bundle_notice_for_everything_in_force()
		{
			_h.Published(1, 101);
			_h.Published(2, 201);
			_h.Published(3, 301, group: null);
			_h.MemberGroups["erin"] = new HashSet<int> { GroupFive };

			var outcome = await _h.Service.ReevaluateMemberAsync(DepartmentId, "erin", DateTime.UtcNow);

			outcome.Item1.Should().Be(3, "two group-5 protocols and one for the whole department");
			_h.Notifications.Verify(x => x.NotifyBundleAsync(DepartmentId, "erin", 3, It.IsAny<CancellationToken>()), Times.Once);
			_h.Notifications.Verify(x => x.NotifyAssignedAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DispatchProtocol>(), It.IsAny<ProtocolVersion>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Leaving_the_audience_withdraws_an_open_acknowledgment_and_keeps_a_signed_one()
		{
			var (protocol, version) = _h.Published(1, 101);
			var (protocol2, version2) = _h.Published(2, 201);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			await _h.Service.AssignVersionAsync(protocol2, version2, DateTime.UtcNow);
			await SignAsync(Alice, 101);

			_h.MemberGroups[Alice] = new HashSet<int> { GroupSix };
			var outcome = await _h.Service.ReevaluateMemberAsync(DepartmentId, Alice, DateTime.UtcNow);

			outcome.Item2.Should().Be(1);
			_h.Row(101, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged, "a signed acknowledgment is evidence and is kept");
			_h.Row(201, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Withdrawn);

			// Coming back reopens the withdrawn row instead of adding a second one.
			_h.MemberGroups[Alice] = new HashSet<int> { GroupFive };
			(await _h.Service.ReevaluateMemberAsync(DepartmentId, Alice, DateTime.UtcNow)).Item1.Should().Be(1);
			_h.Acks.Where(a => a.ProtocolVersionId == 201 && a.UserId == Alice).Should().ContainSingle().Which.Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Open);
		}

		[Test]
		public async Task A_member_who_is_no_longer_active_has_every_open_acknowledgment_withdrawn()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			_h.Inactive.Add(Bob);

			var outcome = await _h.Service.ReevaluateMemberAsync(DepartmentId, Bob, DateTime.UtcNow);

			outcome.Item2.Should().Be(1);
			_h.Row(101, Bob).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Withdrawn);
		}

		[Test]
		public async Task A_minor_version_carries_a_signed_acknowledgment_forward_and_supersedes_an_open_one()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			await SignAsync(Alice, 101);

			var (p2, minor) = _h.Next(1, 102, 1, 1);
			await _h.Service.OnVersionActivatedAsync(p2, minor, DateTime.UtcNow);

			var carried = _h.Row(102, Alice);
			carried.Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
			carried.Source.Should().Be((int)ProtocolAcknowledgmentSources.CarriedForward);
			carried.CarriedFromAcknowledgmentId.Should().Be(_h.Row(101, Alice).ProtocolAcknowledgmentId);
			_h.Row(101, Bob).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Superseded, "Bob owes the version in force now");
			_h.Row(102, Bob).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Open);
		}

		[Test]
		public async Task A_major_version_asks_everyone_again()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			await SignAsync(Alice, 101);

			var (p2, major) = _h.Next(1, 102, 2, 0);
			await _h.Service.OnVersionActivatedAsync(p2, major, DateTime.UtcNow);

			_h.Row(102, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Open);
			_h.Row(101, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged, "the signature on 1.0 stays");
		}

		[Test]
		public async Task A_version_scheduled_for_later_can_be_acknowledged_from_publish_and_is_due_on_its_effective_date()
		{
			var (protocol, current) = _h.Published(1, 101);
			var effective = DateTime.UtcNow.AddDays(45);
			_h.Versions[102] = new ProtocolVersion
			{
				ProtocolVersionId = 102, DepartmentId = DepartmentId, DispatchProtocolId = 1, Major = 2, Minor = 0, Status = (int)ProtocolVersionStatuses.Approved,
				EffectiveOn = effective, ContentHtml = "<p>v2</p>", ContentHash = ProtocolContentHash.Compute("<p>v2</p>"), RequiresAcknowledgment = true
			};

			await _h.Service.AssignVersionAsync(protocol, Copy(_h.Versions[102]), DateTime.UtcNow);

			_h.Row(102, Alice).DueOn.Should().BeCloseTo(effective, TimeSpan.FromSeconds(1));
			await _h.Service.MarkOpenedAsync(DepartmentId, Alice, 102, DateTime.UtcNow);
			(await _h.Service.AcknowledgeAsync(_h.Reader(Alice), 102, null, SignatureProof.SessionOnly(Alice), false)).Outcome
				.Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
		}

		#endregion Assignment

		#region Read-and-sign

		private async Task<ProtocolAcknowledgeResult> SignAsync(string userId, int versionId)
		{
			await _h.Service.MarkOpenedAsync(DepartmentId, userId, versionId, DateTime.UtcNow);
			return await _h.Service.AcknowledgeAsync(_h.Reader(userId), versionId, _h.Versions[versionId].ContentHash, SignatureProof.SessionOnly(userId), false);
		}

		[Test]
		public async Task The_web_requires_the_member_to_open_the_version_before_signing()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);

			var unopened = await _h.Service.AcknowledgeAsync(_h.Reader(Alice), 101, version.ContentHash, SignatureProof.SessionOnly(Alice), false);
			unopened.Outcome.Should().Be(ProtocolAcknowledgeOutcomes.NotOpened);
			_h.Signatures.Should().BeEmpty();

			var signed = await SignAsync(Alice, 101);
			signed.Outcome.Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
		}

		[Test]
		public async Task Read_and_sign_is_an_L1_signature_bound_to_the_version_hash_and_writes_the_transcript()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);

			await SignAsync(Alice, 101);

			var signature = _h.Signatures.Should().ContainSingle().Subject;
			signature.SubjectKind.Should().Be((int)SignatureSubjectKinds.ProtocolAcknowledgment);
			signature.SubjectId.Should().Be("101");
			signature.ArtifactSha256.Should().Be(version.ContentHash);
			signature.Level.Should().Be((int)SignatureLevels.Attest);
			var row = _h.Row(101, Alice);
			row.Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
			row.SignatureRecordId.Should().Be(signature.SignatureRecordId);
			var write = _h.LearningWrites.Should().ContainSingle().Subject;
			write.Kind.Should().Be(LearningRecordSourceKinds.ProtocolAcknowledgment);
			write.SourceRefId.Should().Be(row.ProtocolAcknowledgmentId.ToString());
			write.Drafts.Should().ContainSingle(d => d.UserId == Alice);
		}

		[Test]
		public async Task A_department_that_asks_for_a_step_up_refuses_a_session_only_signature()
		{
			_h.Settings = new ProtocolLibrarySettings { ReadAndSignRequiresStepUp = true };
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);

			(await SignAsync(Alice, 101)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.StepUpRequired);

			var proof = SignatureProof.SessionOnly(Alice);
			proof.StepUpVerifiedOnUtc = DateTime.UtcNow;
			(await _h.Service.AcknowledgeAsync(_h.Reader(Alice), 101, version.ContentHash, proof, false)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
			_h.Signatures.Single().Level.Should().Be((int)SignatureLevels.StepUp);
		}

		[Test]
		public async Task Signing_twice_changes_nothing()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			await SignAsync(Alice, 101);

			(await SignAsync(Alice, 101)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.AlreadyAcknowledged);
			_h.Signatures.Should().HaveCount(1);
		}

		[Test]
		public async Task An_offline_acknowledgment_is_accepted_only_for_the_version_still_in_force()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);

			// Bob read 1.0 offline; 2.0 came into force before his phone synced.
			var (p2, v2) = _h.Next(1, 102, 2, 0);
			await _h.Service.OnVersionActivatedAsync(p2, v2, DateTime.UtcNow);
			var stale = await _h.Service.AcknowledgeAsync(_h.Reader(Bob), 101, version.ContentHash, SignatureProof.SessionOnly(Bob), true);
			stale.Outcome.Should().Be(ProtocolAcknowledgeOutcomes.ReadNewerVersion);
			stale.CurrentVersionId.Should().Be(102);

			// Read the version in force, but the hash he sends is not its content: refused.
			var wrong = await _h.Service.AcknowledgeAsync(_h.Reader(Bob), 102, version.ContentHash, SignatureProof.SessionOnly(Bob), true);
			wrong.Outcome.Should().Be(ProtocolAcknowledgeOutcomes.ContentChanged);

			var ok = await _h.Service.AcknowledgeAsync(_h.Reader(Bob), 102, v2.ContentHash, SignatureProof.SessionOnly(Bob), true);
			ok.Outcome.Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
			_h.Row(102, Bob).ViaOffline.Should().BeTrue();
		}

		[Test]
		public void The_offline_rule_accepts_the_version_in_force_and_a_scheduled_one_and_nothing_else()
		{
			var protocol = new DispatchProtocol { DispatchProtocolId = 1, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 2 };
			ProtocolVersion V(int id, ProtocolVersionStatuses status, int major = 1) => new ProtocolVersion
			{
				ProtocolVersionId = id, DispatchProtocolId = 1, Status = (int)status, Major = major, ContentHash = "h" + id, EffectiveOn = DateTime.UtcNow.AddDays(3)
			};

			ProtocolAcknowledgmentRules.CheckOffline(protocol, V(2, ProtocolVersionStatuses.Published), "h2").Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
			ProtocolAcknowledgmentRules.CheckOffline(protocol, V(1, ProtocolVersionStatuses.Superseded), "h1").Should().Be(ProtocolAcknowledgeOutcomes.ReadNewerVersion);
			ProtocolAcknowledgmentRules.CheckOffline(protocol, V(3, ProtocolVersionStatuses.Approved, 2), "h3").Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
			ProtocolAcknowledgmentRules.CheckOffline(protocol, V(4, ProtocolVersionStatuses.InReview), "h4").Should().Be(ProtocolAcknowledgeOutcomes.ReadNewerVersion);
			ProtocolAcknowledgmentRules.CheckOffline(protocol, V(2, ProtocolVersionStatuses.Published), "other").Should().Be(ProtocolAcknowledgeOutcomes.ContentChanged);
			protocol.IsDeleted = true;
			ProtocolAcknowledgmentRules.CheckOffline(protocol, V(2, ProtocolVersionStatuses.Published), "h2").Should().Be(ProtocolAcknowledgeOutcomes.ReadNewerVersion);
		}

		[Test]
		public async Task A_member_who_owes_nothing_cannot_sign()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			(await _h.Service.AcknowledgeAsync(_h.Reader(Carol), 101, version.ContentHash, SignatureProof.SessionOnly(Carol), true)).Outcome
				.Should().Be(ProtocolAcknowledgeOutcomes.NotAssigned);
			(await _h.Service.AcknowledgeAsync(_h.Reader(Alice), 101, version.ContentHash, SignatureProof.SessionOnly(Bob), true)).Outcome
				.Should().Be(ProtocolAcknowledgeOutcomes.InvalidRequest, "a proof built for someone else is no proof");
		}

		#endregion Read-and-sign

		#region Quiz-gated acknowledgment

		[Test]
		public async Task A_quiz_gated_acknowledgment_completes_only_when_the_training_is_passed_not_merely_complete()
		{
			var (protocol, version) = _h.Published(1, 101);
			_h.Versions[101].QuizTrainingId = 77;
			await _h.Service.AssignVersionAsync(protocol, Copy(_h.Versions[101]), DateTime.UtcNow);
			_h.TrainingAssignments.Verify(x => x.AssignAsync(It.Is<Training>(t => t.TrainingId == 77), It.Is<IEnumerable<string>>(u => u.Count() == 2), null,
				TrainingAssignmentSources.Manual, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once, "publishing assigns the quiz training");

			// Complete without a pass (a quiz attempt that failed and was then marked done) is not enough.
			_h.TrainingUsers.Add(new TrainingUser { TrainingId = 77, UserId = Alice, Complete = true, Passed = false });
			(await SignAsync(Alice, 101)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.AwaitingQuiz);
			_h.Row(101, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.AwaitingQuiz);
			_h.LearningWrites.Should().BeEmpty();

			(await _h.Service.CompletePassedQuizzesAsync(DateTime.UtcNow)).Should().Be(0);
			_h.Row(101, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.AwaitingQuiz);

			_h.TrainingUsers.Single().Passed = true;
			(await _h.Service.CompletePassedQuizzesAsync(DateTime.UtcNow)).Should().Be(1);
			_h.Row(101, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
			_h.Row(101, Alice).CompletedOn.Should().NotBeNull();
			_h.LearningWrites.Should().ContainSingle();
		}

		[Test]
		public async Task A_quiz_already_passed_completes_at_signing()
		{
			var (protocol, version) = _h.Published(1, 101);
			_h.Versions[101].QuizTrainingId = 77;
			await _h.Service.AssignVersionAsync(protocol, Copy(_h.Versions[101]), DateTime.UtcNow);
			_h.TrainingUsers.Add(new TrainingUser { TrainingId = 77, UserId = Bob, Complete = true, Passed = true });

			(await SignAsync(Bob, 101)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
		}

		#endregion Quiz-gated acknowledgment

		#region Worker 86

		[Test]
		public async Task Reminders_go_at_the_due_date_and_after_and_escalate_to_the_supervisor_without_any_billing_gate()
		{
			var (protocol, version) = _h.Published(1, 101, dueDays: 5);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow.AddDays(-20));

			// Due 15 days ago: the escalation stage for both members (one notice each, one escalation each).
			var sent = await _h.Service.SendRemindersAsync(DateTime.UtcNow);

			sent.Should().Be(4);
			_h.Acks.Should().OnlyContain(a => a.ReminderStage == (int)ProtocolAcknowledgmentReminderStages.Escalated && a.EscalatedOn.HasValue);
			_h.Notifications.Verify(x => x.NotifyEscalationAsync(DepartmentId, It.Is<IReadOnlyCollection<string>>(u => u.Single() == Officer), It.IsAny<string>(),
				It.IsAny<DispatchProtocol>(), It.IsAny<ProtocolVersion>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

			// A second run sends nothing (the stage was claimed), and no run ever asks the LMS or billing gate.
			(await _h.Service.SendRemindersAsync(DateTime.UtcNow)).Should().Be(0);
			_h.Lms.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_signed_acknowledgment_is_never_reminded()
		{
			var (protocol, version) = _h.Published(1, 101, dueDays: 1);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow.AddDays(-3));
			await SignAsync(Alice, 101);

			await _h.Service.SendRemindersAsync(DateTime.UtcNow);

			_h.Row(101, Alice).ReminderStage.Should().Be(0);
			_h.Row(101, Bob).ReminderStage.Should().BeGreaterThan(0);
		}

		[Test]
		public void The_reminder_stage_follows_the_due_date()
		{
			var due = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
			ProtocolAcknowledgmentRules.DueStage(due, due.AddMinutes(-1)).Should().Be(ProtocolAcknowledgmentReminderStages.None);
			ProtocolAcknowledgmentRules.DueStage(due, due).Should().Be(ProtocolAcknowledgmentReminderStages.Due);
			ProtocolAcknowledgmentRules.DueStage(due, due.AddDays(ProtocolAcknowledgmentRules.OverdueReminderDays)).Should().Be(ProtocolAcknowledgmentReminderStages.Overdue);
			ProtocolAcknowledgmentRules.DueStage(due, due.AddDays(ProtocolAcknowledgmentRules.EscalationDays)).Should().Be(ProtocolAcknowledgmentReminderStages.Escalated);
			ProtocolAcknowledgmentRules.DueStage(null, due).Should().Be(ProtocolAcknowledgmentReminderStages.None);
		}

		#endregion Worker 86

		#region Compliance and LMS

		[Test]
		public async Task The_compliance_grid_lists_each_person_with_their_state_and_filters_by_group()
		{
			var (protocol, version) = _h.Published(1, 101, group: null, dueDays: 1);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow.AddDays(-3));
			await SignAsync(Alice, 101);

			var grid = await _h.Service.GetComplianceAsync(_h.Reader(Officer), 1, null, null, DateTime.UtcNow);
			grid.Rows.Select(r => (r.UserId, r.StateKey)).Should().BeEquivalentTo(new[] { (Alice, "acknowledged"), (Bob, "overdue"), (Carol, "overdue") });

			var groupFive = await _h.Service.GetComplianceAsync(_h.Reader(Officer), 1, null, GroupFive, DateTime.UtcNow);
			groupFive.Rows.Select(r => r.UserId).Should().BeEquivalentTo(Alice, Bob);

			var overview = await _h.Service.GetComplianceAsync(_h.Reader(Officer), null, null, null, DateTime.UtcNow);
			var line = overview.Summaries.Should().ContainSingle().Subject;
			line.Assigned.Should().Be(3);
			line.Acknowledged.Should().Be(1);
			line.Percent.Should().Be(33, "honest progress rounds down");

			var csv = _h.Service.ToCsv(grid);
			csv.Should().Contain("acknowledged").And.Contain("overdue");

			(await _h.Service.GetComplianceAsync(_h.Reader(Carol), 1, null, null, DateTime.UtcNow)).Rows.Should().BeEmpty("a plain member does not see the grid");
		}

		[Test]
		public void Csv_cells_cannot_start_a_formula()
		{
			Resgrid.Services.ProtocolAcknowledgmentService.Csv("=HYPERLINK(\"x\")").Should().StartWith("\"'=");
			Resgrid.Services.ProtocolAcknowledgmentService.Csv("plain \"quoted\"").Should().Be("\"plain \"\"quoted\"\"\"");
		}

		[Test]
		public async Task Standards_tags_and_the_proof_of_compliance_report_fail_closed_without_the_lms()
		{
			_h.Published(1, 101);
			_h.LmsActive = false;

			(await _h.Service.GetStandardsReportAsync(_h.Reader(Officer), DateTime.UtcNow)).Should().BeNull();
			(await _h.Service.SaveSettingsAsync(_h.Reader(Officer), 1, 20, 3, null, "CFAI, CALEA")).Succeeded.Should().BeTrue();
			_h.Protocols[1].StandardsTags.Should().BeNull("the free tier keeps due days and order but never stores standards tags");
			_h.Protocols[1].AckDueDays.Should().Be(20);

			_h.LmsActive = true;
			(await _h.Service.SaveSettingsAsync(_h.Reader(Officer), 1, 20, 3, null, "CFAI, CALEA, cfai")).Succeeded.Should().BeTrue();
			_h.Protocols[1].StandardsTags.Should().Be("CFAI, CALEA");
			var report = await _h.Service.GetStandardsReportAsync(_h.Reader(Officer), DateTime.UtcNow);
			report.Sections.Select(s => s.Standard).Should().BeEquivalentTo("CFAI", "CALEA");
		}

		[Test]
		public async Task A_bank_quiz_gates_the_acknowledgment_only_while_the_lms_is_active_and_completes_it_when_passed()
		{
			// Round 5F: the protocol asks for a question-bank quiz (LMS). Signing waits for it; passing completes the acknowledgment.
			var (_, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(_h.Protocols[1], version, DateTime.UtcNow);
			var bank = new ProtocolBankQuizState { Configured = true, QuestionCount = 5, PassMark = 80 };
			_h.Learning.Setup(x => x.GetBankQuizStateAsync(DepartmentId, 1, 101, It.IsAny<string>())).ReturnsAsync(() => bank);
			_h.LmsActive = true;

			await _h.Service.MarkOpenedAsync(DepartmentId, Alice, 101, DateTime.UtcNow);
			var signed = await _h.Service.AcknowledgeAsync(_h.Reader(Alice), 101, version.ContentHash, SignatureProof.SessionOnly(Alice, null, null), false);
			signed.Outcome.Should().Be(ProtocolAcknowledgeOutcomes.AwaitingQuiz);
			_h.LearningWrites.Should().BeEmpty("nothing is complete until the quiz is passed");
			(await _h.Service.CompleteIfQuizPassedAsync(DepartmentId, 101, Alice, DateTime.UtcNow)).Should().BeFalse("not passed yet");

			bank = new ProtocolBankQuizState { Configured = true, Passed = true, QuestionCount = 5, PassMark = 80 };
			(await _h.Service.CompleteIfQuizPassedAsync(DepartmentId, 101, Alice, DateTime.UtcNow)).Should().BeTrue();
			_h.Row(101, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
			_h.LearningWrites.Should().ContainSingle();
			(await _h.Service.CompleteIfQuizPassedAsync(DepartmentId, 101, Alice, DateTime.UtcNow)).Should().BeFalse("already complete");

			// Without the add-on the bank quiz is not asked (fails closed: the free read-and-sign completes on its own).
			_h.LmsActive = false;
			bank = new ProtocolBankQuizState { Configured = true, QuestionCount = 5, PassMark = 80 };
			await _h.Service.MarkOpenedAsync(DepartmentId, Bob, 101, DateTime.UtcNow);
			(await _h.Service.AcknowledgeAsync(_h.Reader(Bob), 101, version.ContentHash, SignatureProof.SessionOnly(Bob, null, null), false))
				.Outcome.Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
		}

		[Test]
		public async Task Worker_86_completes_a_waiting_bank_quiz_acknowledgment_without_any_billing_gate()
		{
			var (_, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(_h.Protocols[1], version, DateTime.UtcNow);
			var passed = false;
			_h.Learning.Setup(x => x.GetBankQuizStateAsync(DepartmentId, 1, 101, It.IsAny<string>()))
				.ReturnsAsync(() => new ProtocolBankQuizState { Configured = true, Passed = passed });
			_h.LmsActive = true;
			await _h.Service.MarkOpenedAsync(DepartmentId, Alice, 101, DateTime.UtcNow);
			await _h.Service.AcknowledgeAsync(_h.Reader(Alice), 101, version.ContentHash, SignatureProof.SessionOnly(Alice, null, null), false);
			_h.Lms.Invocations.Clear();

			(await _h.Service.CompletePassedQuizzesAsync(DateTime.UtcNow)).Should().Be(0);
			passed = true;
			(await _h.Service.CompletePassedQuizzesAsync(DateTime.UtcNow)).Should().Be(1);
			_h.Row(101, Alice).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
			_h.Lms.Invocations.Should().BeEmpty("the sweep reads the database only (R-L9)");
		}

		[Test]
		public async Task Only_a_quiz_training_of_the_department_can_gate_an_acknowledgment()
		{
			_h.Published(1, 101);
			_h.Trainings.Setup(x => x.GetTrainingByIdAsync(88)).ReturnsAsync(new Training { TrainingId = 88, DepartmentId = 99, QuestionCount = 2 });
			_h.Trainings.Setup(x => x.GetTrainingByIdAsync(89)).ReturnsAsync(new Training { TrainingId = 89, DepartmentId = DepartmentId, QuestionCount = 0 });

			(await _h.Service.SaveSettingsAsync(_h.Reader(Officer), 1, null, 0, 88, null)).Error.Should().Be(ProtocolLifecycleErrors.InvalidInput);
			(await _h.Service.SaveSettingsAsync(_h.Reader(Officer), 1, null, 0, 89, null)).Error.Should().Be(ProtocolLifecycleErrors.InvalidInput);
			(await _h.Service.SaveSettingsAsync(_h.Reader(Carol), 1, null, 0, null, null)).Error.Should().Be(ProtocolLifecycleErrors.NotAuthorized);
		}

		#endregion Compliance and LMS

		#region Bulletins

		[Test]
		public async Task A_bulletin_is_in_force_at_once_must_read_and_awaits_its_countersign()
		{
			_h.Library.Setup(x => x.CreateAsync(It.IsAny<ProtocolCreateRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolCreateRequest request, CancellationToken _) =>
				{
					var protocol = request.Protocol;
					protocol.DispatchProtocolId = 9;
					protocol.DepartmentId = request.DepartmentId;
					protocol.Status = (int)ProtocolStatuses.Draft;
					_h.Protocols[9] = Copy(protocol);
					var version = new ProtocolVersion
					{
						ProtocolVersionId = 901, DepartmentId = request.DepartmentId, DispatchProtocolId = 9, Status = (int)ProtocolVersionStatuses.Draft,
						ContentHtml = request.ContentHtml, ContentHash = ProtocolContentHash.Compute(request.ContentHtml), RequiresAcknowledgment = request.RequiresAcknowledgment ?? false
					};
					_h.Versions[901] = Copy(version);
					foreach (var rule in request.Audience)
						_h.Audiences.Add(new ProtocolAudience { DepartmentId = request.DepartmentId, DispatchProtocolId = 9, Kind = rule.Kind, RefId = rule.RefId });
					return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
				});
			var approver = _h.Reader(Officer);
			approver.CanApprove = true;

			var result = await _h.Service.IssueBulletinAsync(approver, new ProtocolBulletinRequest
			{
				Title = "Hydrant 42 out of service",
				ContentHtml = "Do not use hydrant 42.\nUse 43 instead.",
				Audience = new List<ContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.Group, GroupFive.ToString()) },
				ExpiresOnUtc = DateTime.UtcNow.AddDays(3),
				MustRead = true
			});

			result.Succeeded.Should().BeTrue();
			_h.Protocols[9].Kind.Should().Be((int)ProtocolKinds.Bulletin);
			_h.Protocols[9].Status.Should().Be((int)ProtocolStatuses.Published);
			_h.Versions[901].Status.Should().Be((int)ProtocolVersionStatuses.Published);
			_h.Versions[901].ExpiresOn.Should().NotBeNull("worker 84 retires it at its expiry");
			_h.Versions[901].ContentHtml.Should().Contain("<p>Do not use hydrant 42.</p>");
			_h.Approvals.Verify(x => x.InsertAsync(It.Is<ProtocolVersionApproval>(a => a.ProtocolVersionId == 901 && a.Decision == (int)ProtocolApprovalDecisions.Pending),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once, "the approval comes afterwards");
			_h.Acks.Select(a => a.UserId).Should().BeEquivalentTo(Alice, Bob);
			_h.Acks.Should().OnlyContain(a => a.Source == (int)ProtocolAcknowledgmentSources.Bulletin);

			(await _h.Service.GetMustReadAsync(DepartmentId, Alice, DateTime.UtcNow)).Should().ContainSingle(i => i.ProtocolId == 9);
			(await _h.Service.GetMustReadAsync(DepartmentId, Carol, DateTime.UtcNow)).Should().BeEmpty();

			// Acknowledged: it leaves the home card.
			await SignAsync(Alice, 901);
			(await _h.Service.GetMustReadAsync(DepartmentId, Alice, DateTime.UtcNow)).Should().BeEmpty();
			_h.LearningWrites.Should().BeEmpty("a bulletin is a notice, not learning");
		}

		[Test]
		public async Task A_bulletin_needs_the_publish_right_and_a_near_expiry()
		{
			var member = _h.Reader(Alice);
			(await _h.Service.IssueBulletinAsync(member, new ProtocolBulletinRequest { Title = "x", ContentHtml = "y", ExpiresOnUtc = DateTime.UtcNow.AddDays(1) }))
				.Error.Should().Be(ProtocolLifecycleErrors.NotAuthorized);

			var approver = _h.Reader(Officer);
			(await _h.Service.IssueBulletinAsync(approver, new ProtocolBulletinRequest { Title = "x", ContentHtml = "y", ExpiresOnUtc = DateTime.UtcNow.AddDays(200) }))
				.Error.Should().Be(ProtocolLifecycleErrors.InvalidInput);
			(await _h.Service.IssueBulletinAsync(approver, new ProtocolBulletinRequest { Title = "x", ContentHtml = "y", ExpiresOnUtc = DateTime.UtcNow.AddMinutes(-5) }))
				.Error.Should().Be(ProtocolLifecycleErrors.InvalidInput);
		}

		[Test]
		public async Task An_expired_or_withdrawn_bulletin_leaves_the_home_card_and_withdraws_open_acknowledgments()
		{
			var (protocol, version) = _h.Published(9, 901, kind: ProtocolKinds.Bulletin);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			(await _h.Service.GetMustReadAsync(DepartmentId, Bob, DateTime.UtcNow)).Should().ContainSingle();

			// Worker 84 retires it at its expiry; the library's retire hook withdraws the open rows.
			_h.Protocols[9].Status = (int)ProtocolStatuses.Retired;
			_h.Versions[901].Status = (int)ProtocolVersionStatuses.Retired;
			(await _h.Service.OnProtocolRetiredAsync(Copy(_h.Protocols[9]), DateTime.UtcNow)).Should().Be(2);

			(await _h.Service.GetMustReadAsync(DepartmentId, Bob, DateTime.UtcNow)).Should().BeEmpty();
			_h.Row(901, Bob).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Withdrawn);
		}

		#endregion Bulletins

		#region Workflow events (round 7C, triggers 235 and 251-253)

		[Test]
		public async Task Assignment_raises_one_assigned_event_per_new_row_and_a_repeat_raises_none()
		{
			var (protocol, version) = _h.Published(1, 101, dueDays: 14);

			await _h.Service.OnVersionActivatedAsync(protocol, version, DateTime.UtcNow);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);

			_h.AcknowledgmentEvents.Should().HaveCount(2, "Alice and Bob were assigned once each; the second pass inserted nothing");
			_h.AcknowledgmentEvents.Select(e => e.UserId).Should().BeEquivalentTo(Alice, Bob);
			_h.AcknowledgmentEvents.Should().OnlyContain(e => e.Trigger == WorkflowTriggerEventType.ProtocolAcknowledgmentAssigned && e.ProtocolId == 1 &&
				e.ProtocolVersionId == 101 && e.ProtocolCode == "P1" && e.VersionLabel == "1.0" && e.AssignmentSource == "Publish" && e.DueOn.HasValue && e.AcknowledgmentId > 0);
		}

		[Test]
		public async Task Signing_raises_completed_once_and_a_refused_or_repeated_signature_raises_nothing()
		{
			var (protocol, version) = _h.Published(1, 101);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow);
			_h.AcknowledgmentEvents.Clear();

			// Not opened yet: refused, no event.
			(await _h.Service.AcknowledgeAsync(_h.Reader(Alice), 101, version.ContentHash, SignatureProof.SessionOnly(Alice), false)).Outcome
				.Should().Be(ProtocolAcknowledgeOutcomes.NotOpened);
			_h.AcknowledgmentEvents.Should().BeEmpty();

			(await SignAsync(Alice, 101)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.Acknowledged);
			(await SignAsync(Alice, 101)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.AlreadyAcknowledged);

			_h.AcknowledgmentEvents.Should().ContainSingle();
			var completed = _h.AcknowledgmentEvents.Single();
			completed.Trigger.Should().Be(WorkflowTriggerEventType.ProtocolAcknowledgmentCompleted);
			completed.UserId.Should().Be(Alice);
			completed.CompletedOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_quiz_gated_signature_completes_only_when_the_quiz_is_passed_and_raises_completed_then()
		{
			var (protocol, version) = _h.Published(1, 101);
			_h.Versions[101].QuizTrainingId = 77;
			version.QuizTrainingId = 77;
			await _h.Service.AssignVersionAsync(protocol, Copy(_h.Versions[101]), DateTime.UtcNow);
			_h.AcknowledgmentEvents.Clear();

			(await SignAsync(Bob, 101)).Outcome.Should().Be(ProtocolAcknowledgeOutcomes.AwaitingQuiz);
			_h.AcknowledgmentEvents.Should().BeEmpty("signed but not complete");

			_h.TrainingUsers.Add(new TrainingUser { TrainingId = 77, UserId = Bob, Complete = true, Passed = true });
			(await _h.Service.CompleteIfQuizPassedAsync(DepartmentId, 101, Bob, DateTime.UtcNow)).Should().BeTrue();
			(await _h.Service.CompleteIfQuizPassedAsync(DepartmentId, 101, Bob, DateTime.UtcNow)).Should().BeFalse();

			_h.AcknowledgmentEvents.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ProtocolAcknowledgmentCompleted && e.UserId == Bob && e.QuizRequired);
		}

		[Test]
		public async Task Overdue_is_raised_once_per_row_at_the_first_claimed_stage_past_due_and_never_for_a_signed_one()
		{
			var (protocol, version) = _h.Published(1, 101, dueDays: 5);
			await _h.Service.AssignVersionAsync(protocol, version, DateTime.UtcNow.AddDays(-20));
			await SignAsync(Alice, 101);
			_h.AcknowledgmentEvents.Clear();

			await _h.Service.SendRemindersAsync(DateTime.UtcNow);
			await _h.Service.SendRemindersAsync(DateTime.UtcNow);

			_h.AcknowledgmentEvents.Should().ContainSingle();
			var overdue = _h.AcknowledgmentEvents.Single();
			overdue.Trigger.Should().Be(WorkflowTriggerEventType.ProtocolAcknowledgmentOverdue);
			overdue.UserId.Should().Be(Bob);
			overdue.ReminderStage.Should().Be("Escalated", "first seen 15 days past due");
			overdue.DaysOverdue.Should().BeInRange(14, 15);
		}

		[Test]
		public async Task Issuing_a_bulletin_raises_bulletin_issued_once_and_a_refused_issue_raises_nothing()
		{
			_h.Library.Setup(x => x.CreateAsync(It.IsAny<ProtocolCreateRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolCreateRequest request, CancellationToken _) =>
				{
					var protocol = request.Protocol;
					protocol.DispatchProtocolId = 9;
					protocol.DepartmentId = request.DepartmentId;
					_h.Protocols[9] = Copy(protocol);
					var version = new ProtocolVersion
					{
						ProtocolVersionId = 901, DepartmentId = request.DepartmentId, DispatchProtocolId = 9, Status = (int)ProtocolVersionStatuses.Draft,
						ContentHtml = request.ContentHtml, ContentHash = ProtocolContentHash.Compute(request.ContentHtml), RequiresAcknowledgment = request.RequiresAcknowledgment ?? false
					};
					_h.Versions[901] = Copy(version);
					return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
				});
			var approver = _h.Reader(Officer);
			approver.CanApprove = true;

			(await _h.Service.IssueBulletinAsync(_h.Reader(Alice), new ProtocolBulletinRequest { Title = "x", ContentHtml = "y", ExpiresOnUtc = DateTime.UtcNow.AddDays(1) }))
				.Error.Should().Be(ProtocolLifecycleErrors.NotAuthorized);
			_h.ProtocolEvents.Should().BeEmpty();

			(await _h.Service.IssueBulletinAsync(approver, new ProtocolBulletinRequest
			{
				Title = "Hydrant 42 out of service", ContentHtml = "Do not use hydrant 42.", ExpiresOnUtc = DateTime.UtcNow.AddDays(3), MustRead = false
			})).Succeeded.Should().BeTrue();

			_h.ProtocolEvents.Should().ContainSingle();
			var issued = _h.ProtocolEvents.Single();
			issued.Trigger.Should().Be(WorkflowTriggerEventType.ProtocolBulletinIssued);
			issued.ProtocolId.Should().Be(9);
			issued.ProtocolKind.Should().Be("Bulletin");
			issued.ProtocolName.Should().Be("Hydrant 42 out of service");
			issued.ExpiresOn.Should().NotBeNull();
			issued.UserId.Should().Be(Officer);
		}

		#endregion Workflow events (round 7C, triggers 235 and 251-253)
	}
}
