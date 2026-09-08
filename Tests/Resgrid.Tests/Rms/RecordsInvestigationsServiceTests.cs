using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-5 investigations (RMS plan section 4.4): case-level authorization on top of RecordRestricted_View, audited reads, append-only custody, separated findings approval, closure locks.</summary>
	[TestFixture]
	public class RecordsInvestigationsServiceTests
	{
		private RmsPreventionHarness _h;
		private const string Investigator = "inv-1";
		private const string Reviewer = "rev-1";

		[SetUp]
		public void SetUp()
		{
			_h = new RmsPreventionHarness();
			_h.RestrictedViewers.Add(Investigator);
			_h.RestrictedViewers.Add(Reviewer);
		}

		[Test]
		public async Task Opening_a_case_makes_the_opener_lead_and_department_admins_are_not_members_by_default()
		{
			var investigation = await _h.InvestigationsService.OpenAsync(Dept, Admin, "Warehouse fire", null, 42, "Fire in loading dock, origin unknown.");
			investigation.CaseNumber.Should().Be($"INV-{DateTime.UtcNow.Year}-0001");
			investigation.IncidentSummary.Should().Be("Fire in loading dock, origin unknown.");
			_h.Protection.Writes.Should().Contain("investigation case");
			(await _h.InvestigationsService.ListMyCasesAsync(Dept, Admin, false)).Should().ContainSingle();

			Func<Task> otherAdmin = () => _h.InvestigationsService.GetAsync(Dept, Admin2, investigation.RmsInvestigationCaseId);
			await otherAdmin.Should().ThrowAsync<UnauthorizedAccessException>("department administrator status is not a bypass");
			_h.Audits.Rows.Should().Contain(a => a.Action == (int)RmsAccessAuditAction.Denied && a.CorrelationId == investigation.RmsInvestigationCaseId && a.ActorUserId == Admin2 && !a.Successful);
			(await _h.InvestigationsService.ListMyCasesAsync(Dept, Admin2, true)).Should().BeEmpty();

			Func<Task> noRestricted = () => _h.InvestigationsService.OpenAsync(Dept, Member, "x", null, null, null);
			await noRestricted.Should().ThrowAsync<UnauthorizedAccessException>();

			var aggregate = await _h.InvestigationsService.GetAsync(Dept, Admin, investigation.RmsInvestigationCaseId, "10.0.0.5");
			aggregate.CallerRole.Should().Be(RmsInvestigationRole.Lead);
			_h.Audits.Rows.Should().Contain(a => a.Action == (int)RmsAccessAuditAction.Read && a.Purpose == RecordsInvestigationsService.AuditPurpose && a.CorrelationId == investigation.RmsInvestigationCaseId && a.IpAddress == "10.0.0.5" && a.RecordId == null);
		}

		[Test]
		public async Task Members_notes_evidence_and_custody_follow_the_case_rules()
		{
			var investigation = await _h.InvestigationsService.OpenAsync(Dept, Admin, "Warehouse fire", null, null, null);
			var caseId = investigation.RmsInvestigationCaseId;
			await _h.InvestigationsService.AddMemberAsync(Dept, Admin, caseId, Investigator, RmsInvestigationRole.Investigator);
			await _h.InvestigationsService.AddMemberAsync(Dept, Admin, caseId, Reviewer, RmsInvestigationRole.Reviewer);
			Func<Task> outsider = () => _h.InvestigationsService.AddMemberAsync(Dept, Admin, caseId, Outsider, RmsInvestigationRole.ReadOnly);
			await outsider.Should().ThrowAsync<ArgumentException>();
			Func<Task> nonLead = () => _h.InvestigationsService.AddMemberAsync(Dept, Investigator, caseId, Member, RmsInvestigationRole.ReadOnly);
			await nonLead.Should().ThrowAsync<UnauthorizedAccessException>();

			var note = await _h.InvestigationsService.AddNoteAsync(Dept, Investigator, caseId, RmsInvestigationNoteKind.Interview, DateTime.UtcNow, "Night watchman", "Saw smoke at 02:10.");
			note.Body.Should().Be("Saw smoke at 02:10.");
			Func<Task> reviewerNote = () => _h.InvestigationsService.AddNoteAsync(Dept, Reviewer, caseId, RmsInvestigationNoteKind.SceneNote, DateTime.UtcNow, null, "x");
			await reviewerNote.Should().ThrowAsync<UnauthorizedAccessException>();
			investigation.State.Should().Be((int)RmsInvestigationCaseState.Active, "the first work item activates the case");

			var evidence = await _h.InvestigationsService.AddEvidenceAsync(Dept, Investigator, caseId, new RmsInvestigationEvidence { Kind = (int)RmsInvestigationEvidenceKind.Physical, Description = "Extension cord, melted", CollectedFrom = "Dock bay 2" });
			evidence.EvidenceNumber.Should().Be($"EV-{DateTime.UtcNow.Year}-0001");
			evidence.CurrentCustodianUserId.Should().Be(Investigator);
			var chain = await _h.InvestigationsService.GetCustodyChainAsync(Dept, Reviewer, evidence.RmsInvestigationEvidenceId);
			chain.Should().ContainSingle(c => c.Sequence == 1 && c.Reason == "Collected");

			var transfer = await _h.InvestigationsService.TransferCustodyAsync(Dept, Investigator, evidence.RmsInvestigationEvidenceId, null, "State fire marshal lab", "Arc mapping analysis", RmsEvidenceState.Transferred);
			transfer.Sequence.Should().Be(2);
			transfer.FromUserId.Should().Be(Investigator);
			transfer.ToExternal.Should().Be("State fire marshal lab");
			evidence.State.Should().Be((int)RmsEvidenceState.Transferred);
			evidence.CurrentCustodianExternal.Should().Be("State fire marshal lab");
			(await _h.InvestigationsService.GetCustodyChainAsync(Dept, Admin, evidence.RmsInvestigationEvidenceId)).Should().HaveCount(2);
			Func<Task> noReason = () => _h.InvestigationsService.TransferCustodyAsync(Dept, Investigator, evidence.RmsInvestigationEvidenceId, Admin, null, "", RmsEvidenceState.InStorage);
			await noReason.Should().ThrowAsync<ArgumentException>();

			var referral = await _h.InvestigationsService.AddReferralAsync(Dept, Admin, caseId, "County Sheriff", "Possible incendiary", "SO-26-118");
			await _h.InvestigationsService.UpdateReferralStateAsync(Dept, Investigator, referral.RmsInvestigationReferralId, RmsReferralState.Acknowledged);
			referral.State.Should().Be((int)RmsReferralState.Acknowledged);

			var aggregate = await _h.InvestigationsService.GetAsync(Dept, Reviewer, caseId);
			aggregate.CallerRole.Should().Be(RmsInvestigationRole.Reviewer);
			aggregate.Notes.Should().HaveCount(1); aggregate.Evidence.Should().HaveCount(1); aggregate.Referrals.Should().HaveCount(1); aggregate.Members.Should().HaveCount(3);
		}

		[Test]
		public async Task Findings_need_a_second_person_to_approve_and_closure_locks_the_notes()
		{
			var investigation = await _h.InvestigationsService.OpenAsync(Dept, Admin, "Warehouse fire", null, null, null);
			var caseId = investigation.RmsInvestigationCaseId;
			await _h.InvestigationsService.AddMemberAsync(Dept, Admin, caseId, Reviewer, RmsInvestigationRole.Reviewer);
			var note = await _h.InvestigationsService.AddNoteAsync(Dept, Admin, caseId, RmsInvestigationNoteKind.SceneNote, DateTime.UtcNow, null, "V pattern on east wall.");
			_h.Reports.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rep-1")).ReturnsAsync(new RmsIncidentReport { RmsIncidentReportId = "rep-1", DepartmentId = Dept, CurrentRevisionId = "rev-3", RecordNumber = "INC-2026-0100" });
			var link = await _h.InvestigationsService.LinkIncidentAsync(Dept, Admin, caseId, "rep-1");
			link.PinnedRevisionId.Should().Be("rev-3");

			Func<Task> closeEarly = () => _h.InvestigationsService.CloseAsync(Dept, Admin, caseId, "done");
			await closeEarly.Should().ThrowAsync<InvalidOperationException>("findings are not approved");

			await _h.InvestigationsService.RecordFindingsAsync(Dept, Admin, caseId, RmsFireCauseClassification.Accidental, "Overloaded extension cord", "Dock bay 2, floor level", "Accidental electrical fire.", true);
			investigation.State.Should().Be((int)RmsInvestigationCaseState.PendingReview);
			investigation.RecommendsIncidentAmendment.Should().BeTrue();
			Func<Task> selfApprove = () => _h.InvestigationsService.ApproveFindingsAsync(Dept, Admin, caseId);
			await selfApprove.Should().ThrowAsync<InvalidOperationException>("the author may not approve their own findings");
			await _h.InvestigationsService.ReturnFindingsAsync(Dept, Reviewer, caseId, "Cite the arc mapping.");
			investigation.State.Should().Be((int)RmsInvestigationCaseState.Active);
			await _h.InvestigationsService.RecordFindingsAsync(Dept, Admin, caseId, RmsFireCauseClassification.Accidental, "Overloaded extension cord (arc mapping confirms)", "Dock bay 2", "Accidental electrical fire.", true);
			await _h.InvestigationsService.ApproveFindingsAsync(Dept, Reviewer, caseId);
			investigation.FindingsApprovedByUserId.Should().Be(Reviewer);
			_h.Audits.Rows.Should().Contain(a => a.Action == (int)RmsAccessAuditAction.Sign && a.CorrelationId == caseId);

			Func<Task> reviewerCloses = () => _h.InvestigationsService.CloseAsync(Dept, Reviewer, caseId, "done");
			await reviewerCloses.Should().ThrowAsync<UnauthorizedAccessException>();
			await _h.InvestigationsService.CloseAsync(Dept, Admin, caseId, "Cause determined; referred to insurer.");
			investigation.IsClosed.Should().BeTrue();
			note.IsLocked.Should().BeTrue();
			Func<Task> editLocked = () => _h.InvestigationsService.UpdateNoteAsync(Dept, Admin, note.RmsInvestigationNoteId, null, "changed");
			await editLocked.Should().ThrowAsync<InvalidOperationException>();
			Func<Task> addToClosed = () => _h.InvestigationsService.AddNoteAsync(Dept, Admin, caseId, RmsInvestigationNoteKind.Other, DateTime.UtcNow, null, "late");
			await addToClosed.Should().ThrowAsync<InvalidOperationException>();

			await _h.InvestigationsService.ReopenAsync(Dept, Admin, caseId, "New witness.");
			investigation.IsClosed.Should().BeFalse();
			note.IsLocked.Should().BeTrue("locked notes stay locked; new notes may be added");
			await _h.InvestigationsService.AddNoteAsync(Dept, Admin, caseId, RmsInvestigationNoteKind.Interview, DateTime.UtcNow, "Neighbor", "Heard a pop.");
			(await _h.InvestigationsService.GetAsync(Dept, Admin, caseId)).Notes.Should().HaveCount(2);
		}

		[Test]
		public async Task Export_is_audited_and_refused_while_content_is_concealed()
		{
			var investigation = await _h.InvestigationsService.OpenAsync(Dept, Admin, "Warehouse fire", null, null, "Summary");
			var caseId = investigation.RmsInvestigationCaseId;
			await _h.InvestigationsService.AddEvidenceAsync(Dept, Admin, caseId, new RmsInvestigationEvidence { Description = "Cord" });
			var packet = Encoding.UTF8.GetString(await _h.InvestigationsService.ExportAsync(Dept, Admin, caseId, "10.0.0.9"));
			packet.Should().Contain("resgrid.investigation-packet.v1").And.Contain("Cord").And.Contain("\"custody\"");
			_h.Audits.Rows.Should().Contain(a => a.Action == (int)RmsAccessAuditAction.Export && a.CorrelationId == caseId && a.IpAddress == "10.0.0.9");
			(await _h.InvestigationsService.GetAccessAuditAsync(Dept, Admin, caseId, 50)).Should().NotBeEmpty();

			var concealed = new Mock<IRecordsProtectionService>();
			concealed.Setup(p => p.RevealInvestigationCasesAsync(Dept, It.IsAny<IReadOnlyList<RmsInvestigationCase>>(), default)).ReturnsAsync(new ProtectedReadResult { IsProtected = true, RedactedFields = new List<string> { "rmsinvestigationcases.findings" }, ProtectedReason = "step_up_required" });
			concealed.Setup(p => p.RevealInvestigationNotesAsync(Dept, It.IsAny<IReadOnlyList<RmsInvestigationNote>>(), default)).ReturnsAsync(new ProtectedReadResult());
			concealed.Setup(p => p.RevealInvestigationEvidenceAsync(Dept, It.IsAny<IReadOnlyList<RmsInvestigationEvidence>>(), default)).ReturnsAsync(new ProtectedReadResult());
			concealed.Setup(p => p.RevealInvestigationReferralsAsync(Dept, It.IsAny<IReadOnlyList<RmsInvestigationReferral>>(), default)).ReturnsAsync(new ProtectedReadResult());
			concealed.Setup(p => p.RevealPreventionAttachmentsAsync(Dept, It.IsAny<IReadOnlyList<RmsPreventionAttachment>>(), false, default)).ReturnsAsync(new ProtectedReadResult());
			var service = new RecordsInvestigationsService(_h.Gate, _h.Cases, _h.CaseIncidents, _h.CaseMembers, _h.CaseNotes, _h.Evidence, _h.Custody, _h.Referrals, _h.Attachments, _h.Reports.Object, _h.Audits, _h.Authorization.Object, concealed.Object, _h.UnitOfWork.Object);
			Func<Task> export = () => service.ExportAsync(Dept, Admin, caseId);
			(await export.Should().ThrowAsync<RecordProtectedContentException>()).Which.Reason.Should().Be("step_up_required");
		}

		[Test]
		public async Task Investigation_files_are_restricted_and_readable_only_by_members()
		{
			var investigation = await _h.InvestigationsService.OpenAsync(Dept, Admin, "Warehouse fire", null, null, null);
			var caseId = investigation.RmsInvestigationCaseId;
			var bytes = Encoding.UTF8.GetBytes("scene sketch");
			var attachment = await _h.AttachmentsService.AddAsync(Dept, Admin, RmsPreventionParentKind.InvestigationCase, caseId, "sketch.txt", "text/plain", bytes, "Scene sketch", false);
			attachment.Classification.Should().Be((int)RmsEvidenceClassification.Restricted);
			attachment.Checksum.Should().NotBeNullOrEmpty();
			_h.Protection.Writes.Should().Contain("prevention attachment");

			Func<Task> otherAdmin = () => _h.AttachmentsService.GetWithDataAsync(Dept, Admin2, attachment.RmsPreventionAttachmentId);
			await otherAdmin.Should().ThrowAsync<UnauthorizedAccessException>();
			await _h.InvestigationsService.AddMemberAsync(Dept, Admin, caseId, Reviewer, RmsInvestigationRole.ReadOnly);
			var read = await _h.AttachmentsService.GetWithDataAsync(Dept, Reviewer, attachment.RmsPreventionAttachmentId);
			read.Data.Should().Equal(bytes);
			_h.Audits.Rows.Should().Contain(a => a.Action == (int)RmsAccessAuditAction.Export && a.Purpose == "Investigation file downloaded" && a.ActorUserId == Reviewer);
			Func<Task> readOnlyUpload = () => _h.AttachmentsService.AddAsync(Dept, Reviewer, RmsPreventionParentKind.InvestigationCase, caseId, "x.txt", "text/plain", bytes, null, false);
			await readOnlyUpload.Should().ThrowAsync<UnauthorizedAccessException>();
			(await _h.InvestigationsService.GetAsync(Dept, Admin, caseId)).Attachments.Should().ContainSingle(a => a.FileName == "sketch.txt");
		}
	}
}
