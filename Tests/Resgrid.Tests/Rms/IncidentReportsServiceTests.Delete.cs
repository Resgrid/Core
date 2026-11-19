using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Deleting incident reports (soft delete; worker 43 erases the content later) and the bug that made it necessary: a
	/// cancelled report kept its Call, so a report started by mistake meant the Call could never get its real one.
	/// </summary>
	public partial class IncidentReportsServiceTests
	{
		[Test]
		public async Task A_cancelled_report_no_longer_holds_its_call()
		{
			var first = await _service.StartFromCallAsync(Dept, "author", CallId);
			await _service.CancelAsync(Dept, "author", first.Report.RmsIncidentReportId);

			(await _service.GetForCallAsync(Dept, CallId)).Should().BeNull("a cancelled report is not the Call's report");
			var second = await _service.StartFromCallAsync(Dept, "author", CallId);

			second.Report.RmsIncidentReportId.Should().NotBe(first.Report.RmsIncidentReportId, "starting again must give a new report, not the cancelled one");
			second.State.Should().Be(RmsRecordState.Draft);
			(await _service.GetForCallAsync(Dept, CallId)).Report.RmsIncidentReportId.Should().Be(second.Report.RmsIncidentReportId);
		}

		[Test]
		public async Task A_voided_report_still_holds_its_call()
		{
			_submissionEnabled = false;
			var voided = await FinalizeAndVoidAsync();

			var again = await _service.StartFromCallAsync(Dept, "author", CallId);

			again.Report.RmsIncidentReportId.Should().Be(voided, "a voided report was official; its replacement goes through amendment, not a second report");
		}

		[Test]
		public async Task Deleting_a_draft_cancels_it_first_then_hides_it_everywhere_and_frees_the_call()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;

			await _service.DeleteAsync(Dept, "author", id, "Started on the wrong call");

			var row = _store.Reports.Single(r => r.RmsIncidentReportId == id);
			row.State.Should().Be((int)RmsRecordState.Cancelled, "a never-finalized report goes through Cancel, its own lifecycle step");
			row.DeletedOn.Should().NotBeNull();
			row.PurgedOn.Should().BeNull("content stays until the recovery window ends");
			_store.Outbox.Should().ContainSingle(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCancelled);
			_store.Projections.Single(p => p.SourceId == id).DeletedOn.Should().NotBeNull("search and the change feed must drop it");
			// The audit is written while the report is still live; written after, the repository would strip purpose and reason.
			var audit = _store.Audits.Single(a => a.RecordId == id && a.Purpose == IncidentReportsService.DeleteAuditPurpose);
			audit.ActorUserId.Should().Be("author");
			audit.DetailJson.Should().Contain("Started on the wrong call");

			(await _service.GetAsync(Dept, id)).Should().BeNull();
			(await _service.QueryAsync(Dept, new RmsIncidentReportQuery())).Should().BeEmpty();
			(await _service.GetForCallAsync(Dept, CallId)).Should().BeNull();
			(await _service.StartFromCallAsync(Dept, "author", CallId)).Report.RmsIncidentReportId.Should().NotBe(id);
		}

		[Test]
		public async Task Deleting_a_cancelled_report_raises_no_second_cancel()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			await _service.CancelAsync(Dept, "author", id);

			await _service.DeleteAsync(Dept, "author", id, null);

			_store.Reports.Single(r => r.RmsIncidentReportId == id).DeletedOn.Should().NotBeNull();
			_store.Outbox.Count(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCancelled).Should().Be(1);
		}

		[Test]
		public async Task A_finalized_report_must_be_voided_before_it_can_be_deleted()
		{
			_submissionEnabled = false;
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			await _service.FinalizeAsync(Dept, "author", id, started.Report.RowVersion, null, null, null, null);

			Func<Task> delete = () => _service.DeleteAsync(Dept, "author", id, "test");

			(await delete.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.NotDeletable);
			_store.Reports.Single().DeletedOn.Should().BeNull();
		}

		[Test]
		public async Task A_voided_report_needs_a_department_admin_and_a_reason()
		{
			_submissionEnabled = false;
			var id = await FinalizeAndVoidAsync();

			Func<Task> byAuthor = () => _service.DeleteAsync(Dept, "author", id, "test report");
			(await byAuthor.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.VoidedNeedsDepartmentAdmin);

			_authorization.Setup(a => a.IsDepartmentAdminAsync("admin", Dept)).ReturnsAsync(true);
			Func<Task> noReason = () => _service.DeleteAsync(Dept, "admin", id, "  ");
			(await noReason.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.ReasonRequired);

			await _service.DeleteAsync(Dept, "admin", id, "Training scenario entered in production");

			var row = _store.Reports.Single(r => r.RmsIncidentReportId == id);
			row.DeletedOn.Should().NotBeNull();
			row.State.Should().Be((int)RmsRecordState.Voided, "the void revision stays as it was");
		}

		[Test]
		public async Task A_report_that_reached_a_destination_cannot_be_deleted()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			await _service.FinalizeAsync(Dept, "author", id, started.Report.RowVersion, null, null, null, null);
			// The worker sent it and the destination rejected it; then it was voided.
			var submission = _store.Submissions.Single();
			submission.State = (int)RmsSubmissionState.Rejected;
			submission.Attempts = 1;
			submission.SentOn = DateTime.UtcNow;
			submission.CompletedOn = DateTime.UtcNow;
			_store.Reports.Single().State = (int)RmsRecordState.Rejected;
			await _service.VoidAsync(Dept, "author", id, "duplicate", null);
			_authorization.Setup(a => a.IsDepartmentAdminAsync("admin", Dept)).ReturnsAsync(true);

			Func<Task> delete = () => _service.DeleteAsync(Dept, "admin", id, "test");

			(await delete.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.SubmittedExternally);
		}

		[Test]
		public void A_submission_counts_as_delivered_once_anything_went_out()
		{
			IncidentReportsService.LeftResgrid(new RmsSubmission { State = (int)RmsSubmissionState.Queued }).Should().BeFalse();
			IncidentReportsService.LeftResgrid(new RmsSubmission { State = (int)RmsSubmissionState.Superseded }).Should().BeFalse();
			IncidentReportsService.LeftResgrid(new RmsSubmission { State = (int)RmsSubmissionState.Superseded, Attempts = 1 }).Should().BeTrue();
			IncidentReportsService.LeftResgrid(new RmsSubmission { State = (int)RmsSubmissionState.Failed, RequiresReconciliation = true }).Should().BeTrue();
			IncidentReportsService.LeftResgrid(new RmsSubmission { State = (int)RmsSubmissionState.AwaitingDestination }).Should().BeTrue();
			IncidentReportsService.LeftResgrid(new RmsSubmission { State = (int)RmsSubmissionState.Queued, ExternalId = "FD1" }).Should().BeTrue();
		}

		[Test]
		public async Task A_legal_hold_refuses_the_delete()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			LegalHolds.Setup(h => h.GetActiveForDepartmentAsync(Dept))
				.ReturnsAsync(new List<RmsRecordLegalHold> { new RmsRecordLegalHold { RmsRecordLegalHoldId = "h1", DepartmentId = Dept, RecordId = id } });

			Func<Task> delete = () => _service.DeleteAsync(Dept, "author", id, null);

			(await delete.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.LegalHold);
			_store.Reports.Single().State.Should().Be((int)RmsRecordState.Draft, "a refused delete must not have cancelled the draft");
		}

		[Test]
		public async Task Casualty_and_exposure_entries_refuse_the_delete()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			_store.Casualties.Add(new RmsCasualtyRescue { RmsCasualtyRescueId = "c1", DepartmentId = Dept, RecordId = id, ProtectionId = "p" });

			Func<Task> delete = () => _service.DeleteAsync(Dept, "author", id, null);

			(await delete.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.PermanentContent);
		}

		[Test]
		public async Task Someone_who_may_not_close_the_report_out_is_refused_before_any_policy_answer()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);

			Func<Task> delete = () => _service.DeleteAsync(Dept, "stranger", started.Report.RmsIncidentReportId, null);

			await delete.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.Reports.Single().DeletedOn.Should().BeNull();
		}

		[Test]
		public async Task The_analysis_goes_with_its_report()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			_store.Analyses.Add(new RmsIncidentAnalysis { RmsIncidentAnalysisId = "a1", DepartmentId = Dept, IncidentReportId = id, State = (int)RmsIncidentAnalysisState.Draft, RowVersion = 1 });

			await _service.DeleteAsync(Dept, "author", id, null);

			_store.Analyses.Single().DeletedOn.Should().NotBeNull();
			_store.Analyses.Single().RowVersion.Should().Be(2);
		}

		[Test]
		public async Task Deleting_a_draft_cancels_and_deletes_in_one_transaction()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var commits = _store.Shared.Commits;

			await _service.DeleteAsync(Dept, "author", started.Report.RmsIncidentReportId, null);

			(_store.Shared.Commits - commits).Should().Be(1, "the cancel and the delete commit together");
			_store.Outbox.Single(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCancelled).Attempts.Should().Be(1, "RecordCancelled is dispatched once the delete has committed");
		}

		[Test]
		public async Task A_delete_that_fails_after_the_cancel_step_commits_nothing_and_dispatches_no_cancel_event()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var commits = _store.Shared.Commits;
			var discards = _store.Shared.Discards;
			_store.Shared.AuditsRepo.Setup(r => r.InsertAsync(It.Is<RmsAccessAudit>(a => a.Purpose == IncidentReportsService.DeleteAuditPurpose), It.IsAny<System.Threading.CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("audit store unavailable"));

			Func<Task> delete = () => _service.DeleteAsync(Dept, "author", started.Report.RmsIncidentReportId, null);

			await delete.Should().ThrowAsync<InvalidOperationException>();
			(_store.Shared.Commits - commits).Should().Be(0, "a cancel committed on its own would free the Call while the report stayed listed");
			(_store.Shared.Discards - discards).Should().Be(1);
			_store.Outbox.Where(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCancelled).Should().OnlyContain(o => o.Attempts == 0, "nothing is dispatched from a rolled-back transaction");
		}

		[Test]
		public async Task An_edit_after_the_policy_check_fails_the_delete_instead_of_hiding_the_newer_draft()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			var calls = 0;
			// Between the first policy answer and the delete, an editor saves an exposure: the stored row moves on a version.
			LegalHolds.Setup(h => h.GetActiveForDepartmentAsync(Dept)).ReturnsAsync(() =>
			{
				if (calls++ == 0)
				{
					var stored = _store.Reports.Single(r => r.RmsIncidentReportId == id);
					var edited = JsonConvert.DeserializeObject<RmsIncidentReport>(JsonConvert.SerializeObject(stored));
					edited.RowVersion = stored.RowVersion + 1;
					_store.Reports.Remove(stored);
					_store.Reports.Add(edited);
					_store.Exposures.Add(new RmsExposure { RmsExposureId = "x1", DepartmentId = Dept, RecordId = id, ProtectionId = "p" });
				}
				return new List<RmsRecordLegalHold>();
			});

			Func<Task> delete = () => _service.DeleteAsync(Dept, "author", id, null);

			await delete.Should().ThrowAsync<RecordConcurrencyException>();
			var row = _store.Reports.Single(r => r.RmsIncidentReportId == id);
			row.DeletedOn.Should().BeNull("the policy was answered for an older version");
			row.State.Should().Be((int)RmsRecordState.Draft, "the newer draft is not cancelled on the old answer");
			_store.Outbox.Should().NotContain(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCancelled);
		}

		[Test]
		public async Task A_hold_placed_after_the_first_check_still_refuses_the_delete()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			var calls = 0;
			LegalHolds.Setup(h => h.GetActiveForDepartmentAsync(Dept)).ReturnsAsync(() => calls++ == 0
				? new List<RmsRecordLegalHold>()
				: new List<RmsRecordLegalHold> { new RmsRecordLegalHold { RmsRecordLegalHoldId = "h1", DepartmentId = Dept, RecordId = id } });
			var commits = _store.Shared.Commits;

			Func<Task> delete = () => _service.DeleteAsync(Dept, "author", id, null);

			(await delete.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.LegalHold);
			_store.Reports.Single(r => r.RmsIncidentReportId == id).DeletedOn.Should().BeNull();
			(_store.Shared.Commits - commits).Should().Be(0);
		}

		[Test]
		public async Task An_analysis_started_after_the_first_check_goes_with_the_report()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			var calls = 0;
			_store.AnalysesRepo.Setup(r => r.GetForReportAsync(Dept, id)).ReturnsAsync(() =>
			{
				if (calls++ == 0)
				{
					// Committed by StartForReportAsync right after the delete's first look.
					_store.Analyses.Add(new RmsIncidentAnalysis { RmsIncidentAnalysisId = "a1", DepartmentId = Dept, IncidentReportId = id, State = (int)RmsIncidentAnalysisState.Draft, RowVersion = 1 });
					return null;
				}
				return _store.Analyses.FirstOrDefault(a => a.IncidentReportId == id && a.DeletedOn == null);
			});

			await _service.DeleteAsync(Dept, "author", id, null);

			_store.Analyses.Single().DeletedOn.Should().NotBeNull("an analysis committed before the delete's lock is deleted with its report");
		}

		[Test]
		public async Task Only_the_deleting_user_s_recent_delete_answers_a_retry()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			(await _service.WasDeletedByAsync(Dept, "author", id)).Should().BeFalse("the report is still live");

			await _service.DeleteAsync(Dept, "author", id, null);

			(await _service.WasDeletedByAsync(Dept, "author", id)).Should().BeTrue();
			(await _service.WasDeletedByAsync(Dept, "someone-else", id)).Should().BeFalse();
			(await _service.WasDeletedByAsync(Dept + 1, "author", id)).Should().BeFalse();
			_store.Reports.Single(r => r.RmsIncidentReportId == id).DeletedOn = DateTime.UtcNow.AddDays(-2);
			(await _service.WasDeletedByAsync(Dept, "author", id)).Should().BeFalse("past the idempotency window a retry is a new request");
		}

		[Test]
		public async Task An_analysis_filed_with_the_destination_refuses_the_delete()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			_store.Analyses.Add(new RmsIncidentAnalysis { RmsIncidentAnalysisId = "a1", DepartmentId = Dept, IncidentReportId = id, NerisAnalysisId = "NA-1", RowVersion = 1 });

			Func<Task> delete = () => _service.DeleteAsync(Dept, "author", id, null);

			(await delete.Should().ThrowAsync<IncidentReportDeleteRefusedException>()).Which.Refusal.Should().Be(IncidentReportDeleteRefusal.SubmittedExternally);
		}

		[Test]
		public async Task The_queue_can_leave_out_cancelled_and_voided_reports()
		{
			var cancelled = await _service.StartFromCallAsync(Dept, "author", CallId);
			await _service.CancelAsync(Dept, "author", cancelled.Report.RmsIncidentReportId);
			var open = await _service.StartFromCallAsync(Dept, "author", CallId);
			var closed = new List<int> { (int)RmsRecordState.Cancelled, (int)RmsRecordState.Voided };

			var page = await _service.QueryAsync(Dept, new RmsIncidentReportQuery { ExcludeStates = closed });
			var count = await _service.CountAsync(Dept, new RmsIncidentReportQuery { ExcludeStates = closed });
			var all = await _service.QueryAsync(Dept, new RmsIncidentReportQuery());
			var picked = await _service.QueryAsync(Dept, new RmsIncidentReportQuery { States = new List<int> { (int)RmsRecordState.Cancelled }, ExcludeStates = closed });

			page.Select(r => r.RmsIncidentReportId).Should().Equal(open.Report.RmsIncidentReportId);
			count.Should().Be(1);
			all.Should().HaveCount(2);
			picked.Select(r => r.RmsIncidentReportId).Should().Equal(new[] { cancelled.Report.RmsIncidentReportId }, "an explicit state wins over the exclusion");
		}

		[Test]
		public void Delete_carries_the_void_policy_on_both_surfaces_and_the_web_form_is_antiforgery_checked()
		{
			static IEnumerable<string> Policies(Type controller) => controller.GetMethods().Where(m => m.Name == "Delete")
				.SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()).Select(a => a.Policy);

			Policies(typeof(Resgrid.Web.Areas.User.Controllers.IncidentReportsController)).Should().Equal(Resgrid.Providers.Claims.ResgridResources.Record_Void);
			Policies(typeof(Resgrid.Web.Services.Controllers.v4.IncidentReportsController)).Should().Equal(Resgrid.Providers.Claims.ResgridResources.Record_Void);
			typeof(Resgrid.Web.Areas.User.Controllers.IncidentReportsController).GetMethods().Single(m => m.Name == "Delete")
				.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute), true).Should().ContainSingle();
		}

		private async Task<string> FinalizeAndVoidAsync()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var id = started.Report.RmsIncidentReportId;
			await _service.FinalizeAsync(Dept, "author", id, started.Report.RowVersion, null, null, null, null);
			await _service.VoidAsync(Dept, "author", id, "test", "Test report");
			return id;
		}
	}
}
