using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using static Resgrid.Tests.Wellness.WellnessTestWorld;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// Member wellness WL0 (protocols-training-knowledge-plan §13b): the restricted tier (admin status alone opens nothing, members read
	/// their own, the wellness officer role reads everyone's), the operational vs protected split, retention and legal hold, suggestions
	/// that only a person confirms, anniversary reminders that are opt-in and claimed, and an export of the member's own rows only.
	/// </summary>
	[TestFixture]
	public class MemberWellnessServiceTests
	{
		private static readonly DateTime Now = DateTime.UtcNow;
		private bool _doNotBroadcast;

		[SetUp]
		public void Allow_broadcast()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
		}

		[TearDown]
		public void Restore_broadcast() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;

		#region Restricted tier

		[Test]
		public async Task An_administrator_without_the_wellness_role_cannot_read_another_members_entries()
		{
			var world = new WellnessTestWorld();
			var entry = world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-3));
			var service = world.Service();

			Func<Task> overview = () => service.GetOverviewAsync(DepartmentId, Admin, Member);
			await overview.Should().ThrowAsync<UnauthorizedAccessException>();
			(await service.GetEntryAsync(DepartmentId, Admin, entry.RmsResponderExposureId)).Should().BeNull("the protected substance and health details stay out of an administrator's reach");
			Func<Task> report = () => service.GetDepartmentSummaryAsync(DepartmentId, Admin, null, null);
			await report.Should().ThrowAsync<UnauthorizedAccessException>();
			(await service.IsWellnessOfficerAsync(DepartmentId, Admin)).Should().BeFalse();
		}

		[Test]
		public async Task A_member_reads_their_own_entries_with_the_protected_details()
		{
			var world = new WellnessTestWorld();
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-3));
			world.AddExposure(Peer, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-2), substance: "Someone else's");

			var overview = await world.Service().GetOverviewAsync(DepartmentId, Member, Member);

			overview.Entries.Should().ContainSingle();
			overview.Entries[0].UserId.Should().Be(Member);
			overview.Entries[0].Substance.Should().Be("Diesel exhaust");
			overview.Entries[0].HealthDetails.Should().Be("Cough for two days");
		}

		[Test]
		public async Task A_member_cannot_read_another_members_entries_and_the_wellness_officer_can()
		{
			var world = new WellnessTestWorld();
			var entry = world.AddExposure(Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, Now.AddDays(-3));
			var service = world.Service();

			(await service.GetEntryAsync(DepartmentId, Peer, entry.RmsResponderExposureId)).Should().BeNull();
			(await service.GetEntryAsync(DepartmentId, Officer, entry.RmsResponderExposureId)).Should().NotBeNull();
			(await service.GetOverviewAsync(DepartmentId, Officer, Member)).CriticalIncidentCount.Should().Be(1);
		}

		[Test]
		public async Task Without_a_wellness_officer_role_configured_nobody_but_the_member_reads_a_log()
		{
			var world = new WellnessTestWorld { Settings = new WellnessSettings() };
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-3));
			var service = world.Service();

			(await service.IsWellnessOfficerAsync(DepartmentId, Officer)).Should().BeFalse();
			Func<Task> read = () => service.GetOverviewAsync(DepartmentId, Officer, Member);
			await read.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Only_a_wellness_officer_records_an_entry_for_someone_else()
		{
			var world = new WellnessTestWorld();
			var input = new ResponderExposureInput { UserId = Member, Category = (int)ResponderExposureCategories.HazardousSubstance, OccurredOn = Now.AddHours(-5) };
			var service = world.Service();

			Func<Task> byAdmin = () => service.RecordAsync(DepartmentId, Admin, input);
			await byAdmin.Should().ThrowAsync<UnauthorizedAccessException>();
			var saved = await service.RecordAsync(DepartmentId, Officer, input);
			saved.UserId.Should().Be(Member);
			saved.Status.Should().Be((int)ResponderExposureStatuses.Confirmed);
		}

		[Test]
		public async Task Only_department_administrators_change_the_wellness_settings_and_the_retention_never_drops_below_30_years()
		{
			var world = new WellnessTestWorld();
			var service = world.Service();

			Func<Task> byOfficer = () => service.SaveSettingsAsync(DepartmentId, Officer, new WellnessSettings());
			await byOfficer.Should().ThrowAsync<UnauthorizedAccessException>();

			var stored = await service.SaveSettingsAsync(DepartmentId, Admin, new WellnessSettings { WellnessOfficerRoleId = OfficerRoleId, RetentionYears = 5 });
			stored.RetentionYears.Should().Be(MemberWellnessRules.MinimumRetentionYears);
			world.SettingsService.Verify(s => s.SetWellnessSettingsAsync(DepartmentId, It.Is<WellnessSettings>(w => w.RetentionYears == 30), default), Times.Once);
			world.Audits.Should().ContainSingle(a => a.Type == AuditLogTypes.WellnessSettingsChanged);
		}

		#endregion Restricted tier

		#region Operational vs protected

		[Test]
		public async Task Audit_entries_name_which_details_exist_but_never_carry_a_protected_value()
		{
			var world = new WellnessTestWorld();
			await world.Service().RecordAsync(DepartmentId, Member, new ResponderExposureInput
			{
				Category = (int)ResponderExposureCategories.BloodbornePathogen,
				OccurredOn = Now.AddHours(-2),
				Substance = "SECRET-SUBSTANCE",
				HealthDetails = "SECRET-HEALTH",
				Narrative = "SECRET-NARRATIVE"
			});

			var audit = world.Audits.Single(a => a.Type == AuditLogTypes.ResponderExposureRecorded);
			audit.After.Should().NotContain("SECRET").And.Contain("BloodbornePathogen").And.Contain("substance").And.Contain("healthdetails");
		}

		[Test]
		public async Task The_officer_report_counts_operational_columns_and_its_export_rows_carry_no_protected_value()
		{
			var world = new WellnessTestWorld();
			world.AddExposure(Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, Now.AddDays(-10));
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-3));
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Suggested, Now.AddDays(-1));
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Voided, Now.AddDays(-1));
			var service = world.Service();

			var summary = (await service.GetDepartmentSummaryAsync(DepartmentId, Officer, null, null)).Single();
			summary.Total.Should().Be(2);
			summary.CriticalIncidents.Should().Be(1);
			summary.PendingSuggestions.Should().Be(1);
			world.ProtectedRead.VerifyNoOtherCalls();

			var rows = await service.GetDepartmentEntriesAsync(DepartmentId, Officer, null, null);
			rows.Should().HaveCount(2);
			rows.Should().OnlyContain(r => r.Substance == null && r.HealthDetails == null && r.Narrative == null && r.PpeWorn == null && r.Decontamination == null);
		}

		#endregion Operational vs protected

		#region Suggestions

		private static WellnessTestWorld WorldWithFlaggedCall(CallStates state, DateTime flaggedOn)
		{
			var world = new WellnessTestWorld();
			world.AddCall(500, state, Now.AddHours(-6));
			world.Statuses.Add(new ActionLog { UserId = Member, ActionTypeId = (int)ActionTypes.OnScene, Timestamp = Now.AddHours(-5), DepartmentId = DepartmentId });
			world.Statuses.Add(new ActionLog { UserId = Member, ActionTypeId = 0, Timestamp = Now.AddHours(-4), DepartmentId = DepartmentId });
			world.Statuses.Add(new ActionLog { UserId = Hidden, ActionTypeId = (int)ActionTypes.OnScene, Timestamp = Now.AddHours(-5), DepartmentId = DepartmentId });
			world.UnitStates.Add(new UnitState { UnitStateId = 1, UnitId = 3, Timestamp = Now.AddHours(-5.5), Roles = new List<UnitStateRole> { new UnitStateRole { UserId = Peer } } });
			world.UnitStates.Add(new UnitState { UnitStateId = 2, UnitId = 3, Timestamp = Now.AddHours(-4.5), Roles = new List<UnitStateRole>() });
			world.Flags.Add(new RmsCallExposureFlag { RmsCallExposureFlagId = "flag-1", DepartmentId = DepartmentId, CallId = 500, Category = (int)ResponderExposureCategories.CriticalIncident, FlaggedOn = flaggedOn, FlaggedByUserId = Officer });
			return world;
		}

		[Test]
		public async Task Post_call_suggestions_are_created_as_suggested_and_never_as_confirmed()
		{
			var world = WorldWithFlaggedCall(CallStates.Closed, Now.AddHours(-1));

			var result = await world.Service().ProcessPendingFlagsAsync(Now);

			result.SuggestionsCreated.Should().Be(2);
			world.Exposures.Should().HaveCount(2).And.OnlyContain(e => e.Status == (int)ResponderExposureStatuses.Suggested && e.ConfirmedOn == null && e.ConfirmedByUserId == null);
			world.Exposures.Select(e => e.UserId).Should().BeEquivalentTo(new[] { Member, Peer }, "the hidden member is not addressed by automation");
			world.Exposures.Single(e => e.UserId == Member).DurationMinutes.Should().Be(60);
			world.Exposures.Single(e => e.UserId == Peer).SuggestionBasis.Should().Contain(ResponderExposureSuggestionBases.UnitCrew);
			world.Flags[0].SuggestionCount.Should().Be(2);
			(await world.Service().GetOverviewAsync(DepartmentId, Member, Member)).CriticalIncidentCount.Should().Be(0, "a suggestion counts for nothing until it is confirmed");
			world.Sent.Select(s => s.UserId).Should().BeEquivalentTo(new[] { Member, Peer });
		}

		[Test]
		public async Task A_flag_on_a_call_still_being_worked_waits_until_the_call_ends_or_the_delay_passes()
		{
			var waiting = WorldWithFlaggedCall(CallStates.Active, Now.AddHours(-1));
			(await waiting.Service().ProcessPendingFlagsAsync(Now)).FlagsProcessed.Should().Be(0);
			waiting.Exposures.Should().BeEmpty();
			waiting.Flags[0].SuggestionsGeneratedOn.Should().BeNull();

			var late = WorldWithFlaggedCall(CallStates.Active, Now.AddHours(-13));
			(await late.Service().ProcessPendingFlagsAsync(Now)).SuggestionsCreated.Should().Be(2);
		}

		[Test]
		public async Task Only_the_member_or_a_wellness_officer_confirms_a_suggestion()
		{
			var world = WorldWithFlaggedCall(CallStates.Closed, Now.AddHours(-1));
			var service = world.Service();
			await service.ProcessPendingFlagsAsync(Now);
			var suggestion = world.Exposures.Single(e => e.UserId == Member);

			Func<Task> byPeer = () => service.ConfirmAsync(DepartmentId, Peer, suggestion.RmsResponderExposureId, null);
			await byPeer.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> byAdmin = () => service.ConfirmAsync(DepartmentId, Admin, suggestion.RmsResponderExposureId, null);
			await byAdmin.Should().ThrowAsync<UnauthorizedAccessException>();
			suggestion.Status.Should().Be((int)ResponderExposureStatuses.Suggested);

			var confirmed = await service.ConfirmAsync(DepartmentId, Member, suggestion.RmsResponderExposureId, null);
			confirmed.Status.Should().Be((int)ResponderExposureStatuses.Confirmed);
			confirmed.ConfirmedByUserId.Should().Be(Member);
			(await service.GetOverviewAsync(DepartmentId, Member, Member)).CriticalIncidentCount.Should().Be(1);
		}

		[Test]
		public async Task A_dismissed_suggestion_is_never_suggested_again_and_clearing_a_flag_removes_only_unconfirmed_suggestions()
		{
			var world = WorldWithFlaggedCall(CallStates.Closed, Now.AddHours(-1));
			var service = world.Service();
			await service.ProcessPendingFlagsAsync(Now);
			await service.DismissAsync(DepartmentId, Member, world.Exposures.Single(e => e.UserId == Member).RmsResponderExposureId);
			await service.ConfirmAsync(DepartmentId, Peer, world.Exposures.Single(e => e.UserId == Peer).RmsResponderExposureId, null);

			// Flagged again after a clear: suggestions are built afresh, but nobody who dismissed or confirmed is asked twice.
			await service.ClearCallFlagAsync(DepartmentId, Officer, "flag-1");
			world.Exposures.Should().HaveCount(2, "the dismissed and the confirmed entry both stay");
			await service.FlagCallAsync(DepartmentId, Officer, 500, (int)ResponderExposureCategories.CriticalIncident);
			(await service.ProcessPendingFlagsAsync(Now)).SuggestionsCreated.Should().Be(0);
		}

		[Test]
		public async Task Flagging_a_call_needs_the_wellness_role_administration_or_the_right_to_edit_the_call()
		{
			var world = new WellnessTestWorld();
			world.AddCall(600, CallStates.Closed, Now.AddHours(-3));
			var service = world.Service();

			Func<Task> byMember = () => service.FlagCallAsync(DepartmentId, Member, 600, (int)ResponderExposureCategories.CriticalIncident);
			await byMember.Should().ThrowAsync<UnauthorizedAccessException>();
			(await service.FlagCallAsync(DepartmentId, Admin, 600, (int)ResponderExposureCategories.CriticalIncident)).CallId.Should().Be(600);
			world.CanEditCall = (userId, callId) => userId == Peer && callId == 600;
			(await service.FlagCallAsync(DepartmentId, Peer, 600, (int)ResponderExposureCategories.Carcinogen)).Category.Should().Be((int)ResponderExposureCategories.Carcinogen);

			world.AddCall(601, CallStates.Closed, Now, OtherDepartmentId);
			Func<Task> foreign = () => service.FlagCallAsync(DepartmentId, Admin, 601, (int)ResponderExposureCategories.CriticalIncident);
			await foreign.Should().ThrowAsync<MemberWellnessException>().Where(e => e.Code == "call_not_found");
		}

		#endregion Suggestions

		#region Anniversary reminders

		private static RmsResponderExposure CriticalIncident(WellnessTestWorld world, string userId, DateTime occurredOn, bool remindMember, bool remindPeerTeam, DateTime? next)
		{
			var row = world.AddExposure(userId, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, occurredOn);
			row.RemindMember = remindMember;
			row.RemindPeerTeam = remindPeerTeam;
			row.NextAnniversaryOn = next;
			return row;
		}

		[Test]
		public async Task Anniversary_reminders_go_only_to_who_the_member_opted_in_and_are_claimed_before_sending()
		{
			var world = new WellnessTestWorld();
			var today = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
			var both = CriticalIncident(world, Member, new DateTime(2024, 11, 30, 20, 0, 0, DateTimeKind.Utc), true, true, new DateTime(2026, 11, 30));
			CriticalIncident(world, Peer, new DateTime(2025, 11, 28, 20, 0, 0, DateTimeKind.Utc), false, false, null);

			var sent = await world.Service().SendAnniversaryRemindersAsync(today);

			sent.Should().Be(2);
			world.Sent.Should().Contain(s => s.UserId == Member && s.Title == "Thinking of you");
			world.Sent.Should().Contain(s => s.UserId == Peer && s.Message.Contains("Mia Member"));
			world.Sent.Should().NotContain(s => s.Message.Contains("Diesel") || s.Message.Contains("Cough"), "a reminder never carries a protected value");
			both.NextAnniversaryOn.Should().Be(new DateTime(2027, 11, 30), "the claim moved the next anniversary on");

			world.Sent.Clear();
			(await world.Service().SendAnniversaryRemindersAsync(today.AddMinutes(15))).Should().Be(0, "a second run in the same window finds nothing due");
		}

		[Test]
		public async Task A_lost_claim_or_a_departed_member_sends_nothing_and_the_morning_hour_gates_the_pass()
		{
			var world = new WellnessTestWorld();
			var today = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
			CriticalIncident(world, Member, new DateTime(2024, 11, 30, 20, 0, 0, DateTimeKind.Utc), true, false, new DateTime(2026, 11, 30));
			CriticalIncident(world, Hidden, new DateTime(2024, 11, 29, 20, 0, 0, DateTimeKind.Utc), true, true, new DateTime(2026, 11, 29));

			(await world.Service().SendAnniversaryRemindersAsync(today.Date.AddHours(6))).Should().Be(0, "before 09:00 department-local nothing is sent");

			world.ClaimSucceeds = false;
			(await world.Service().SendAnniversaryRemindersAsync(today)).Should().Be(0);

			world.ClaimSucceeds = true;
			(await world.Service().SendAnniversaryRemindersAsync(today)).Should().Be(1);
			world.Sent.Should().ContainSingle(s => s.UserId == Member);
			world.Exposures.Single(e => e.UserId == Hidden).NextAnniversaryOn.Should().Be(new DateTime(2027, 11, 29), "claimed and moved on without telling anyone about a hidden member");
		}

		[Test]
		public async Task Only_the_member_opts_in_to_reminders_and_only_for_a_confirmed_critical_incident()
		{
			var world = new WellnessTestWorld();
			var critical = world.AddExposure(Member, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, Now.AddDays(-400));
			var carcinogen = world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-400));
			var service = world.Service();

			Func<Task> byOfficer = () => service.SetRemindersAsync(DepartmentId, Officer, critical.RmsResponderExposureId, true, true);
			await byOfficer.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> wrongCategory = () => service.SetRemindersAsync(DepartmentId, Member, carcinogen.RmsResponderExposureId, true, false);
			await wrongCategory.Should().ThrowAsync<MemberWellnessException>().Where(e => e.Code == "reminders_critical_incident_only");

			var saved = await service.SetRemindersAsync(DepartmentId, Member, critical.RmsResponderExposureId, true, false);
			saved.NextAnniversaryOn.Should().NotBeNull();
			saved.NextAnniversaryOn.Value.Should().BeOnOrAfter(Now.Date);

			saved = await service.SetRemindersAsync(DepartmentId, Member, critical.RmsResponderExposureId, false, false);
			saved.NextAnniversaryOn.Should().BeNull();
		}

		#endregion Anniversary reminders

		#region Retention and legal hold

		[Test]
		public async Task Entries_are_kept_at_least_30_years_and_a_legal_hold_keeps_them_longer()
		{
			var world = new WellnessTestWorld { Settings = new WellnessSettings { RetentionYears = 1 } };
			var recent = world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddYears(-29));
			var old = world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddYears(-31));
			var held = world.AddExposure(Peer, ResponderExposureCategories.CriticalIncident, ResponderExposureStatuses.Confirmed, Now.AddYears(-32));
			world.Holds.Add(new RmsRecordLegalHold { RmsRecordLegalHoldId = "hold", DepartmentId = DepartmentId, RecordId = held.RmsResponderExposureId });

			var erased = await world.Service().EraseExpiredAsync(Now);

			erased.Should().Be(1);
			world.Exposures.Should().Contain(recent, "29 years is inside the 30-year minimum even though the department asked for 1");
			world.Exposures.Should().Contain(held, "a legal hold keeps an entry past its retention");
			world.Exposures.Should().NotContain(old);
		}

		[Test]
		public async Task A_department_wide_hold_keeps_every_entry_and_a_longer_setting_keeps_older_ones()
		{
			var world = new WellnessTestWorld { Settings = new WellnessSettings { RetentionYears = 40 } };
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddYears(-35));
			(await world.Service().EraseExpiredAsync(Now)).Should().Be(0, "the department keeps entries 40 years");

			world.Settings = new WellnessSettings();
			world.Holds.Add(new RmsRecordLegalHold { RmsRecordLegalHoldId = "dept", DepartmentId = DepartmentId });
			(await world.Service().EraseExpiredAsync(Now)).Should().Be(0, "a department-wide hold covers exposure entries");

			world.Holds[0].ReleasedOn = Now;
			(await world.Service().EraseExpiredAsync(Now)).Should().Be(1);
		}

		[Test]
		public void Retention_and_anniversary_rules()
		{
			MemberWellnessRules.RetentionYears(new WellnessSettings { RetentionYears = 3 }).Should().Be(30);
			MemberWellnessRules.RetentionYears(new WellnessSettings { RetentionYears = 500 }).Should().Be(100);
			MemberWellnessRules.NextAnniversary(new DateTime(2024, 2, 29), new DateTime(2025, 1, 1)).Should().Be(new DateTime(2025, 2, 28));
			MemberWellnessRules.NextAnniversary(new DateTime(2024, 6, 1), new DateTime(2024, 6, 2)).Should().Be(new DateTime(2025, 6, 1), "the first anniversary is a year on");
			MemberWellnessRules.FollowingAnniversary(new DateTime(2024, 6, 1), new DateTime(2025, 6, 1)).Should().Be(new DateTime(2026, 6, 1));
			MemberWellnessRules.IsReminderDue(new DateTime(2026, 6, 1), new DateTime(2026, 5, 25)).Should().BeTrue();
			MemberWellnessRules.IsReminderDue(new DateTime(2026, 6, 1), new DateTime(2026, 5, 24)).Should().BeFalse();
		}

		#endregion Retention and legal hold

		#region Export

		[Test]
		public async Task The_export_contains_only_the_members_own_rows()
		{
			var world = new WellnessTestWorld();
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-3));
			world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Dismissed, Now.AddDays(-2));
			world.AddExposure(Peer, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Now.AddDays(-1), substance: "PEER-ONLY");
			// A repository that ever returned a foreign row must not leak it into the member's export.
			world.ExposuresRepository.Setup(r => r.GetForUserAsync(DepartmentId, Member)).ReturnsAsync(world.Exposures.ToList());
			var service = world.Service();

			var own = await service.GetOwnEntriesForExportAsync(DepartmentId, Member);
			own.Should().ContainSingle().Which.UserId.Should().Be(Member);
			world.Audits.Should().Contain(a => a.Type == AuditLogTypes.ResponderExposuresExported && a.UserId == Member);

			var gdpr = JsonConvert.SerializeObject(await service.BuildGdprExportAsync(Member, DepartmentId));
			gdpr.Should().NotContain("PEER-ONLY").And.Contain("Diesel exhaust");
		}

		#endregion Export

		#region Screening and follow-ups

		[Test]
		public async Task Screening_follow_ups_are_records_work_assignments_only_the_wellness_service_reaches()
		{
			var world = new WellnessTestWorld();
			var service = world.Service();

			Func<Task> byAdmin = () => service.SetScreeningFlagAsync(DepartmentId, Admin, Member, null, null, "Annual low-dose CT");
			await byAdmin.Should().ThrowAsync<UnauthorizedAccessException>();

			var flag = await service.SetScreeningFlagAsync(DepartmentId, Officer, Member, (int)ResponderExposureCategories.Carcinogen, Now.AddMonths(6), "Annual low-dose CT");
			var task = await service.AddFollowUpAsync(DepartmentId, Officer, flag.RmsResponderScreeningFlagId, ResponderFollowUpTaskKinds.ScreeningAppointment, null, Now.AddMonths(1));

			task.Purpose.Should().Be(RmsWorkAssignmentPurposes.WellnessFollowUp);
			task.RecordId.Should().Be(flag.RmsResponderScreeningFlagId);
			task.AssigneeUserId.Should().Be(Member);
			task.Note.Should().Be(ResponderFollowUpTaskKinds.ScreeningAppointment, "the plaintext note holds only the kind code");
			RmsWorkAssignmentPurposes.IsKnown(RmsWorkAssignmentPurposes.WellnessFollowUp).Should().BeFalse("the generic Records assignment path cannot create or reach it");
			world.Audits.Should().NotContain(a => (a.After ?? string.Empty).Contains("low-dose"));

			Func<Task> toPeer = () => service.AddFollowUpAsync(DepartmentId, Officer, flag.RmsResponderScreeningFlagId, ResponderFollowUpTaskKinds.Other, Peer, null);
			await toPeer.Should().ThrowAsync<MemberWellnessException>().Where(e => e.Code == "follow_up_assignee_invalid");

			Func<Task> byPeer = () => service.CompleteFollowUpAsync(DepartmentId, Peer, task.RmsRecordWorkAssignmentId);
			await byPeer.Should().ThrowAsync<UnauthorizedAccessException>();
			(await service.CompleteFollowUpAsync(DepartmentId, Member, task.RmsRecordWorkAssignmentId)).State.Should().Be((int)RmsWorkAssignmentState.Completed);

			var overview = await service.GetOverviewAsync(DepartmentId, Member, Member);
			overview.ScreeningFlag.Should().NotBeNull();
			overview.FollowUps.Should().ContainSingle();
		}

		[Test]
		public async Task The_wellness_service_never_completes_a_records_own_work_assignment()
		{
			var world = new WellnessTestWorld();
			world.Assignments.Add(new RmsRecordWorkAssignment { RmsRecordWorkAssignmentId = "records-task", DepartmentId = DepartmentId, RecordId = "record-1", Purpose = RmsWorkAssignmentPurposes.Review, AssigneeUserId = Member, State = (int)RmsWorkAssignmentState.Open });

			Func<Task> complete = () => world.Service().CompleteFollowUpAsync(DepartmentId, Member, "records-task");
			await complete.Should().ThrowAsync<MemberWellnessException>().Where(e => e.Code == "follow_up_not_found");
		}

		#endregion Screening and follow-ups
	}
}
