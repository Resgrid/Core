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
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		private sealed class ReminderHarness
		{
			public ChecklistReminderService Service;
			public List<ChecklistReminder> Notices = new List<ChecklistReminder>();
			public List<DepartmentMember> Members = new List<DepartmentMember> { new DepartmentMember { DepartmentId = 77, UserId = "author", IsAdmin = true }, new DepartmentMember { DepartmentId = 77, UserId = "crew" } };
			public Mock<ICommunicationService> Communication = new Mock<ICommunicationService>();
			public Mock<IUnitsService> Units = new Mock<IUnitsService>();
			public Mock<IDepartmentGroupsService> Groups = new Mock<IDepartmentGroupsService>();
			public Mock<IAuthorizationService> Authorization = new Mock<IAuthorizationService>();
			public Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public Mock<IUserProfileService> Profiles = new Mock<IUserProfileService>();
			public Action BeforeClaim;
		}
		private ReminderHarness Reminders(string language = "en", IChecklistAssetSource assets = null, IChecklistAssignmentService assignments = null)
		{
			var harness = new ReminderHarness(); var queue = new Mock<IChecklistReminderRepository>();
			harness.Departments.Setup(s => s.GetDepartmentByIdAsync(77, true)).ReturnsAsync(new Department { DepartmentId = 77, ManagingUserId = "author" });
			harness.Departments.Setup(s => s.GetAllMembersForDepartmentUnlimitedAsync(77, true)).ReturnsAsync(() => harness.Members);
			harness.Profiles.Setup(s => s.GetProfileByUserIdAsync(It.IsAny<string>(), false)).ReturnsAsync(new UserProfile { Language = language });
			harness.Communication.Setup(s => s.SendNotificationAsync(It.IsAny<string>(), 77, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), false)).ReturnsAsync(true);
			harness.Authorization.Setup(s => s.CanUserViewUnitAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
			harness.Authorization.Setup(s => s.CanUserViewPersonAsync(It.IsAny<string>(), It.IsAny<string>(), 77)).ReturnsAsync(true);
			queue.Setup(s => s.DepartmentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int after, CancellationToken ct) => after < 77 ? new List<int> { 77 } : new List<int>());
			queue.Setup(s => s.EnqueueAsync(It.IsAny<ChecklistReminder>(), It.IsAny<CancellationToken>())).Returns((ChecklistReminder row, CancellationToken ct) =>
			{
				if (!harness.Notices.Any(r => r.DepartmentId == row.DepartmentId && r.OccurrenceId == row.OccurrenceId && r.RecipientUserId == row.RecipientUserId && r.Kind == row.Kind && r.PeriodKey == row.PeriodKey)) harness.Notices.Add(row);
				return Task.CompletedTask;
			});
			queue.Setup(s => s.ClaimAsync(77, It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync((int department, DateTime now, bool digest, CancellationToken ct) =>
			{
				harness.BeforeClaim?.Invoke();
				var available = harness.Notices.Where(r => r.Status == 0 && r.NextAttemptUtc <= now && (!r.ClaimUntilUtc.HasValue || r.ClaimUntilUtc <= now)).ToList();
				var rows = available.Where(r => r.RecipientUserId == available.FirstOrDefault()?.RecipientUserId).Take(digest ? 500 : 1).ToList();
				foreach (var row in rows) { row.ClaimToken = Guid.NewGuid().ToString(); row.ClaimUntilUtc = now.AddMinutes(30); row.Attempts++; }
				return JsonConvert.DeserializeObject<List<ChecklistReminder>>(JsonConvert.SerializeObject(rows));
			});
			queue.Setup(s => s.FinishAsync(It.IsAny<ChecklistReminder>(), It.IsAny<ChecklistReminderStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns((ChecklistReminder notice, ChecklistReminderStatus status, DateTime now, CancellationToken ct) =>
			{
				var row = harness.Notices.Single(r => r.Id == notice.Id);
				if (row.Status != 0 || row.ClaimToken != notice.ClaimToken) throw new InvalidOperationException("Lost lease");
				row.Status = (int)status; row.ClaimToken = null; row.ClaimUntilUtc = null; row.NextAttemptUtc = now.AddMinutes(5); return Task.CompletedTask;
			});
			harness.Service = new ChecklistReminderService(_store, queue.Object, _uow.Object, _access.Object, harness.Departments.Object, harness.Units.Object, harness.Groups.Object,
				harness.Authorization.Object, _authorization.Object, harness.Communication.Object, harness.Profiles.Object, Mock.Of<IDepartmentSettingsService>(), assignments, assets);
			return harness;
		}
		private async Task<ChecklistOccurrence> ReminderOccurrence()
		{
			var setup = await Scheduled();
			await _service.SaveReminderSettingsAsync(_actor, new ChecklistReminderSettingsInput { Enabled = true, EscalateAfterMinutes = 30 });
			await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
			return (await _store.ListAsync<ChecklistOccurrence>(77)).OrderBy(r => r.PeriodStartUtc).First();
		}
		[Test]
		public async Task Reminder_settings_require_management_revision_and_valid_ranges_and_preserve_ADP_audits()
		{
			var input = new ChecklistReminderSettingsInput { Enabled = true };
			(await _service.ReminderSettingsAsync(_actor)).Enabled.Should().BeFalse();
			await _service.SaveReminderSettingsAsync(_actor, input);
			(await _service.ReminderSettingsAsync(_actor)).Revision.Should().Be(1);
			Func<Task> stale = () => _service.SaveReminderSettingsAsync(_actor, input); await stale.Should().ThrowAsync<ChecklistException>();
			input.Revision = 1; input.NotifyBeforeMinutes = -1;
			Func<Task> invalid = () => _service.SaveReminderSettingsAsync(_actor, input); await invalid.Should().ThrowAsync<ChecklistException>();
			input.NotifyBeforeMinutes = 60; input.EscalateAfterMinutes = 0; await invalid.Should().ThrowAsync<ChecklistException>();
			input.EscalateAfterMinutes = null;
			_authorization.Setup(a => a.CanManageAsync(_actor)).ReturnsAsync(false);
			Func<Task> denied = () => _service.SaveReminderSettingsAsync(_actor, input); (await denied.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			_authorization.Setup(a => a.CanManageAsync(_actor)).ReturnsAsync(true);
			_write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed(isProtected: true)));
			Func<Task> unsealedAudit = () => _service.SaveReminderSettingsAsync(_actor, input); await unsealedAudit.Should().ThrowAsync<ChecklistException>();
			(await _service.ReminderSettingsAsync(_actor)).Revision.Should().Be(1, "audit protection failure rolls back the settings change");
			ReadinessHistoryFields.AuditTypes.Should().Contain((int)AuditLogTypes.ChecklistReminderSettingsUpdated);
		}
		[TestCaseSource(typeof(ChecklistLocalizationTests), nameof(ChecklistLocalizationTests.Cultures))]
		public async Task Reminder_is_localized_generic_and_deduplicated_without_protected_reads(string language)
		{
			var occurrence = await ReminderOccurrence(); var harness = Reminders(language); _read.Invocations.Clear();
			var now = occurrence.PeriodStartUtc.Value;
			(await harness.Service.SweepAsync(now)).HandedOff.Should().Be(1);
			(await harness.Service.SweepAsync(now)).HandedOff.Should().Be(0);
			var invocation = harness.Communication.Invocations.Single();
			var message = (string)invocation.Arguments[2]; message.Should().Contain("/User/Checklists/Due").And.NotContain("SYNTHETIC");
			if (language != "en") { message.Should().NotContain("Sign in"); ((string)invocation.Arguments[5]).Should().NotBe("Checklist reminders"); }
			_read.Invocations.Should().BeEmpty();
			JsonConvert.SerializeObject(harness.Notices).Should().NotContain("SYNTHETIC").And.NotContain("Sign in");
		}
		[TestCase(true, 1), TestCase(false, 2)]
		public async Task Reminders_digest_combines_distinct_checks_for_one_recipient(bool digest, int sends)
		{
			var occurrence = await ReminderOccurrence(); var harness = Reminders();
			var copy = JsonConvert.DeserializeObject<ChecklistOccurrence>(JsonConvert.SerializeObject(occurrence)); copy.Id = Guid.NewGuid().ToString(); await _store.WriteAsync(copy, true);
			var settings = await _service.ReminderSettingsAsync(_actor); settings.DigestMode = digest; await _service.SaveReminderSettingsAsync(_actor, settings);
			(await harness.Service.SweepAsync(occurrence.PeriodStartUtc.Value)).HandedOff.Should().Be(2);
			harness.Communication.Invocations.Should().HaveCount(sends);
		}
		[TestCase(2), TestCase(5), TestCase(6)]
		public async Task Reminder_pending_delivery_is_suppressed_after_completion_skip_or_cancellation(int state)
		{
			var row = await ReminderOccurrence(); var harness = Reminders();
			harness.BeforeClaim = () => { row.State = state; _store.WriteAsync(row, false).GetAwaiter().GetResult(); };
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value)).Suppressed.Should().Be(1);
			harness.Communication.Invocations.Should().BeEmpty();
		}
		[Test]
		public async Task Reminder_pending_delivery_rechecks_flag_and_membership()
		{
			var row = await ReminderOccurrence(); var harness = Reminders();
			harness.BeforeClaim = () => _access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(false);
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value)).Suppressed.Should().Be(1);
			harness.Communication.Invocations.Should().BeEmpty();
			_access.Setup(a => a.CanUseChecklistsAsync(77)).ReturnsAsync(true); harness = Reminders();
			harness.BeforeClaim = () => harness.Members[0].IsDisabled = true;
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value)).Suppressed.Should().Be(1);
			harness.Communication.Invocations.Should().BeEmpty();
		}
		[Test]
		public async Task Reminder_provider_exception_retries_after_backoff_but_policy_suppression_does_not()
		{
			var row = await ReminderOccurrence(); var harness = Reminders(); var now = row.PeriodStartUtc.Value;
			harness.Communication.SetReturnsDefault(Task.FromException<bool>(new Exception("SYNTHETIC-PHI")));
			harness.Communication.Setup(s => s.SendNotificationAsync(It.IsAny<string>(), 77, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), false)).ThrowsAsync(new Exception("SYNTHETIC-PHI"));
			(await harness.Service.SweepAsync(now)).Errors.Should().Be(1); harness.Notices.Single().Status.Should().Be(0);
			await harness.Service.SweepAsync(now.AddMinutes(1)); harness.Communication.Invocations.Should().ContainSingle();
			harness.Communication.Setup(s => s.SendNotificationAsync(It.IsAny<string>(), 77, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), false)).ReturnsAsync(false);
			(await harness.Service.SweepAsync(now.AddMinutes(6))).Suppressed.Should().Be(1);
			await harness.Service.SweepAsync(now.AddMinutes(10)); harness.Communication.Invocations.Should().HaveCount(2);
			JsonConvert.SerializeObject(harness.Notices).Should().NotContain("SYNTHETIC");
		}
		[Test]
		public async Task Reminder_routes_unit_checks_to_current_staff_and_missed_escalations_to_admins()
		{
			var row = await ReminderOccurrence(); var harness = Reminders(); row.TargetType = (int)ChecklistTargetType.Unit; row.TargetId = "12"; await _store.WriteAsync(row, false);
			harness.Units.Setup(s => s.GetUnitByIdAsync(12)).ReturnsAsync(new Unit { UnitId = 12, DepartmentId = 77 });
			harness.Units.Setup(s => s.GetActiveRolesForUnitAsync(12)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { DepartmentId = 77, UnitId = 12, UserId = "crew" }, new UnitActiveRole { DepartmentId = 88, UnitId = 12, UserId = "foreign" } });
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value)).HandedOff.Should().Be(1);
			((string)harness.Communication.Invocations.Single().Arguments[0]).Should().Be("crew");
			row.State = 4; row.MissedOn = row.WindowEndUtc; await _store.WriteAsync(row, false);
			(await harness.Service.SweepAsync(row.WindowEndUtc.Value)).HandedOff.Should().Be(1);
			(await harness.Service.SweepAsync(row.WindowEndUtc.Value.AddMinutes(30))).HandedOff.Should().Be(1);
			harness.Notices.Should().ContainSingle(n => n.Kind == 2 && n.RecipientUserId == "author").And.NotContain(n => n.RecipientUserId == "foreign");
		}
		[Test]
		public async Task Reminder_rechecks_completion_after_profile_lookup()
		{
			var row = await ReminderOccurrence(); var harness = Reminders();
			harness.Profiles.Setup(s => s.GetProfileByUserIdAsync("author", false)).ReturnsAsync(() =>
			{ row.State = 2; _store.WriteAsync(row, false).GetAwaiter().GetResult(); return new UserProfile { Language = "fr" }; });
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value)).Suppressed.Should().Be(1);
			harness.Communication.Invocations.Should().BeEmpty();
		}
		[Test]
		public async Task Reminder_unit_assignment_changes_suppress_old_recipient_and_reach_current_staff()
		{
			var row = await ReminderOccurrence(); var harness = Reminders(); row.TargetType = (int)ChecklistTargetType.Unit; row.TargetId = "12"; await _store.WriteAsync(row, false);
			harness.Units.Setup(s => s.GetUnitByIdAsync(12)).ReturnsAsync(new Unit { UnitId = 12, DepartmentId = 77 });
			var currentUser = "crew";
			harness.Units.Setup(s => s.GetActiveRolesForUnitAsync(12)).ReturnsAsync(() => new List<UnitActiveRole> { new UnitActiveRole { DepartmentId = 77, UnitId = 12, UserId = currentUser } });
			harness.BeforeClaim = () => currentUser = "author";
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value)).Suppressed.Should().Be(1);
			harness.Communication.Invocations.Should().BeEmpty();
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value.AddMinutes(5))).HandedOff.Should().Be(1);
			((string)harness.Communication.Invocations.Single().Arguments[0]).Should().Be("author");
		}
		[Test]
		public async Task Reminder_unstaffed_unit_uses_its_current_station_group_and_rejects_cross_tenant_members()
		{
			var row = await ReminderOccurrence(); var harness = Reminders(); row.TargetType = (int)ChecklistTargetType.Unit; row.TargetId = "12"; await _store.WriteAsync(row, false);
			harness.Units.Setup(s => s.GetUnitByIdAsync(12)).ReturnsAsync(new Unit { UnitId = 12, DepartmentId = 77, StationGroupId = 8 });
			harness.Units.Setup(s => s.GetActiveRolesForUnitAsync(12)).ReturnsAsync(new List<UnitActiveRole>());
			harness.Groups.Setup(s => s.GetGroupByIdAsync(8, true)).ReturnsAsync(new DepartmentGroup { DepartmentId = 77, DepartmentGroupId = 8 });
			harness.Groups.Setup(s => s.GetAllMembersForGroupAsync(8)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentId = 77, UserId = "crew" }, new DepartmentGroupMember { DepartmentId = 88, UserId = "foreign" } });
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value)).HandedOff.Should().Be(1);
			((string)harness.Communication.Invocations.Single().Arguments[0]).Should().Be("crew");
		}
		[Test]
		public async Task Reminder_reenable_does_not_send_checks_from_the_disabled_interval()
		{
			var row = await ReminderOccurrence(); var harness = Reminders();
			var settings = (await _store.ListAsync<DepartmentChecklistSettings>(77)).Single(); settings.RemindersActiveFromUtc = row.PeriodStartUtc.Value.AddMinutes(1); await _store.WriteAsync(settings, false);
			(await harness.Service.SweepAsync(row.PeriodStartUtc.Value.AddMinutes(5))).HandedOff.Should().Be(0);
			harness.Notices.Should().BeEmpty();
		}
	}
}
