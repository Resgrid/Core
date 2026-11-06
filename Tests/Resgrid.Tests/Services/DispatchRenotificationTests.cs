using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Departments;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class DispatchRenotificationServiceTests
	{
		private const int DepartmentId = 7;
		private const int CallId = 501;
		private const string UserId = "user-1";
		private const int UnitId = 31;

		private Mock<IDispatchRenotificationsRepository> _repository;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentsService> _departments;
		private Mock<ICallsService> _calls;
		private Mock<ICommunicationService> _communication;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<IUnitStatesRepository> _unitStates;
		private DispatchRenotificationService _service;
		private DispatchRenotifyConfig _config;
		private Call _call;
		private List<DispatchRenotification> _registered;
		private DateTime _now;
		private bool _timingLocked;
		private bool _channelsLocked;
		private bool _renotifyEnabled;

		[SetUp]
		public void SetUp()
		{
			_timingLocked = DispatchConfig.RenotifyTimingLocked;
			_channelsLocked = DispatchConfig.RenotifyChannelsLocked;
			_renotifyEnabled = DispatchConfig.RenotifyEnabled;
			DispatchConfig.RenotifyTimingLocked = true;
			DispatchConfig.RenotifyChannelsLocked = true;
			DispatchConfig.RenotifyEnabled = true;

			_now = new DateTime(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc);
			_config = new DispatchRenotifyConfig { Enabled = true };
			_call = new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				Name = "Structure Fire",
				State = (int)CallStates.Active,
				Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = UserId } },
				GroupDispatches = new List<CallDispatchGroup>(),
				RoleDispatches = new List<CallDispatchRole>(),
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = CallId, UnitId = UnitId } }
			};

			_repository = new Mock<IDispatchRenotificationsRepository>();
			_registered = new List<DispatchRenotification>();
			_repository.Setup(r => r.RegisterAsync(It.IsAny<IEnumerable<DispatchRenotification>>(), It.IsAny<CancellationToken>()))
				.Callback((IEnumerable<DispatchRenotification> rows, CancellationToken ct) => _registered.AddRange(rows))
				.Returns(Task.CompletedTask);
			_repository.Setup(r => r.TryClaimAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_repository.Setup(r => r.MarkAcknowledgedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_repository.Setup(r => r.CancelAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((IEnumerable<string> ids, DateTime now, CancellationToken ct) => ids.Count());

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetDispatchRenotifyConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _config);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "ABCD" });

			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool a, bool b, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k) => c);

			_communication = new Mock<ICommunicationService>();
			_communication.Setup(c => c.SendCallRenotificationAsync(It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<DispatchRenotifyChannels>(), It.IsAny<UserProfile>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
			_communication.Setup(c => c.SendUnitCallRenotificationAsync(It.IsAny<Call>(), It.IsAny<CallDispatchUnit>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<DispatchRenotifyChannels>(), It.IsAny<string>())).ReturnsAsync(true);

			_actionLogs = new Mock<IActionLogsService>();
			_actionLogs.Setup(a => a.GetAllActionLogsForUserInDateRangeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<ActionLog>());

			_unitStates = new Mock<IUnitStatesRepository>();
			_unitStates.Setup(u => u.GetAllUnitStatesForUnitInDateRangeAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<UnitState>());

			_service = new DispatchRenotificationService(_repository.Object, _settings.Object, _departments.Object, _calls.Object, _communication.Object,
				_actionLogs.Object, _unitStates.Object);
		}

		[TearDown]
		public void TearDown()
		{
			DispatchConfig.RenotifyTimingLocked = _timingLocked;
			DispatchConfig.RenotifyChannelsLocked = _channelsLocked;
			DispatchConfig.RenotifyEnabled = _renotifyEnabled;
		}

		private DispatchRenotification UserRow(int attempts = 0, DispatchRenotifySourceTypes source = DispatchRenotifySourceTypes.User, string sourceId = UserId) => new DispatchRenotification
		{
			DispatchRenotificationId = "row-user",
			DepartmentId = DepartmentId,
			CallId = CallId,
			RecipientType = (int)DispatchRenotifyRecipientTypes.User,
			RecipientId = UserId,
			SourceType = (int)source,
			SourceId = sourceId,
			Attempts = attempts,
			AcknowledgeAfter = _now.AddMinutes(-5 * (attempts + 1)),
			NextNotifyOn = _now
		};

		private DispatchRenotification UnitRow(int attempts = 0) => new DispatchRenotification
		{
			DispatchRenotificationId = "row-unit",
			DepartmentId = DepartmentId,
			CallId = CallId,
			RecipientType = (int)DispatchRenotifyRecipientTypes.Unit,
			RecipientId = UnitId.ToString(),
			SourceType = (int)DispatchRenotifySourceTypes.Unit,
			SourceId = UnitId.ToString(),
			Attempts = attempts,
			AcknowledgeAfter = _now.AddMinutes(-5 * (attempts + 1)),
			NextNotifyOn = _now
		};

		private void Due(params DispatchRenotification[] rows)
		{
			_repository.Setup(r => r.GetDueAsync(_now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(rows.ToList());
		}

		private void UserSetStatus(DateTime timestamp, StatusSetOrigins? origin, int actionType = (int)ActionTypes.NotResponding, int departmentId = DepartmentId)
		{
			_actionLogs.Setup(a => a.GetAllActionLogsForUserInDateRangeAsync(UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<ActionLog>
				{
					new ActionLog { UserId = UserId, DepartmentId = departmentId, ActionTypeId = actionType, Timestamp = timestamp, SetByOrigin = (int?)origin }
				});
		}

		private void VerifyNothingSent()
		{
			_communication.Verify(c => c.SendCallRenotificationAsync(It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<DispatchRenotifyChannels>(), It.IsAny<UserProfile>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_communication.Verify(c => c.SendUnitCallRenotificationAsync(It.IsAny<Call>(), It.IsAny<CallDispatchUnit>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<DispatchRenotifyChannels>(), It.IsAny<string>()), Times.Never);
		}

		#region Registering a broadcast

		[Test]
		public async Task A_department_that_has_not_turned_it_on_registers_nothing()
		{
			_config = new DispatchRenotifyConfig();

			await _service.RegisterBroadcastAsync(_call, new[] { DispatchRenotifyRecipient.ForUser(UserId, DispatchRenotifySourceTypes.User, UserId) }, _now);

			_repository.Verify(r => r.RegisterAsync(It.IsAny<IEnumerable<DispatchRenotification>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void The_setting_is_off_by_default()
		{
			new DispatchRenotifyConfig().Enabled.Should().BeFalse();
		}

		[Test]
		public async Task A_broadcast_registers_each_person_and_unit_once_first_due_one_interval_after_it_went_out()
		{
			var dispatchedOn = DateTime.UtcNow.AddSeconds(-20);

			await _service.RegisterBroadcastAsync(_call, new[]
			{
				DispatchRenotifyRecipient.ForUser(UserId, DispatchRenotifySourceTypes.User, UserId),
				DispatchRenotifyRecipient.ForUser(UserId.ToUpperInvariant(), DispatchRenotifySourceTypes.Group, "4"),
				DispatchRenotifyRecipient.ForUser("user-2", DispatchRenotifySourceTypes.Role, "9"),
				DispatchRenotifyRecipient.ForUnit(UnitId)
			}, dispatchedOn);

			_registered.Should().HaveCount(3, "a person reached by two routes is chased once");
			_registered.Should().OnlyContain(r => r.CallId == CallId && r.DepartmentId == DepartmentId && r.Status == (int)DispatchRenotifyStatuses.Pending
				&& r.Attempts == 0 && r.AcknowledgeAfter == dispatchedOn);
			_registered.Select(r => r.NextNotifyOn.Value).Should().OnlyContain(t => t > DateTime.UtcNow.AddMinutes(4.9) && t <= DateTime.UtcNow.AddMinutes(5));
			_registered.Single(r => r.RecipientType == (int)DispatchRenotifyRecipientTypes.Unit).RecipientId.Should().Be(UnitId.ToString());
			_registered.Single(r => r.RecipientId == "user-2").SourceType.Should().Be((int)DispatchRenotifySourceTypes.Role);
		}

		[Test]
		public async Task While_the_timing_is_locked_a_stored_interval_is_ignored()
		{
			_config = new DispatchRenotifyConfig { Enabled = true, IntervalMinutes = 30, MaxRetries = 8 };

			await _service.RegisterBroadcastAsync(_call, new[] { DispatchRenotifyRecipient.ForUnit(UnitId) }, DateTime.UtcNow);

			_registered.Single().NextNotifyOn.Value.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(10));
		}

		[Test]
		public async Task A_closed_call_or_the_system_switch_turned_off_registers_nothing()
		{
			DispatchConfig.RenotifyEnabled = false;
			await _service.RegisterBroadcastAsync(_call, new[] { DispatchRenotifyRecipient.ForUnit(UnitId) }, _now);

			DispatchConfig.RenotifyEnabled = true;
			_call.State = (int)CallStates.Closed;
			await _service.RegisterBroadcastAsync(_call, new[] { DispatchRenotifyRecipient.ForUnit(UnitId) }, _now);

			_repository.Verify(r => r.RegisterAsync(It.IsAny<IEnumerable<DispatchRenotification>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Registering a broadcast

		#region Acknowledgment

		[TestCase(ActionTypes.NotResponding)]
		[TestCase(ActionTypes.Responding)]
		[TestCase(ActionTypes.StandingBy)]
		public async Task Any_status_the_member_sets_after_the_dispatch_acknowledges_it(ActionTypes action)
		{
			var acknowledged = _now.AddMinutes(-2);
			UserSetStatus(acknowledged, StatusSetOrigins.ResponderApp, (int)action);
			Due(UserRow());

			var result = await _service.ProcessDueAsync(_now);

			result.Acknowledged.Should().Be(1);
			_repository.Verify(r => r.MarkAcknowledgedAsync("row-user", acknowledged, _now, It.IsAny<CancellationToken>()), Times.Once);
			VerifyNothingSent();
		}

		[TestCase(StatusSetOrigins.DispatchAutomation)]
		[TestCase(StatusSetOrigins.Schedule)]
		[TestCase(StatusSetOrigins.Maintenance)]
		public async Task A_status_Resgrid_applied_itself_is_not_an_acknowledgment(StatusSetOrigins origin)
		{
			UserSetStatus(_now.AddMinutes(-4), origin, (int)ActionTypes.RespondingToScene);
			Due(UserRow());

			var result = await _service.ProcessDueAsync(_now);

			result.Acknowledged.Should().Be(0);
			result.Renotified.Should().Be(1);
			_communication.Verify(c => c.SendCallRenotificationAsync(_call, UserId, 1, 3, DispatchRenotifyChannels.Push, null, null, null), Times.Once);
		}

		[Test]
		public async Task A_status_from_before_origins_were_recorded_counts_as_the_members_own()
		{
			UserSetStatus(_now.AddMinutes(-1), null);
			Due(UserRow());

			(await _service.ProcessDueAsync(_now)).Acknowledged.Should().Be(1);
		}

		[Test]
		public async Task A_status_set_in_another_department_does_not_acknowledge_this_dispatch()
		{
			UserSetStatus(_now.AddMinutes(-1), StatusSetOrigins.ResponderApp, departmentId: DepartmentId + 1);
			Due(UserRow());

			(await _service.ProcessDueAsync(_now)).Renotified.Should().Be(1);
		}

		[Test]
		public async Task A_status_from_before_the_dispatch_does_not_acknowledge_it()
		{
			var row = UserRow();
			UserSetStatus(row.AcknowledgeAfter.AddSeconds(-30), StatusSetOrigins.ResponderApp);
			Due(row);

			(await _service.ProcessDueAsync(_now)).Renotified.Should().Be(1);
		}

		[Test]
		public async Task A_unit_that_responds_without_a_destination_has_acknowledged()
		{
			var acknowledged = _now.AddMinutes(-3);
			_unitStates.Setup(u => u.GetAllUnitStatesForUnitInDateRangeAsync(UnitId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<UnitState>
				{
					new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.Responding, Timestamp = acknowledged, DestinationId = null, SetByOrigin = (int)StatusSetOrigins.UnitApp }
				});
			Due(UnitRow());

			var result = await _service.ProcessDueAsync(_now);

			result.Acknowledged.Should().Be(1);
			_repository.Verify(r => r.MarkAcknowledgedAsync("row-unit", acknowledged, _now, It.IsAny<CancellationToken>()), Times.Once);
			VerifyNothingSent();
		}

		[Test]
		public async Task The_dispatch_status_applied_to_a_unit_is_not_its_acknowledgment()
		{
			_unitStates.Setup(u => u.GetAllUnitStatesForUnitInDateRangeAsync(UnitId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<UnitState>
				{
					new UnitState { UnitId = UnitId, State = (int)UnitStateTypes.Responding, Timestamp = _now.AddMinutes(-4), SetByOrigin = (int)StatusSetOrigins.DispatchAutomation }
				});
			Due(UnitRow());

			var result = await _service.ProcessDueAsync(_now);

			result.Renotified.Should().Be(1);
			_communication.Verify(c => c.SendUnitCallRenotificationAsync(_call, It.Is<CallDispatchUnit>(d => d.UnitId == UnitId), 1, 3, DispatchRenotifyChannels.Push, null), Times.Once);
		}

		#endregion Acknowledgment

		#region Attempts

		[Test]
		public async Task An_unacknowledged_member_is_re_notified_and_due_again_one_interval_later()
		{
			Due(UserRow());

			var result = await _service.ProcessDueAsync(_now);

			result.Renotified.Should().Be(1);
			result.Exhausted.Should().Be(0);
			_repository.Verify(r => r.TryClaimAttemptAsync("row-user", 0, 1, _now.AddMinutes(5), _now, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_third_re_notification_is_the_last()
		{
			Due(UserRow(attempts: 2));

			var result = await _service.ProcessDueAsync(_now);

			result.Exhausted.Should().Be(1);
			_repository.Verify(r => r.TryClaimAttemptAsync("row-user", 2, 3, null, _now, It.IsAny<CancellationToken>()), Times.Once);
			_communication.Verify(c => c.SendCallRenotificationAsync(_call, UserId, 3, 3, DispatchRenotifyChannels.Push, null, null, null), Times.Once);
		}

		[Test]
		public async Task An_attempt_another_sweep_claimed_is_not_sent_twice()
		{
			_repository.Setup(r => r.TryClaimAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);
			Due(UserRow(), UnitRow());

			(await _service.ProcessDueAsync(_now)).Renotified.Should().Be(0);
			VerifyNothingSent();
		}

		[Test]
		public async Task While_the_channels_are_locked_push_is_the_only_channel_sent_whatever_is_stored()
		{
			_config = new DispatchRenotifyConfig { Enabled = true, Channels = (int)(DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice | DispatchRenotifyChannels.Email) };
			Due(UserRow());

			await _service.ProcessDueAsync(_now);

			_communication.Verify(c => c.SendCallRenotificationAsync(_call, UserId, 1, 3, DispatchRenotifyChannels.Push, null, null, null), Times.Once);
			_settings.Verify(s => s.GetTextToCallNumberForDepartmentAsync(It.IsAny<int>()), Times.Never, "no text or voice channel is used, so no number is needed");
		}

		[Test]
		public async Task Once_the_channels_are_unlocked_the_stored_channels_are_sent_from_the_departments_number()
		{
			DispatchConfig.RenotifyChannelsLocked = false;
			var stored = DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice | DispatchRenotifyChannels.ChatPlatform;
			_config = new DispatchRenotifyConfig { Enabled = true, Channels = (int)stored };
			_settings.Setup(s => s.GetTextToCallNumberForDepartmentAsync(DepartmentId)).ReturnsAsync("+15555550100");
			Due(UserRow(), UnitRow());

			await _service.ProcessDueAsync(_now);

			_communication.Verify(c => c.SendCallRenotificationAsync(_call, UserId, 1, 3, stored, null, null, "+15555550100"), Times.Once);
			_communication.Verify(c => c.SendUnitCallRenotificationAsync(_call, It.Is<CallDispatchUnit>(d => d.UnitId == UnitId), 1, 3, stored, null), Times.Once,
				"the sender keeps a unit to push");
		}

		#endregion Attempts

		#region Cancellation

		[TestCase(CallStates.Closed)]
		[TestCase(CallStates.Cancelled)]
		public async Task A_call_that_is_no_longer_active_cancels_its_rows(CallStates state)
		{
			_call.State = (int)state;
			Due(UserRow(), UnitRow());

			var result = await _service.ProcessDueAsync(_now);

			result.Cancelled.Should().Be(2);
			_repository.Verify(r => r.CancelAsync(It.Is<IEnumerable<string>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { "row-unit", "row-user" })), _now, It.IsAny<CancellationToken>()), Times.Once);
			VerifyNothingSent();
		}

		[Test]
		public async Task Turning_it_off_cancels_the_reminders_already_running()
		{
			_config = new DispatchRenotifyConfig { Enabled = false };
			Due(UserRow());

			(await _service.ProcessDueAsync(_now)).Cancelled.Should().Be(1);
			VerifyNothingSent();
		}

		[Test]
		public async Task A_member_taken_off_the_call_is_no_longer_chased()
		{
			_call.Dispatches.Clear();
			Due(UserRow());

			(await _service.ProcessDueAsync(_now)).Cancelled.Should().Be(1);
			VerifyNothingSent();
		}

		[Test]
		public async Task A_unit_taken_off_the_call_is_no_longer_chased()
		{
			_call.UnitDispatches.Clear();
			Due(UnitRow());

			(await _service.ProcessDueAsync(_now)).Cancelled.Should().Be(1);
			VerifyNothingSent();
		}

		[Test]
		public async Task A_row_reached_long_after_it_was_due_is_dropped_rather_than_sent_late()
		{
			var row = UserRow();
			row.NextNotifyOn = _now.AddMinutes(-11);
			Due(row);

			(await _service.ProcessDueAsync(_now)).Cancelled.Should().Be(1);
			VerifyNothingSent();
		}

		[Test]
		public void A_member_reached_through_a_group_stays_on_the_call_while_the_group_is_dispatched()
		{
			_call.Dispatches.Clear();
			var row = UserRow(source: DispatchRenotifySourceTypes.Group, sourceId: "4");

			DispatchRenotificationService.IsStillDispatched(_call, row).Should().BeFalse();

			_call.GroupDispatches.Add(new CallDispatchGroup { CallId = CallId, DepartmentGroupId = 4 });
			DispatchRenotificationService.IsStillDispatched(_call, row).Should().BeTrue();
		}

		[Test]
		public void A_member_reached_through_a_unit_crew_stays_on_the_call_while_the_unit_is_dispatched()
		{
			_call.Dispatches.Clear();
			var row = UserRow(source: DispatchRenotifySourceTypes.UnitCrew, sourceId: UnitId.ToString());

			DispatchRenotificationService.IsStillDispatched(_call, row).Should().BeTrue();

			_call.UnitDispatches.Clear();
			DispatchRenotificationService.IsStillDispatched(_call, row).Should().BeFalse();
		}

		#endregion Cancellation

		[Test]
		public async Task The_sweep_purges_finished_rows_past_their_retention()
		{
			Due();

			await _service.ProcessDueAsync(_now);

			_repository.Verify(r => r.PurgeCompletedAsync(_now.AddDays(-DispatchConfig.RenotifyRetentionDays), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
		}
	}

	[TestFixture]
	public class DispatchRenotifyConfigTests
	{
		[Test]
		public void Locked_timing_is_five_minutes_three_times_whatever_is_stored()
		{
			var config = new DispatchRenotifyConfig { IntervalMinutes = 1, MaxRetries = 10 };

			config.GetEffectiveIntervalMinutes(true).Should().Be(5);
			config.GetEffectiveMaxRetries(true).Should().Be(3);
		}

		[TestCase(0, 5)]
		[TestCase(-3, 5)]
		[TestCase(2, 2)]
		[TestCase(500, DispatchRenotifyConfig.MaximumIntervalMinutes)]
		public void Unlocked_interval_is_clamped(int stored, int expected)
		{
			new DispatchRenotifyConfig { IntervalMinutes = stored }.GetEffectiveIntervalMinutes(false).Should().Be(expected);
		}

		[TestCase(0, 3)]
		[TestCase(1, 1)]
		[TestCase(99, DispatchRenotifyConfig.MaximumMaxRetries)]
		public void Unlocked_retries_are_clamped(int stored, int expected)
		{
			new DispatchRenotifyConfig { MaxRetries = stored }.GetEffectiveMaxRetries(false).Should().Be(expected);
		}

		[TestCase(DispatchRenotifyChannels.Push)]
		[TestCase(DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Email)]
		[TestCase(DispatchRenotifyChannels.None)]
		[TestCase(DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Voice | DispatchRenotifyChannels.ChatPlatform)]
		public void Locked_channels_are_push_only_whatever_is_stored(DispatchRenotifyChannels stored)
		{
			new DispatchRenotifyConfig { Channels = (int)stored }.GetEffectiveChannels(true).Should().Be(DispatchRenotifyChannels.Push);
		}

		[TestCase(DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Email, DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Email)]
		[TestCase(DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Voice | DispatchRenotifyChannels.ChatPlatform, DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Voice | DispatchRenotifyChannels.ChatPlatform)]
		[TestCase(DispatchRenotifyChannels.None, DispatchRenotifyChannels.Push)]
		[TestCase((DispatchRenotifyChannels)(64 | 4), DispatchRenotifyChannels.Email)]
		public void Unlocked_channels_are_the_stored_ones(DispatchRenotifyChannels stored, DispatchRenotifyChannels expected)
		{
			new DispatchRenotifyConfig { Channels = (int)stored }.GetEffectiveChannels(false).Should().Be(expected);
		}

		[Test]
		public void Every_channel_is_supported_and_a_unit_has_push_only()
		{
			DispatchRenotifyConfig.AllChannels.Should().Equal(
				DispatchRenotifyChannels.Push, DispatchRenotifyChannels.Sms, DispatchRenotifyChannels.Email, DispatchRenotifyChannels.Voice, DispatchRenotifyChannels.ChatPlatform);
			DispatchRenotifyConfig.SupportedChannels.Should().Be((DispatchRenotifyChannels)31);
			DispatchRenotifyConfig.UnitChannels.Should().Be(DispatchRenotifyChannels.Push);
		}

		[Test]
		public void Applying_the_locks_stores_the_defaults_for_everything_locked()
		{
			var requested = new DispatchRenotifyConfig { Enabled = true, IntervalMinutes = 2, MaxRetries = 9, Channels = (int)(DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice) };

			var locked = requested.ApplyLocks(true, true);
			locked.Should().BeEquivalentTo(new DispatchRenotifyConfig { Enabled = true, IntervalMinutes = 5, MaxRetries = 3, Channels = (int)DispatchRenotifyChannels.Push });

			var unlocked = requested.ApplyLocks(false, false);
			unlocked.Should().BeEquivalentTo(new DispatchRenotifyConfig { Enabled = true, IntervalMinutes = 2, MaxRetries = 9, Channels = (int)(DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice) });

			requested.ApplyLocks(false, true).Channels.Should().Be((int)DispatchRenotifyChannels.Push, "the channel lock is independent of the timing lock");
			requested.ApplyLocks(true, false).Channels.Should().Be((int)(DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice));
		}

		[Test]
		public void Posted_channel_values_become_flags_and_unknown_values_are_dropped()
		{
			DispatchRenotifyConfig.ChannelsFrom(new[] { 1, 2, 8 }).Should().Be((int)(DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice));
			DispatchRenotifyConfig.ChannelsFrom(new[] { -1, 0, 3, 31, 64 }).Should().Be(0, "only a single listed channel's value counts");
			DispatchRenotifyConfig.ChannelsFrom(null).Should().Be(0);
		}

		[Test]
		public void The_stored_setting_round_trips()
		{
			var stored = ObjectSerialization.Serialize(new DispatchRenotifyConfig { Enabled = true, IntervalMinutes = 7, MaxRetries = 4, Channels = (int)(DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Sms) });
			var read = ObjectSerialization.Deserialize<DispatchRenotifyConfig>(stored);

			read.Enabled.Should().BeTrue();
			read.IntervalMinutes.Should().Be(7);
			read.MaxRetries.Should().Be(4);
			read.Channels.Should().Be((int)(DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Sms));

			var off = ObjectSerialization.Deserialize<DispatchRenotifyConfig>(ObjectSerialization.Serialize(new DispatchRenotifyConfig()));
			off.Enabled.Should().BeFalse();
			off.GetEffectiveIntervalMinutes(true).Should().Be(5);
			off.GetEffectiveMaxRetries(true).Should().Be(3);
		}

		[Test]
		public void The_setting_type_is_124()
		{
			((int)DepartmentSettingTypes.DispatchRenotifyConfig).Should().Be(124);
		}
	}

	/// <summary>The last line of defence: whatever a caller hands the settings service, a locked option is stored at its default.</summary>
	[TestFixture]
	[NonParallelizable]
	public class DispatchRenotifySettingsServiceTests
	{
		private Mock<IDepartmentSettingsRepository> _repository;
		private DepartmentSettingsService _service;
		private DepartmentSetting _saved;
		private bool _timingLocked;
		private bool _channelsLocked;

		[SetUp]
		public void SetUp()
		{
			_timingLocked = DispatchConfig.RenotifyTimingLocked;
			_channelsLocked = DispatchConfig.RenotifyChannelsLocked;
			_repository = new Mock<IDepartmentSettingsRepository>();
			_repository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((DepartmentSetting setting, CancellationToken ct, bool firstLevelOnly) => _saved = setting)
				.ReturnsAsync((DepartmentSetting setting, CancellationToken ct, bool firstLevelOnly) => setting);
			_service = new DepartmentSettingsService(_repository.Object, Mock.Of<IAddressService>(), Mock.Of<Resgrid.Model.Providers.IGeoLocationProvider>(),
				Mock.Of<Resgrid.Model.Providers.ICacheProvider>());
		}

		[TearDown]
		public void TearDown()
		{
			DispatchConfig.RenotifyTimingLocked = _timingLocked;
			DispatchConfig.RenotifyChannelsLocked = _channelsLocked;
		}

		private static DispatchRenotifyConfig Requested() => new DispatchRenotifyConfig
		{
			Enabled = true, IntervalMinutes = 2, MaxRetries = 9, Channels = (int)(DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice)
		};

		[Test]
		public async Task Saving_while_locked_stores_push_only_and_the_default_timing()
		{
			DispatchConfig.RenotifyTimingLocked = true;
			DispatchConfig.RenotifyChannelsLocked = true;

			await _service.SetDispatchRenotifyConfigAsync(7, Requested());

			_saved.SettingType.Should().Be((int)DepartmentSettingTypes.DispatchRenotifyConfig);
			ObjectSerialization.Deserialize<DispatchRenotifyConfig>(_saved.Setting).Should()
				.BeEquivalentTo(new DispatchRenotifyConfig { Enabled = true, IntervalMinutes = 5, MaxRetries = 3, Channels = (int)DispatchRenotifyChannels.Push });
		}

		[Test]
		public async Task Saving_once_unlocked_stores_what_was_asked_for()
		{
			DispatchConfig.RenotifyTimingLocked = false;
			DispatchConfig.RenotifyChannelsLocked = false;

			await _service.SetDispatchRenotifyConfigAsync(7, Requested());

			ObjectSerialization.Deserialize<DispatchRenotifyConfig>(_saved.Setting).Should().BeEquivalentTo(Requested());
		}
	}

	[TestFixture]
	public class DispatchRenotifySettingsPageTests
	{
		private static DispatchSettingsView CraftedPost() => new DispatchSettingsView
		{
			RenotifyEnabled = true,
			RenotifyIntervalMinutes = 1,
			RenotifyMaxRetries = 10,
			RenotifyChannelValues = new List<int> { (int)DispatchRenotifyChannels.Sms, (int)DispatchRenotifyChannels.Email, (int)DispatchRenotifyChannels.Voice, (int)DispatchRenotifyChannels.ChatPlatform }
		};

		[Test]
		public void A_locked_save_forces_the_default_timing_and_every_channel_but_push_off_whatever_is_posted()
		{
			var saved = DepartmentController.BuildDispatchRenotifyConfig(CraftedPost(), true, true);

			saved.Enabled.Should().BeTrue("turning re-notification on is not locked");
			saved.IntervalMinutes.Should().Be(DispatchRenotifyConfig.DefaultIntervalMinutes);
			saved.MaxRetries.Should().Be(DispatchRenotifyConfig.DefaultMaxRetries);
			saved.Channels.Should().Be((int)DispatchRenotifyChannels.Push);
		}

		[Test]
		public void An_unlocked_save_takes_the_posted_timing_within_limits_and_the_ticked_channels()
		{
			var posted = CraftedPost();
			posted.RenotifyIntervalMinutes = 500;
			posted.RenotifyMaxRetries = 2;

			var saved = DepartmentController.BuildDispatchRenotifyConfig(posted, false, false);

			saved.IntervalMinutes.Should().Be(DispatchRenotifyConfig.MaximumIntervalMinutes);
			saved.MaxRetries.Should().Be(2);
			saved.Channels.Should().Be((int)(DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Email | DispatchRenotifyChannels.Voice | DispatchRenotifyChannels.ChatPlatform));
		}

		[Test]
		public void An_unlocked_save_with_no_channel_ticked_keeps_push()
		{
			var posted = CraftedPost();
			posted.RenotifyChannelValues.Clear();

			DepartmentController.BuildDispatchRenotifyConfig(posted, false, false).Channels.Should().Be((int)DispatchRenotifyChannels.Push);
		}

		[Test]
		public void The_timing_and_channels_are_locked_until_they_are_deliberately_opened_up()
		{
			DispatchConfig.RenotifyTimingLocked.Should().BeTrue();
			DispatchConfig.RenotifyChannelsLocked.Should().BeTrue();
		}

		[TestCase(nameof(DispatchSettingsView.RenotifyTimingLocked))]
		[TestCase(nameof(DispatchSettingsView.RenotifyChannelsLocked))]
		[TestCase(nameof(DispatchSettingsView.RenotifyChannels))]
		public void What_is_locked_and_shown_ticked_can_never_be_posted(string property)
		{
			typeof(DispatchSettingsView).GetProperty(property).GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), true)
				.Should().NotBeEmpty();
		}

		[Test]
		public void The_page_renders_every_channel_disabled_with_push_ticked_while_the_channels_are_locked()
		{
			var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			var view = System.IO.File.ReadAllText(System.IO.Path.Combine(directory!.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", "Department", "DispatchSettings.cshtml"));

			view.Should().Contain("@foreach (var channel in Resgrid.Model.DispatchRenotifyConfig.AllChannels)");
			view.Should().Contain("name=\"RenotifyChannelValues\"").And.Contain("disabled=\"@Model.RenotifyChannelsLocked\"");
			view.Should().Contain("@if (Model.RenotifyTimingLocked || Model.RenotifyChannelsLocked)");
		}

		[Test]
		public void The_page_renders_the_timing_read_only_with_the_lock_notice_while_locked()
		{
			var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			var view = System.IO.File.ReadAllText(System.IO.Path.Combine(directory!.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", "Department", "DispatchSettings.cshtml"));

			view.Should().Contain("asp-for=\"RenotifyEnabled\"");
			view.Should().Contain("@if (Model.RenotifyTimingLocked || Model.RenotifyChannelsLocked)").And.Contain("localizer[\"DispatchRenotifyLockedNotice\"]");
			System.Text.RegularExpressions.Regex.Matches(view, "disabled=\"@Model.RenotifyTimingLocked\"").Count.Should().Be(2, "the interval and the retry count are both locked");
		}
	}
}

namespace Resgrid.Tests.Services.CommunicationServiceTests
{
	[TestFixture]
	[NonParallelizable]
	public class when_re_notifying_a_dispatch : with_the_communication_service
	{
		private static Call Call() => new Call { CallId = 88, DepartmentId = 1, Name = "Structure Fire", Address = "12 Main St", Priority = 2 };

		[Test]
		public async Task a_member_gets_the_dispatch_push_again_titled_as_a_reminder()
		{
			var profile = new UserProfile { UserId = "user-1", SendPush = true, SendSms = true, SendEmail = true, VoiceForCall = true, Language = "en" };
			_pushServiceMock.Setup(x => x.PushCall(It.IsAny<StandardPushCall>(), "user-1", profile, It.IsAny<DepartmentCallPriority>())).ReturnsAsync(true);

			var sent = await _communicationService.SendCallRenotificationAsync(Call(), "user-1", 2, 3, DispatchRenotifyChannels.Push, profile);

			sent.Should().BeTrue();
			_pushServiceMock.Verify(x => x.PushCall(It.Is<StandardPushCall>(p => p.CallId == 88 && p.Title == "Reminder 2/3: Call Structure Fire" && p.SubTitle == "12 Main St" && p.SoundType == PushSoundTypes.ModernCallRenotify),
				"user-1", profile, It.IsAny<DepartmentCallPriority>()), Times.Once);
		}

		[Test]
		public async Task only_the_channels_asked_for_go_out()
		{
			var profile = new UserProfile { UserId = "user-1", SendPush = true, SendSms = true, SendEmail = true, VoiceForCall = true };

			await _communicationService.SendCallRenotificationAsync(Call(), "user-1", 1, 3, DispatchRenotifyChannels.Push, profile);

			_pushServiceMock.Verify(x => x.PushCall(It.IsAny<StandardPushCall>(), "user-1", profile, It.IsAny<DepartmentCallPriority>()), Times.Once);
			_smsServiceMock.VerifyNoOtherCalls();
			_emailServiceMock.VerifyNoOtherCalls();
			_outboundVoiceProviderMock.VerifyNoOtherCalls();
			_chatbotOutboundServiceMock.VerifyNoOtherCalls();
		}

		private const DispatchRenotifyChannels EveryChannel =
			DispatchRenotifyChannels.Push | DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Email | DispatchRenotifyChannels.Voice | DispatchRenotifyChannels.ChatPlatform;

		[Test]
		public async Task every_channel_asked_for_sends_the_dispatch_again_as_a_reminder()
		{
			var call = Call();
			var profile = new UserProfile { UserId = "user-1", SendPush = true, SendSms = true, SendEmail = true, VoiceForCall = true, VoiceCallMobile = true, Language = "en" };
			_smsServiceMock.Setup(x => x.SendCallAsync(It.IsAny<Call>(), It.IsAny<CallDispatch>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<string>(), It.IsAny<Payment>()))
				.ReturnsAsync(true);

			var sent = await _communicationService.SendCallRenotificationAsync(call, "user-1", 1, 3, EveryChannel, profile, "12 Main St", "+15555550100");

			sent.Should().BeTrue();
			_pushServiceMock.Verify(x => x.PushCall(It.Is<StandardPushCall>(p => p.Title == "Reminder 1/3: Call Structure Fire"), "user-1", profile, It.IsAny<DepartmentCallPriority>()), Times.Once);
			_smsServiceMock.Verify(x => x.SendCallAsync(It.Is<Call>(c => c.Name == "Reminder 1/3: Structure Fire" && c.CallId == 88), It.Is<CallDispatch>(d => d.UserId == "user-1" && d.CallId == 88),
				"+15555550100", 1, profile, "12 Main St", It.IsAny<Payment>()), Times.Once);
			_emailServiceMock.Verify(x => x.SendCallAsync(It.Is<Call>(c => c.Name == "Reminder 1/3: Structure Fire"), It.Is<CallDispatch>(d => d.UserId == "user-1"), profile), Times.Once);
			_outboundVoiceProviderMock.Verify(x => x.CommunicateCallAsync("+15555550100", profile, It.Is<Call>(c => c.CallId == 88)), Times.Once);
			_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync("user-1", 1, It.Is<ChatbotOutboundMessage>(m => m.Type == ChatbotOutboundType.Dispatch
				&& m.Title == "Reminder 1/3: Call Structure Fire" && m.Body == "12 Main St" && m.ReferenceId == "88")), Times.Once);
			call.Name.Should().Be("Structure Fire", "the reminder text is put on a copy, never on the call");
		}

		[Test]
		public async Task the_members_own_notification_settings_still_decide()
		{
			var profile = new UserProfile { UserId = "user-1", SendPush = true, SendSms = false, SendEmail = false, VoiceForCall = false };

			await _communicationService.SendCallRenotificationAsync(Call(), "user-1", 1, 3, EveryChannel & ~DispatchRenotifyChannels.ChatPlatform, profile, null, "+15555550100");

			_pushServiceMock.Verify(x => x.PushCall(It.IsAny<StandardPushCall>(), "user-1", profile, It.IsAny<DepartmentCallPriority>()), Times.Once);
			_smsServiceMock.VerifyNoOtherCalls();
			_emailServiceMock.VerifyNoOtherCalls();
			_outboundVoiceProviderMock.VerifyNoOtherCalls();
		}

		[Test]
		public async Task an_unverified_number_or_address_is_not_used()
		{
			var profile = new UserProfile { UserId = "user-1", SendSms = true, SendEmail = true, VoiceForCall = true, VoiceCallMobile = true, MobileNumberVerified = false, EmailVerified = false };

			await _communicationService.SendCallRenotificationAsync(Call(), "user-1", 1, 3,
				DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Email | DispatchRenotifyChannels.Voice, profile, null, "+15555550100");

			_smsServiceMock.VerifyNoOtherCalls();
			_emailServiceMock.VerifyNoOtherCalls();
			_outboundVoiceProviderMock.VerifyNoOtherCalls();
		}

		[Test]
		public async Task a_protected_department_gets_the_safe_view_on_every_channel()
		{
			var call = Call();
			foreach (var channel in new[] { ProtectedDataEgressChannel.Sms, ProtectedDataEgressChannel.Email, ProtectedDataEgressChannel.Voice, ProtectedDataEgressChannel.ChatPlatform })
				_protectedProjectionServiceMock.Setup(x => x.BuildNotificationSafeCallAsync(1, call, channel, It.IsAny<string>()))
					.ReturnsAsync(new Call { CallId = 88, DepartmentId = 1, Name = "88", NatureOfCall = "Sign in to Resgrid" });
			var profile = new UserProfile { UserId = "user-1", SendSms = true, SendEmail = true, VoiceForCall = true };

			await _communicationService.SendCallRenotificationAsync(call, "user-1", 1, 3, EveryChannel & ~DispatchRenotifyChannels.Push, profile, "12 Main St", "+15555550100");

			_smsServiceMock.Verify(x => x.SendCallAsync(It.Is<Call>(c => c.Name == "Reminder 1/3: 88" && c.Address == null), It.IsAny<CallDispatch>(), "+15555550100", 1, profile,
				null, It.IsAny<Payment>()), Times.Once, "the resolved address is protected location data");
			_emailServiceMock.Verify(x => x.SendCallAsync(It.Is<Call>(c => c.Name == "Reminder 1/3: 88"), It.IsAny<CallDispatch>(), profile), Times.Once);
			_outboundVoiceProviderMock.Verify(x => x.CommunicateCallAsync("+15555550100", profile, It.Is<Call>(c => c.Name == "88")), Times.Once);
			_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync("user-1", 1, It.Is<ChatbotOutboundMessage>(m => m.Title == "Reminder 1/3: Call 88" && m.Body == "Sign in to Resgrid")), Times.Once);
		}

		[Test]
		public async Task a_unit_is_re_notified_by_push_only_whatever_is_asked_for()
		{
			await _communicationService.SendUnitCallRenotificationAsync(Call(), new CallDispatchUnit { CallId = 88, UnitId = 31 }, 1, 3, EveryChannel);

			_pushServiceMock.Verify(x => x.PushCallUnit(It.IsAny<StandardPushCall>(), 31, It.IsAny<DepartmentCallPriority>()), Times.Once);
			_smsServiceMock.VerifyNoOtherCalls();
			_emailServiceMock.VerifyNoOtherCalls();
			_outboundVoiceProviderMock.VerifyNoOtherCalls();
			_chatbotOutboundServiceMock.VerifyNoOtherCalls();
		}

		[Test]
		public async Task a_member_who_turned_push_off_gets_nothing()
		{
			var profile = new UserProfile { UserId = "user-1", SendPush = false };

			var sent = await _communicationService.SendCallRenotificationAsync(Call(), "user-1", 1, 3, DispatchRenotifyChannels.Push, profile);

			sent.Should().BeFalse();
			_pushServiceMock.Verify(x => x.PushCall(It.IsAny<StandardPushCall>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<DepartmentCallPriority>()), Times.Never);
		}

		[Test]
		public async Task a_member_no_longer_in_the_department_gets_nothing()
		{
			_departmentsServiceMock.Setup(x => x.GetDepartmentMemberAsync("user-1", 1, It.IsAny<bool>())).ReturnsAsync((DepartmentMember)null);

			var sent = await _communicationService.SendCallRenotificationAsync(Call(), "user-1", 1, 3, DispatchRenotifyChannels.Push, new UserProfile { UserId = "user-1", SendPush = true });

			sent.Should().BeFalse();
			_pushServiceMock.VerifyNoOtherCalls();
		}

		[Test]
		public async Task a_protected_department_gets_the_safe_push_view()
		{
			var call = Call();
			_protectedProjectionServiceMock.Setup(x => x.BuildNotificationSafeCallAsync(1, call, ProtectedDataEgressChannel.Push, It.IsAny<string>()))
				.ReturnsAsync(new Call { CallId = 88, DepartmentId = 1, Name = "88", NatureOfCall = "Sign in to Resgrid" });

			await _communicationService.SendCallRenotificationAsync(call, "user-1", 1, 3, DispatchRenotifyChannels.Push, new UserProfile { UserId = "user-1", SendPush = true }, "12 Main St");

			_pushServiceMock.Verify(x => x.PushCall(It.Is<StandardPushCall>(p => p.Title == "Reminder 1/3: Call 88" && p.SubTitle == "Sign in to Resgrid"),
				"user-1", It.IsAny<UserProfile>(), It.IsAny<DepartmentCallPriority>()), Times.Once);
		}

		[Test]
		public async Task a_unit_gets_the_unit_push_again_titled_as_a_reminder()
		{
			_pushServiceMock.Setup(x => x.PushCallUnit(It.IsAny<StandardPushCall>(), 31, It.IsAny<DepartmentCallPriority>())).ReturnsAsync(true);

			var sent = await _communicationService.SendUnitCallRenotificationAsync(Call(), new CallDispatchUnit { CallId = 88, UnitId = 31 }, 3, 3, DispatchRenotifyChannels.Push);

			sent.Should().BeTrue();
			_pushServiceMock.Verify(x => x.PushCallUnit(It.Is<StandardPushCall>(p => p.Title == "Reminder 3/3: Call Structure Fire" && p.SubTitle == "12 Main St" && p.SoundType == PushSoundTypes.ModernCallRenotify), 31,
				It.IsAny<DepartmentCallPriority>()), Times.Once);
		}

		[TestCase("de", "Erinnerung 1/3: Call Structure Fire")]
		[TestCase("fr", "Rappel 1/3 : Call Structure Fire")]
		[TestCase("es", "Recordatorio 1/3: Call Structure Fire")]
		public void the_reminder_title_is_in_the_members_language(string language, string expected)
		{
			CommunicationService.BuildRenotifyTitle("Call Structure Fire", 1, 3, language).Should().Be(expected);
		}

		[Test]
		public async Task the_original_dispatch_push_is_unchanged()
		{
			var profile = new UserProfile { UserId = "user-1", SendPush = true };

			await _communicationService.SendCallAsync(Call(), new CallDispatch { UserId = "user-1" }, null, 1, profile, "12 Main St");

			_pushServiceMock.Verify(x => x.PushCall(It.Is<StandardPushCall>(p => p.Title == "Call Structure Fire" && p.SubTitle == "12 Main St" && p.Color == "#000000" && p.SoundType == null),
				"user-1", profile, It.IsAny<DepartmentCallPriority>()), Times.Once);
		}
	}
}
