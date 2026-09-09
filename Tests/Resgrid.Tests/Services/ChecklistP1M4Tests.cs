using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Records.Evidence;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.ReportDelivery;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		private static readonly DateTime ReportMonth = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
		private const string ReportAsset = "81366c6a-aaad-41be-9df1-9091a42d6073";
		private static ChecklistReportQuery Month() => new() { FromUtc = ReportMonth, UntilUtc = ReportMonth.AddDays(30) };
		private async Task SeedReportMonth()
		{
			_authorization.Setup(a => a.CanReadAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistCompletion>())).ReturnsAsync((ChecklistActor a, ChecklistCompletion c) => a.DepartmentId == c.DepartmentId && a.UserId == "author");
			foreach (var target in new[] { new ChecklistTarget { Type = ChecklistTargetType.Unit, Id = "1", Name = "Engine 1" }, new ChecklistTarget { Type = ChecklistTargetType.InventoryAsset, Id = ReportAsset, Name = "=SUM(1,2)" }, new ChecklistTarget { Type = ChecklistTargetType.Personnel, Id = "person", Name = "Synthetic person" } })
			{
				var definition = new ChecklistDefinition { DepartmentId = 77, Content = "{}" };
				var version = new ChecklistDefinitionVersion { DepartmentId = 77, ParentId = definition.Id, Version = 3, Content = JsonConvert.SerializeObject(new ChecklistForm { Name = "Synthetic check <script>alert(1)</script>" }) };
				await _store.WriteAsync(definition, true); await _store.WriteAsync(version, true);
				for (var day = 0; day < 30; day++)
				{
					var at = ReportMonth.AddDays(day).AddHours(8);
					var occurrence = new ChecklistOccurrence { DepartmentId = 77, ParentId = definition.Id, VersionId = version.Id, ScheduleId = Guid.NewGuid().ToString(), TargetType = (int)target.Type, TargetId = target.Id,
						PeriodStartUtc = at, WindowEndUtc = at.AddHours(1), CreatedOn = at, UpdatedOn = at, State = day < 20 ? 2 : day < 25 ? 4 : 5, Content = JsonConvert.SerializeObject(target) };
					if (day < 20)
					{
						var completion = new ChecklistCompletion { DepartmentId = 77, ParentId = definition.Id, VersionId = version.Id, OccurrenceId = occurrence.Id, TargetType = (int)target.Type, TargetId = target.Id, CreatedBy = "author",
							State = 2, SubmittedOn = at.AddMinutes(day < 15 ? 30 : 90), Content = "{\"Note\":\"SYNTHETIC-PHI-CANARY\"}", Score = 80, Passed = true };
						occurrence.CompletionId = completion.Id; await _store.WriteAsync(completion, true);
					}
					await _store.WriteAsync(occurrence, true);
				}
			}
		}
		[Test]
		public async Task P1M4_seeded_month_matches_hand_calculated_unit_person_and_asset_rates_and_missed_trend()
		{
			await SeedReportMonth(); var report = await _service.GetComplianceSummaryAsync(_actor, Month());
			report.Groups.Should().HaveCount(3);
			foreach (var group in report.Groups) { group.Expected.Should().Be(25); group.Completed.Should().Be(20); group.OnTime.Should().Be(15); group.Missed.Should().Be(10); group.Skipped.Should().Be(5); group.CompletionRate.Should().Be(80); }
			report.Trend.Sum(d => d.Missed).Should().Be(30); report.Trend.Sum(d => d.Expected).Should().Be(75);
			var history = await _service.GetEntityChecklistHistoryAsync(_actor, ChecklistTargetType.InventoryAsset, ReportAsset, ReportMonth, ReportMonth.AddDays(30));
			history.Should().HaveCount(30); history.Should().OnlyContain(e => e.Version == 3 && e.Target.Id == ReportAsset);
			JsonConvert.SerializeObject(report).Should().NotContain("SYNTHETIC-PHI-CANARY");
		}
		[Test]
		public async Task P1M4_reports_fail_closed_for_protected_data_disabled_flags_and_invalid_ranges_and_filter_other_members()
		{
			await SeedReportMonth(); var query = Month(); query.UntilUtc = query.FromUtc;
			(await FluentActions.Awaiting(() => _service.GetComplianceSummaryAsync(_actor, query)).Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(400);
			var other = await _service.GetComplianceSummaryAsync(new ChecklistActor { DepartmentId = 77, UserId = "crew" }, Month()); other.Entries.Should().BeEmpty(); other.UnavailableSources.Should().Contain("RestrictedChecklistResults");
			_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistdefinitionversions.content" } }));
			(await FluentActions.Awaiting(() => _service.GetComplianceSummaryAsync(_actor, Month())).Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			_access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(false);
			(await FluentActions.Awaiting(() => _service.GetComplianceSummaryAsync(_actor, Month())).Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
		}
		[Test]
		public async Task P1M4_open_windows_and_on_demand_checks_do_not_inflate_the_denominator()
		{
			await SeedReportMonth(); var rows = await _store.ListAsync<ChecklistOccurrence>(77);
			foreach (var row in rows.Take(2)) { row.WindowEndUtc = DateTime.UtcNow.AddYears(1); await _store.WriteAsync(row, false); }
			foreach (var row in rows.Skip(2).Take(2)) { row.ScheduleId = null; await _store.WriteAsync(row, false); }
			var report = await _service.GetComplianceSummaryAsync(_actor, Month());
			report.Entries.Where(e => e.DueUtc > DateTime.UtcNow || !e.Scheduled).Should().OnlyContain(e => !e.Expected && !e.Missed);
		}
		private void PacketService(Mock<IChecklistHistoricalAssetSource> assets, DateTime? callAt = null, int department = 77)
		{
			var call = new Call { CallId = 101, DepartmentId = department, LoggedOn = callAt ?? ReportMonth.AddDays(30), Name = "CALL-PHI-CANARY", UnitDispatches = new List<CallDispatchUnit> { new() { CallId = 101, UnitId = 1, CallDispatchUnitId = 21, DispatchedOn = ReportMonth.AddDays(30) } } };
			var calls = new Mock<ICallsService>(); calls.Setup(c => c.GetCallByIdAsync(101, true)).ReturnsAsync(call);
			calls.Setup(c => c.PopulateCallData(call, false, false, false, false, true, false, false, false, false, false)).ReturnsAsync(call);
			var auth = new Mock<IAuthorizationService>(); auth.Setup(a => a.CanUserViewCallAsync("author", 101)).ReturnsAsync(true);
			_service = new ChecklistsService(_store, _authorization.Object, _access.Object, _uow.Object, _audits.Object, _outbox.Object, new(() => _read.Object), new(() => _write.Object), _scanner.Object,
				reportCalls: new(() => calls.Object), reportAuthorization: new(() => auth.Object), historicalAssets: assets?.Object);
		}
		[Test]
		public async Task P1M4_packet_includes_all_dispatched_unit_and_historically_issued_asset_checks_without_call_narrative()
		{
			await SeedReportMonth(); var assets = new Mock<IChecklistHistoricalAssetSource>();
			assets.Setup(a => a.AtCallAsync(It.IsAny<ChecklistActor>(), 101, ReportMonth.AddDays(30), It.IsAny<IReadOnlyCollection<int>>(), false)).ReturnsAsync(new List<ReadinessAssetSnapshot> { new() { DepartmentId = 77, UnitId = 1, AssetId = ReportAsset, Name = "Issued SCBA", SourceId = "issue-7", SourceVersion = "1", IssuedUtc = ReportMonth } });
			PacketService(assets); var packet = await _service.GetReadinessPacketForCallAsync(_actor, 101);
			packet.Units.Should().ContainSingle(); packet.Assets.Should().ContainSingle(); packet.Checklists.Should().HaveCount(60); packet.Checklists.Count(e => e.Missed).Should().Be(20);
			packet.Checklists.Should().OnlyContain(e => e.Target.Type != ChecklistTargetType.Personnel);
			packet.UnavailableSources.Should().Contain("ContractorEquipmentUnavailable").And.Contain("WorkOrdersUnavailable").And.NotContain("HistoricalInventoryUnavailable");
			JsonConvert.SerializeObject(packet).Should().NotContain("CANARY");
		}
		[Test]
		public async Task P1M4_contractor_equipment_can_be_deployed_directly_to_the_call_without_a_unit_assignment()
		{
			await SeedReportMonth(); var assets = new Mock<IChecklistHistoricalAssetSource>();
			assets.Setup(a => a.AtCallAsync(It.IsAny<ChecklistActor>(), 101, It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<int>>(), true)).ReturnsAsync(new List<ReadinessAssetSnapshot> { new() { DepartmentId = 77, CallId = 101, AssetId = ReportAsset, Name = "Deployed equipment", SourceId = "deployment-7", SourceVersion = "2", IssuedUtc = ReportMonth } });
			PacketService(assets); var packet = await _service.GetReadinessPacketForCallAsync(_actor, 101);
			packet.Assets.Should().ContainSingle(a => a.SourceSubsystem == "ContractorBilling" && a.UnitId == null); packet.Checklists.Should().HaveCount(60);
			packet.UnavailableSources.Should().NotContain("ContractorEquipmentUnavailable");
		}

		[Test]
		public async Task P1M4_packet_does_not_use_submissions_or_witnesses_after_call_time_and_rejects_foreign_calls()
		{
			await SeedReportMonth(); PacketService(null, ReportMonth.AddHours(8).AddMinutes(45));
			var packet = await _service.GetReadinessPacketForCallAsync(_actor, 101); packet.Checklists.Should().ContainSingle(); packet.Checklists[0].Completed.Should().BeTrue();
			var completion = await _store.GetAsync<ChecklistCompletion>(77, packet.Checklists[0].CompletionId); completion.WitnessedOn = ReportMonth.AddDays(1); await _store.WriteAsync(completion, false);
			packet = await _service.GetReadinessPacketForCallAsync(_actor, 101); packet.Checklists[0].Completed.Should().BeFalse(); packet.Checklists[0].Passed.Should().BeNull(); packet.Checklists[0].Score.Should().BeNull();
			PacketService(null, department: 88);
			(await FluentActions.Awaiting(() => _service.GetReadinessPacketForCallAsync(_actor, 101)).Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
		}
		[Test]
		public async Task P1M4_packet_rejects_current_only_asset_assignments_and_marks_unavailable_sources()
		{
			await SeedReportMonth(); PacketService(null); var packet = await _service.GetReadinessPacketForCallAsync(_actor, 101);
			packet.Assets.Should().BeEmpty(); packet.UnavailableSources.Should().Contain("HistoricalInventoryUnavailable");
			var assets = new Mock<IChecklistHistoricalAssetSource>(); assets.Setup(a => a.AtCallAsync(It.IsAny<ChecklistActor>(), 101, It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<int>>(), false))
				.ReturnsAsync(new List<ReadinessAssetSnapshot> { new() { DepartmentId = 77, UnitId = 1, AssetId = ReportAsset, SourceId = "issue", SourceVersion = "2", IssuedUtc = ReportMonth.AddDays(31) } });
			PacketService(assets); (await FluentActions.Awaiting(() => _service.GetReadinessPacketForCallAsync(_actor, 101)).Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
		}
		[Test]
		public async Task P1M4_exports_escape_HTML_and_CSV_formulas_and_package_checksums_survive_source_changes()
		{
			await SeedReportMonth(); var report = await _service.GetComplianceSummaryAsync(_actor, Month());
			report.Entries[0].Passed = false; report.Entries[0].Score = 100;
			ChecklistReportDocuments.Compliance(report).Should().Contain(ChecklistReportDocuments.Text("Failed"));
			Encoding.UTF8.GetString(ChecklistReportDocuments.Csv(report)).Should().Contain("'=" + "SUM(1,2)");
			ChecklistReportDocuments.Compliance(report).Should().NotContain("<script>").And.Contain("&lt;script&gt;").And.NotContain("CANARY");
			PacketService(null); var packet = await _service.GetReadinessPacketForCallAsync(_actor, 101); var pdf = new Mock<IPdfProvider>(); pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns(Encoding.ASCII.GetBytes("%PDF-1.4 synthetic"));
			var grant = Mock.Of<IProtectedGrantContext>(g => g.UserId == "author" && g.GrantToken == "synthetic-grant" && !g.IsWorkloadCaller);
			var adapter = new ReadinessPacketEvidenceAdapter(_service, _access.Object, grant, pdf.Object);
			var capture = await adapter.CaptureAsync(new RecordEvidenceCaptureRequest { DepartmentId = 77, CapturedByUserId = "author", CallId = 101 });
			capture.Classification.Should().Be(RmsEvidenceClassification.Restricted); var package = (ReadinessEvidencePackage)capture.Manifest;
			ChecklistReportDocuments.Sha256(package.Pdf).Should().Be(package.PdfSha256); ChecklistReportDocuments.Sha256(Encoding.UTF8.GetBytes(package.ManifestJson)).Should().Be(package.ManifestSha256);
			var original = package.ManifestJson; var row = await _store.GetAsync<ChecklistOccurrence>(77, packet.Checklists[0].OccurrenceId); row.Content = "{\"Name\":\"changed\"}"; await _store.WriteAsync(row, false);
			package.ManifestJson.Should().Be(original); package.ManifestSha256.Should().Be(ChecklistReportDocuments.Sha256(Encoding.UTF8.GetBytes(original)));
		}
		[Test]
		public async Task P1M4_live_hints_are_committed_only_for_changes_and_replays_do_not_duplicate_them()
		{
			var run = await Start(); _events.Clear(); var input = Answers(run.Form);
			await _service.SaveRunAsync(_actor, run.Run, input, false);
			_events.Should().ContainSingle(e => e.EventName == "ChecklistRefreshRequested" && !e.Trigger.HasValue);
			JsonConvert.SerializeObject(_events).Should().NotContain("Equipment works").And.NotContain("author");
			await _service.SaveRunAsync(_actor, run.Run, input, false); _events.Should().HaveCount(1);
		}
		[Test, NonParallelizable]
		public async Task P1M4_scheduled_notice_is_localized_value_free_and_delivery_logs_only_success()
		{
			var previousDoNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			try
			{
				Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
				var task = new ScheduledTask { ScheduledTaskId = 12, DepartmentId = 77, UserId = "author", Data = "4", Active = true, TaskType = (int)TaskTypes.ReportDelivery };
				var tasks = new Mock<IScheduledTasksService>(); tasks.Setup(t => t.GetScheduledTaskByIdAsync(12)).ReturnsAsync(task);
				var users = new Mock<IUsersService>(); users.Setup(u => u.GetUserById("author", true)).Returns(new Resgrid.Model.Identity.IdentityUser { Email = "synthetic@example.invalid" });
				var profiles = new Mock<IUserProfileService>(); profiles.Setup(p => p.GetProfileByUserIdAsync("author", It.IsAny<bool>())).ReturnsAsync(new UserProfile { Language = "fr" });
				string html = null; var pdf = new Mock<IPdfProvider>(); pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns((string value) => { html = value; return Encoding.ASCII.GetBytes("%PDF-1.4 synthetic"); });
				var service = new ChecklistScheduledReportService(tasks.Object, _authorization.Object, _access.Object, users.Object, profiles.Object, pdf.Object);
				var notification = await service.BuildAsync(task); notification.Subject.Should().Contain("conformité"); html.Should().NotContain("CANARY").And.NotContain("author").And.NotContain("synthetic@example.invalid");
				var email = new Mock<IEmailService>(); var logic = new ReportDeliveryLogic(tasks.Object, email.Object, pdf.Object, service);
				var item = new ReportDeliveryQueueItem { ScheduledTask = task, Department = new Department { DepartmentId = 77 }, Email = "stale@example.invalid" };
				(await logic.Process(item)).Item1.Should().BeTrue(); email.Verify(e => e.SendReportDeliveryAsync(It.Is<EmailNotification>(n => n.To == "synthetic@example.invalid"), 77, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
				tasks.Verify(t => t.CreateScheduleTaskLogAsync(task, It.IsAny<CancellationToken>()), Times.Once);
				email.Setup(e => e.SendReportDeliveryAsync(It.IsAny<EmailNotification>(), 77, It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("SYNTHETIC-PHI-CANARY"));
				var failed = await logic.Process(item); failed.Item1.Should().BeFalse(); failed.Item2.Should().NotContain("CANARY"); tasks.Verify(t => t.CreateScheduleTaskLogAsync(task, It.IsAny<CancellationToken>()), Times.Once);
				task.Active = false; (await FluentActions.Awaiting(() => service.BuildAsync(task)).Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			}
			finally { Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = previousDoNotBroadcast; }
		}
	}
}
