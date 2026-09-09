using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public sealed class InventoryM5ScheduledReportTests
	{
		private const string Canary = "SYNTHETIC-INVENTORY-REPORT-PHI-CANARY";
		private Mock<IScheduledTasksService> _tasks;
		private Mock<IInventoryAuthorizationService> _auth;
		private Mock<IInventoryMigrationService> _migration;
		private Mock<IInventoryOperationsService> _reports;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IUsersService> _users;
		private Mock<IUserProfileService> _profiles;
		private Mock<IPdfProvider> _pdf;
		private InventoryScheduledReportService _service;
		private ScheduledTask _queued;
		private ScheduledTask _current;
		private IdentityUser _user;
		private bool _protected;
		private bool _enabled;
		private bool _revoked;
		private InventoryReport _report;
		private string _html;
		private Action _duringPdf;
		private int _reportReads;

		[SetUp]
		public void SetUp()
		{
			_tasks = new(); _auth = new(); _migration = new(); _reports = new(); _protection = new(); _users = new(); _profiles = new(); _pdf = new();
			_protected = false; _enabled = true; _revoked = false; _duringPdf = null; _reportReads = 0; _html = null;
			_queued = new ScheduledTask { ScheduledTaskId = 12, DepartmentId = 77, UserId = "manager", Data = "6", TaskType = (int)TaskTypes.ReportDelivery, Active = true,
				ScheduleType = (int)ScheduleTypes.Weekly, Time = "08:00", Monday = true, UserEmailAddress = "stale@example.invalid" };
			_current = Clone(_queued); _user = new IdentityUser { Email = "current@example.invalid" };
			_tasks.Setup(x => x.GetScheduledTaskByIdAsync(12)).ReturnsAsync(() => Clone(_current));
			_auth.Setup(x => x.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), It.IsAny<PermissionTypes?>(), It.IsAny<int?>()))
				.Returns((InventoryActor actor, bool write, PermissionTypes? permission, int? group) => _revoked ? Task.FromException(new InventoryException(403, "MembershipRequired")) : Task.CompletedTask);
			_auth.Setup(x => x.IsEnabledAsync(77)).ReturnsAsync(() => _enabled); _migration.Setup(x => x.IsMigratedAsync(77)).ReturnsAsync(true);
			_protection.Setup(x => x.IsProtectionEnforcedAsync(77)).ReturnsAsync(() => _protected);
			_users.Setup(x => x.GetUserById("manager", true)).Returns(() => _user);
			_profiles.Setup(x => x.GetProfileByUserIdAsync("manager", true)).ReturnsAsync(new UserProfile { Language = "fr" });
			_report = new InventoryReport { Kind = InventoryReportKind.OnHand, GeneratedOn = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc), Columns = new() { "Name", "Amount" },
				Rows = new() { new() { ["Name"] = Canary, ["Amount"] = 1.125001m } } };
			_reports.Setup(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryReportInput>())).ReturnsAsync((InventoryActor actor, InventoryReportInput input) =>
			{
				actor.DepartmentId.Should().Be(77); actor.UserId.Should().Be("manager"); actor.GrantToken.Should().BeNull();
				var report = Clone(_report); report.Kind = input.Kind; report.GeneratedOn = report.GeneratedOn.AddSeconds(++_reportReads); return report;
			});
			_pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>())).Returns((string html) => { _html = html; _duringPdf?.Invoke(); return Encoding.ASCII.GetBytes("%PDF-1.4 synthetic"); });
			_service = new InventoryScheduledReportService(_tasks.Object, _auth.Object, _migration.Object, _reports.Object, _protection.Object, _users.Object, _profiles.Object, _pdf.Object);
		}

		[TestCase(6, InventoryReportKind.OnHand)]
		[TestCase(7, InventoryReportKind.Usage)]
		[TestCase(8, InventoryReportKind.Expiration)]
		[TestCase(9, InventoryReportKind.LowStock)]
		[TestCase(10, InventoryReportKind.TransferHistory)]
		[TestCase(11, InventoryReportKind.Issuance)]
		[TestCase(12, InventoryReportKind.Valuation)]
		[TestCase(13, InventoryReportKind.ControlledSubstanceLog)]
		public async Task Unprotected_scheduled_reports_deliver_current_authorized_data_without_a_grant(int type, InventoryReportKind kind)
		{
			_queued.Data = _current.Data = type.ToString(CultureInfo.InvariantCulture);
			var result = await _service.BuildAsync(_queued);
			result.To.Should().Be("current@example.invalid"); result.AttachmentName.Should().EndWith(".pdf"); Encoding.ASCII.GetString(result.AttachmentData).Should().StartWith("%PDF-");
			_html.Should().Contain(Canary).And.Contain("lang=\"fr\""); result.Subject.Should().Be(InventoryReportDocuments.Title(kind, CultureInfo.GetCultureInfo("fr")));
			await _service.ValidateAsync(_queued);
			_reports.Verify(x => x.BuildReportAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager" && a.GrantToken == null), It.Is<InventoryReportInput>(q => q.Kind == kind)), Times.AtLeast(3));
			_auth.Verify(x => x.RequireAsync(It.IsAny<InventoryActor>(), false, PermissionTypes.AdjustInventory, null), Times.AtLeastOnce);
			_auth.Verify(x => x.RequireAsync(It.IsAny<InventoryActor>(), false, PermissionTypes.ManageControlledSubstances, null), type == 13 ? Times.AtLeastOnce() : Times.Never());
		}

		[TestCase(6)]
		[TestCase(13)]
		public async Task Protected_scheduled_reports_are_localized_value_free_notices_and_never_query_inventory_or_receive_a_grant(int type)
		{
			_protected = true; _queued.Data = _current.Data = type.ToString(CultureInfo.InvariantCulture);
			var result = await _service.BuildAsync(_queued); await _service.ValidateAsync(_queued);
			result.To.Should().Be("current@example.invalid"); _html.Should().Contain("lang=\"fr\"").And.NotContain(Canary).And.NotContain("manager").And.NotContain("current@example.invalid").And.NotContain("1.125001");
			_reports.Invocations.Should().BeEmpty();
		}

		[TestCase("policy")]
		[TestCase("recipient")]
		[TestCase("member")]
		[TestCase("owner")]
		[TestCase("department")]
		[TestCase("data")]
		[TestCase("inactive")]
		[TestCase("module")]
		public async Task A_change_during_PDF_conversion_prevents_stale_or_unauthorized_delivery(string changed)
		{
			_duringPdf = () => Change(changed);
			await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>();
		}

		[Test]
		public async Task A_protected_to_unprotected_policy_flip_does_not_convert_the_notice_into_an_unattended_data_export()
		{
			_protected = true; _duringPdf = () => _protected = false;
			await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>(); _reports.Invocations.Should().BeEmpty();
		}

		[TestCase("policy")]
		[TestCase("recipient")]
		[TestCase("member")]
		[TestCase("owner")]
		[TestCase("data")]
		[TestCase("inactive")]
		public async Task Immediate_handoff_validation_rechecks_current_policy_recipient_membership_and_schedule(string changed)
		{
			await _service.BuildAsync(_queued); Change(changed);
			await FluentActions.Awaiting(() => _service.ValidateAsync(_queued)).Should().ThrowAsync<InventoryException>();
		}

		[Test]
		public async Task Data_or_source_visibility_changes_during_conversion_or_before_handoff_reject_the_old_PDF()
		{
			_duringPdf = () => _report.Rows[0]["Amount"] = 9m;
			(await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportDeliveryChanged");
			_duringPdf = null; await _service.BuildAsync(_queued); _report.Rows.Clear();
			(await FluentActions.Awaiting(() => _service.ValidateAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportDeliveryChanged");
		}

		[Test]
		public async Task Generated_timestamp_and_enumeration_order_do_not_make_an_unchanged_snapshot_undeliverable()
		{
			_report.Rows.Add(new Dictionary<string, object> { ["Name"] = "second", ["Amount"] = 2m });
			_duringPdf = () => { _report.Rows.Reverse(); _report.Rows = _report.Rows.Select(row => row.Reverse().ToDictionary(cell => cell.Key, cell => cell.Value)).ToList(); };
			await _service.BuildAsync(_queued); await _service.ValidateAsync(_queued); _reportReads.Should().BeGreaterThanOrEqualTo(3);
		}

		[TestCase(null)]
		[TestCase("5")]
		[TestCase("14")]
		[TestCase("06")]
		[TestCase("bad")]
		public async Task Non_inventory_or_noncanonical_scheduled_report_identifiers_fail_before_reading_data(string data)
		{
			_queued.Data = _current.Data = data;
			await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>(); _reports.Invocations.Should().BeEmpty(); _pdf.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Current_controlled_report_permission_is_required_even_for_a_protected_notice()
		{
			_protected = true; _queued.Data = _current.Data = "13";
			_auth.Setup(x => x.RequireAsync(It.IsAny<InventoryActor>(), false, PermissionTypes.ManageControlledSubstances, null)).ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			(await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.StatusCode.Should().Be(403);
			_reports.Invocations.Should().BeEmpty(); _pdf.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task A_foreign_or_non_report_schedule_and_missing_current_recipient_are_rejected()
		{
			_current.TaskType = (int)TaskTypes.UserStaffingLevel;
			await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>();
			_current.TaskType = _queued.TaskType; _current.ScheduledTaskId = 99;
			await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>();
			_current.ScheduledTaskId = 12; _user.Email = null;
			await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>(); _pdf.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Invalid_PDF_bytes_are_rejected_and_a_later_valid_retry_can_build()
		{
			_pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>())).Returns(Encoding.UTF8.GetBytes("not a pdf"));
			(await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportGenerationFailed");
			_pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>())).Returns(Encoding.ASCII.GetBytes("%PDF-1.4 synthetic"));
			await _service.BuildAsync(_queued); await _service.ValidateAsync(_queued);
		}

		[Test, Combinatorial]
		public async Task Rescheduling_prevents_delivery_before_build_during_rendering_and_before_handoff(
			[Values("before-build", "during-pdf", "before-handoff")] string phase,
			[Values("type", "date", "time", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday")] string timing,
			[Values(false, true)] bool protectedData)
		{
			_protected = protectedData;
			if (phase == "before-build") ChangeTiming(_current, timing);
			if (phase == "during-pdf") _duringPdf = () => ChangeTiming(_current, timing);
			if (phase == "before-handoff")
			{
				await _service.BuildAsync(_queued); ChangeTiming(_current, timing);
				(await FluentActions.Awaiting(() => _service.ValidateAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportDeliveryChanged");
			}
			else
			{
				(await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportDeliveryChanged");
				if (phase == "before-build") { _reports.Invocations.Should().BeEmpty(); _pdf.Invocations.Should().BeEmpty(); }
			}
			if (protectedData) _reports.Invocations.Should().BeEmpty();
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Updating_the_queued_object_cannot_reuse_a_proof_for_an_earlier_schedule(bool duringPdf)
		{
			void Reschedule() { ChangeTiming(_current, "time"); _queued.Time = _current.Time; }
			if (duringPdf)
			{
				_duringPdf = Reschedule;
				(await FluentActions.Awaiting(() => _service.BuildAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportDeliveryChanged");
			}
			else
			{
				await _service.BuildAsync(_queued); Reschedule();
				(await FluentActions.Awaiting(() => _service.ValidateAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportDeliveryChanged");
			}
		}

		[Test]
		public async Task A_timing_change_during_report_revalidation_is_caught_by_the_final_schedule_check()
		{
			await _service.BuildAsync(_queued);
			_reports.Setup(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryReportInput>())).ReturnsAsync(() =>
			{
				ChangeTiming(_current, "time"); _queued.Time = _current.Time; return Clone(_report);
			});
			(await FluentActions.Awaiting(() => _service.ValidateAsync(_queued)).Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("ReportDeliveryChanged");
		}

		[Test]
		public async Task Matching_schedule_wall_times_survive_database_DateTimeKind_normalization()
		{
			_queued.ScheduleType = _current.ScheduleType = (int)ScheduleTypes.SpecifcDateTime;
			_queued.SpecifcDate = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
			_current.SpecifcDate = DateTime.SpecifyKind(_queued.SpecifcDate.Value, DateTimeKind.Unspecified);
			await _service.BuildAsync(_queued); await _service.ValidateAsync(_queued);
		}

		private static void ChangeTiming(ScheduledTask task, string timing)
		{
			switch (timing)
			{
				case "type": task.ScheduleType = (int)ScheduleTypes.SpecifcDateTime; break;
				case "date": task.SpecifcDate = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc); break;
				case "time": task.Time = "09:00"; break;
				case "Sunday": task.Sunday = !task.Sunday; break;
				case "Monday": task.Monday = !task.Monday; break;
				case "Tuesday": task.Tuesday = !task.Tuesday; break;
				case "Wednesday": task.Wednesday = !task.Wednesday; break;
				case "Thursday": task.Thursday = !task.Thursday; break;
				case "Friday": task.Friday = !task.Friday; break;
				case "Saturday": task.Saturday = !task.Saturday; break;
				default: throw new ArgumentOutOfRangeException(nameof(timing));
			}
		}

		private void Change(string change)
		{
			switch (change)
			{
				case "policy": _protected = !_protected; break;
				case "recipient": _user.Email = "changed@example.invalid"; break;
				case "member": _revoked = true; break;
				case "owner": _current.UserId = "other-user"; break;
				case "department": _current.DepartmentId = 88; break;
				case "data": _current.Data = "13"; break;
				case "inactive": _current.Active = false; break;
				case "module": _enabled = false; break;
				default: throw new ArgumentOutOfRangeException(nameof(change));
			}
		}
		private static T Clone<T>(T input) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(input));
	}
}
