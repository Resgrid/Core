using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Releasing (standing down) one unit from a call that stays open: Belgian EMS, the ambulance arrives first and stands the
	/// MUG down, which goes back to radio availability for the next call. The dispatch stays on the record, marked released.
	/// </summary>
	[TestFixture]
	public class CallResourceReleaseServiceTests
	{
		private const int DeptId = 7;
		private const int CallId = 40;
		private const int Ambulance = 11;
		private const int Mug = 12;

		private Mock<ICallsService> _calls;
		private Mock<ICallDispatchUnitRepository> _unitDispatches;
		private Mock<ICallDispatchStatusService> _dispatchStatuses;
		private Mock<IUnitsService> _units;
		private Mock<ICommunicationService> _communication;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IEventAggregator> _events;
		private Mock<ICallPersonnelReleasesRepository> _personnelReleases;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<ICustomStateService> _customStates;
		private Mock<ICallsRepository> _callsRepository;
		private CallResourceReleaseService _service;
		private Call _call;
		private List<CallDispatchUnit> _saved;

		[SetUp]
		public void SetUp()
		{
			_calls = new Mock<ICallsService>();
			_unitDispatches = new Mock<ICallDispatchUnitRepository>();
			_dispatchStatuses = new Mock<ICallDispatchStatusService>();
			_units = new Mock<IUnitsService>();
			_communication = new Mock<ICommunicationService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_events = new Mock<IEventAggregator>();
			_personnelReleases = new Mock<ICallPersonnelReleasesRepository>();
			_actionLogs = new Mock<IActionLogsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_customStates = new Mock<ICustomStateService>();
			_callsRepository = new Mock<ICallsRepository>();
			_saved = new List<CallDispatchUnit>();

			_call = new Call
			{
				CallId = CallId,
				DepartmentId = DeptId,
				State = (int)CallStates.Active,
				HasBeenDispatched = true,
				UnitDispatches = new List<CallDispatchUnit>
				{
					new CallDispatchUnit { CallDispatchUnitId = 1, CallId = CallId, UnitId = Ambulance, DispatchedOn = DateTime.UtcNow.AddMinutes(-5) },
					new CallDispatchUnit { CallDispatchUnitId = 2, CallId = CallId, UnitId = Mug, DispatchedOn = DateTime.UtcNow.AddMinutes(-5) }
				}
			};

			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool _, bool __, bool ___, bool ____, bool _____, bool ______, bool _______, bool ________, bool _________, bool __________) => c);
			_unitDispatches.Setup(x => x.TryReleaseAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback<int, DateTime, string, CancellationToken>((id, _, __, ___) => _saved.Add(_call.UnitDispatches.Single(d => d.CallDispatchUnitId == id)))
				.ReturnsAsync(true);
			_settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(DeptId)).ReturnsAsync("+3290000000");

			_customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DeptId)).ReturnsAsync(new List<CustomState>());
			_settings.Setup(x => x.GetShiftCallReleasePersonnelStatusToSetAsync(DeptId)).ReturnsAsync(-1);
			_personnelReleases.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallPersonnelRelease>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CallPersonnelRelease r, CancellationToken _, bool __) => { r.CallPersonnelReleaseId ??= "rel-1"; return r; });

			_service = new CallResourceReleaseService(_calls.Object, _callsRepository.Object, _unitDispatches.Object, _personnelReleases.Object, _dispatchStatuses.Object,
				_units.Object, _actionLogs.Object, _groups.Object, _roles.Object, _customStates.Object, _communication.Object, _settings.Object, _events.Object);
		}

		/// <summary>The unit's latest status before and after the release; a new one is the release automation's, against the call.</summary>
		private void StatusBeforeAndAfter(int beforeId, int afterId)
		{
			var after = afterId == beforeId
				? new UnitState { UnitStateId = afterId, UnitId = Mug }
				: new UnitState { UnitStateId = afterId, UnitId = Mug, DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call, SetByOrigin = (int)StatusSetOrigins.DispatchAutomation };

			_units.SetupSequence(x => x.GetLastUnitStateByUnitIdAsync(Mug))
				.ReturnsAsync(new UnitState { UnitStateId = beforeId, UnitId = Mug })
				.ReturnsAsync(after);
		}

		[Test]
		public async Task releases_the_unit_and_keeps_its_dispatch_on_the_record()
		{
			StatusBeforeAndAfter(100, 101);

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeTrue();
			var mug = _call.UnitDispatches.Single(x => x.UnitId == Mug);
			mug.IsReleased.Should().BeTrue();
			mug.ReleasedByUserId.Should().Be("dispatcher-1");
			_saved.Should().ContainSingle().Which.Should().BeSameAs(mug);
			_call.UnitDispatches.Single(x => x.UnitId == Ambulance).IsReleased.Should().BeFalse();
			_call.GetActiveUnitDispatches().Select(x => x.UnitId).Should().Equal(Ambulance);
		}

		[Test]
		public async Task applies_the_release_status_to_that_unit_only_and_never_to_the_call_groups()
		{
			StatusBeforeAndAfter(100, 100);

			await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: false);

			// An empty group list, not null: null would fall back to the call's groups and release their shift personnel.
			_dispatchStatuses.Verify(x => x.ApplyReleaseStatusesAsync(_call, It.Is<IEnumerable<int>>(g => g != null && !g.Any()),
				It.Is<IEnumerable<int>>(u => u.SequenceEqual(new[] { Mug })), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task reports_no_status_change_when_the_unit_kept_its_status()
		{
			// Already back on radio availability ("Radiofonisch") when the dispatcher released it.
			StatusBeforeAndAfter(100, 100);

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeFalse();
		}

		[Test]
		public async Task notifies_the_unit_and_the_consoles_when_asked()
		{
			StatusBeforeAndAfter(100, 101);

			await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			_communication.Verify(x => x.SendCancelUnitCallAsync(_call, It.Is<CallDispatchUnit>(d => d.UnitId == Mug), "+3290000000", null), Times.Once);
			_events.Verify(x => x.SendMessage(It.Is<CallUpdatedEvent>(e => e.DepartmentId == DeptId && e.Call.CallId == CallId)), Times.Once);
		}

		[Test]
		public async Task does_not_notify_the_unit_when_not_asked()
		{
			StatusBeforeAndAfter(100, 101);

			await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: false);

			_communication.Verify(x => x.SendCancelUnitCallAsync(It.IsAny<Call>(), It.IsAny<CallDispatchUnit>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once);
		}

		[Test]
		public async Task a_failed_notice_does_not_undo_the_release()
		{
			StatusBeforeAndAfter(100, 101);
			_communication.Setup(x => x.SendCancelUnitCallAsync(It.IsAny<Call>(), It.IsAny<CallDispatchUnit>(), It.IsAny<string>(), It.IsAny<string>()))
				.ThrowsAsync(new InvalidOperationException("push down"));

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			_call.UnitDispatches.Single(x => x.UnitId == Mug).IsReleased.Should().BeTrue();
		}

		[Test]
		public async Task refuses_a_call_of_another_department()
		{
			_call.DepartmentId = 99;

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.CallNotFound);
			_saved.Should().BeEmpty();
		}

		[TestCase((int)CallStates.Closed, true, 0)]
		[TestCase((int)CallStates.Active, false, 30)]
		public async Task refuses_a_call_that_is_closed_or_has_not_gone_out(int state, bool hasBeenDispatched, int dispatchInMinutes)
		{
			_call.State = state;
			_call.HasBeenDispatched = hasBeenDispatched;
			_call.DispatchOn = dispatchInMinutes > 0 ? DateTime.UtcNow.AddMinutes(dispatchInMinutes) : (DateTime?)null;

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.CallNotLive);
			_saved.Should().BeEmpty();
			_dispatchStatuses.Verify(x => x.ApplyReleaseStatusesAsync(It.IsAny<Call>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task refuses_a_unit_that_was_never_on_the_call()
		{
			var result = await _service.ReleaseUnitAsync(DeptId, CallId, 99, "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.NotOnCall);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task refuses_a_unit_already_released()
		{
			_call.UnitDispatches.Single(x => x.UnitId == Mug).Release(DateTime.UtcNow.AddMinutes(-1), "dispatcher-2");

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.AlreadyReleased);
			result.Dispatch.ReleasedByUserId.Should().Be("dispatcher-2");
			_saved.Should().BeEmpty();
			_communication.Verify(x => x.SendCancelUnitCallAsync(It.IsAny<Call>(), It.IsAny<CallDispatchUnit>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task a_release_that_loses_the_race_to_another_dispatcher_changes_nothing_and_sends_nothing()
		{
			// Both requests read the unit live; the other one's conditional write changed the row first.
			StatusBeforeAndAfter(100, 101);
			_unitDispatches.Setup(x => x.TryReleaseAsync(2, It.IsAny<DateTime>(), "dispatcher-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_unitDispatches.Setup(x => x.GetCallUnitDispatchesByCallIdAsync(CallId)).ReturnsAsync(new List<CallDispatchUnit>
			{
				new CallDispatchUnit { CallDispatchUnitId = 2, CallId = CallId, UnitId = Mug, ReleasedOn = DateTime.UtcNow, ReleasedByUserId = "dispatcher-2" }
			});

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.AlreadyReleased);
			result.Dispatch.ReleasedByUserId.Should().Be("dispatcher-2");
			_call.UnitDispatches.Single(x => x.UnitId == Mug).IsReleased.Should().BeFalse("the in-memory row is only marked when this request released it");
			_dispatchStatuses.Verify(x => x.ApplyReleaseStatusesAsync(It.IsAny<Call>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()), Times.Never);
			_communication.Verify(x => x.SendCancelUnitCallAsync(It.IsAny<Call>(), It.IsAny<CallDispatchUnit>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async Task a_release_whose_dispatch_was_removed_meanwhile_reports_the_unit_not_on_the_call()
		{
			// An edit took the unit off the call between this request's read and its write: no released row confirms a release.
			StatusBeforeAndAfter(100, 101);
			_unitDispatches.Setup(x => x.TryReleaseAsync(2, It.IsAny<DateTime>(), "dispatcher-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_unitDispatches.Setup(x => x.GetCallUnitDispatchesByCallIdAsync(CallId)).ReturnsAsync(new List<CallDispatchUnit>
			{
				new CallDispatchUnit { CallDispatchUnitId = 1, CallId = CallId, UnitId = Ambulance }
			});

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.NotOnCall);
			result.Dispatch.Should().BeNull();
			_communication.Verify(x => x.SendCancelUnitCallAsync(It.IsAny<Call>(), It.IsAny<CallDispatchUnit>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task a_failed_release_status_still_completes_the_release()
		{
			StatusBeforeAndAfter(100, 101);
			_dispatchStatuses.Setup(x => x.ApplyReleaseStatusesAsync(It.IsAny<Call>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("status store down"));

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: true);

			// A retry would only see AlreadyReleased, so the notice, the counts and the call update go out now.
			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeFalse();
			_communication.Verify(x => x.SendCancelUnitCallAsync(_call, It.Is<CallDispatchUnit>(d => d.UnitId == Mug), "+3290000000", null), Times.Once);
			_calls.Verify(x => x.InvalidateOpenCallCountsAsync(DeptId), Times.Once);
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once);
		}

		[Test]
		public async Task a_crew_status_saved_meanwhile_is_not_reported_as_the_release_status()
		{
			// The unit was already available (the automation left it alone) and the crew pressed Available again meanwhile.
			_units.SetupSequence(x => x.GetLastUnitStateByUnitIdAsync(Mug))
				.ReturnsAsync(new UnitState { UnitStateId = 100, UnitId = Mug })
				.ReturnsAsync(new UnitState { UnitStateId = 101, UnitId = Mug, SetByOrigin = (int)StatusSetOrigins.UnitApp });

			var result = await _service.ReleaseUnitAsync(DeptId, CallId, Mug, "dispatcher-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeFalse();
		}

		private void PersonnelOnCall()
		{
			_call.Dispatches = new List<CallDispatch> { new CallDispatch { CallId = CallId, UserId = "medic-1" } };
			_call.GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { CallId = CallId, DepartmentGroupId = 5 } };
			_call.RoleDispatches = new List<CallDispatchRole>();
			_call.PersonnelReleases = new List<CallPersonnelRelease>();
			_groups.Setup(x => x.GetAllMembersForGroupAsync(5)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "doctor-1" } });
		}

		[Test]
		public async Task releases_a_member_reached_through_a_group_and_sets_the_personnel_release_status()
		{
			PersonnelOnCall();
			_actionLogs.Setup(x => x.GetLastActionLogForUserAsync("doctor-1", DeptId))
				.ReturnsAsync(new ActionLog { ActionLogId = 1, UserId = "doctor-1", ActionTypeId = (int)ActionTypes.RespondingToScene, DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call });

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "doctor-1", "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeTrue();
			result.PersonnelRelease.UserId.Should().Be("doctor-1");
			result.PersonnelRelease.ReleasedByUserId.Should().Be("dispatcher-1");
			_call.IsUserReleased("doctor-1").Should().BeTrue();
			_actionLogs.Verify(x => x.SetUserActionAsync("doctor-1", DeptId, (int)ActionTypes.StandingBy, (string)null, CallId, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
			_communication.Verify(x => x.SendCancelCallAsync(_call, It.Is<CallDispatch>(d => d.UserId == "doctor-1"), "+3290000000", DeptId, It.IsAny<UserProfile>(), It.IsAny<string>()), Times.Once);
			_events.Verify(x => x.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once);
		}

		[Test]
		public async Task a_member_already_available_keeps_their_status()
		{
			PersonnelOnCall();
			_actionLogs.Setup(x => x.GetLastActionLogForUserAsync("medic-1", DeptId))
				.ReturnsAsync(new ActionLog { ActionLogId = 1, UserId = "medic-1", ActionTypeId = (int)ActionTypes.StandingBy });

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "medic-1", "dispatcher-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeFalse();
			_actionLogs.Verify(x => x.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_member_working_another_open_call_keeps_their_status()
		{
			PersonnelOnCall();
			_actionLogs.Setup(x => x.GetLastActionLogForUserAsync("medic-1", DeptId))
				.ReturnsAsync(new ActionLog { ActionLogId = 1, UserId = "medic-1", ActionTypeId = (int)ActionTypes.RespondingToScene, DestinationId = 41, DestinationType = (int)DestinationEntityTypes.Call });
			_callsRepository.Setup(x => x.GetByIdAsync(41)).ReturnsAsync(new Call { CallId = 41, DepartmentId = DeptId, State = (int)CallStates.Active });

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "medic-1", "dispatcher-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeFalse();
		}

		[Test]
		public async Task refuses_a_member_who_is_not_on_the_call()
		{
			PersonnelOnCall();

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "someone-else", "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.NotOnCall);
			_personnelReleases.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CallPersonnelRelease>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task refuses_a_member_already_released()
		{
			PersonnelOnCall();
			_call.PersonnelReleases.Add(new CallPersonnelRelease { CallId = CallId, UserId = "MEDIC-1", ReleasedOn = DateTime.UtcNow, ReleasedByUserId = "dispatcher-2" });

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "medic-1", "dispatcher-1", notify: true);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.AlreadyReleased);
			result.PersonnelRelease.ReleasedByUserId.Should().Be("dispatcher-2");
		}

		[Test]
		public async Task a_member_riding_a_unit_on_the_call_can_be_released()
		{
			PersonnelOnCall();
			_units.Setup(x => x.GetCurrentRolesForUnitAsync(Ambulance)).ReturnsAsync(new List<UnitStateRole> { new UnitStateRole { UserId = "crew-1" } });

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "crew-1", "crew-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
		}

		[Test]
		public async Task a_release_stores_the_member_id_as_the_call_spells_it()
		{
			PersonnelOnCall();

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "DOCTOR-1", "dispatcher-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.PersonnelRelease.UserId.Should().Be("doctor-1");
			_personnelReleases.Verify(x => x.SaveOrUpdateAsync(It.Is<CallPersonnelRelease>(r => r.UserId == "doctor-1"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task a_member_paged_through_a_dispatched_units_station_group_can_be_released()
		{
			PersonnelOnCall();
			_settings.Setup(x => x.GetUnitDispatchAlsoDispatchToGroupAsync(DeptId)).ReturnsAsync(true);
			_units.Setup(x => x.GetUnitByIdAsync(Ambulance)).ReturnsAsync(new Unit { UnitId = Ambulance, DepartmentId = DeptId, StationGroupId = 8 });
			_groups.Setup(x => x.GetAllMembersForGroupAsync(8)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "station-1" } });

			(await _service.ReleasePersonnelAsync(DeptId, CallId, "station-1", "dispatcher-1", notify: false)).Outcome.Should().Be(CallResourceReleaseOutcome.Released);

			// Without the department's unit-group paging the broadcast never reached them, so they are not on the call.
			_settings.Setup(x => x.GetUnitDispatchAlsoDispatchToGroupAsync(DeptId)).ReturnsAsync(false);
			_call.PersonnelReleases.Clear();
			(await _service.ReleasePersonnelAsync(DeptId, CallId, "station-1", "dispatcher-1", notify: false)).Outcome.Should().Be(CallResourceReleaseOutcome.NotOnCall);
		}

		[Test]
		public async Task a_member_who_started_on_another_open_call_meanwhile_keeps_that_status()
		{
			PersonnelOnCall();
			// On this call when the release was asked for; responding to call 41 by the time the release status would be set.
			_actionLogs.SetupSequence(x => x.GetLastActionLogForUserAsync("medic-1", DeptId))
				.ReturnsAsync(new ActionLog { ActionLogId = 1, UserId = "medic-1", ActionTypeId = (int)ActionTypes.RespondingToScene, DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call })
				.ReturnsAsync(new ActionLog { ActionLogId = 2, UserId = "medic-1", ActionTypeId = (int)ActionTypes.RespondingToScene, DestinationId = 41, DestinationType = (int)DestinationEntityTypes.Call });
			_callsRepository.Setup(x => x.GetByIdAsync(41)).ReturnsAsync(new Call { CallId = 41, DepartmentId = DeptId, State = (int)CallStates.Active });

			var result = await _service.ReleasePersonnelAsync(DeptId, CallId, "medic-1", "dispatcher-1", notify: false);

			result.Outcome.Should().Be(CallResourceReleaseOutcome.Released);
			result.StatusApplied.Should().BeFalse();
			_actionLogs.Verify(x => x.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task clearing_releases_puts_members_back_on_the_call()
		{
			_personnelReleases.Setup(x => x.DeleteForUsersAsync(CallId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

			(await _service.ClearPersonnelReleasesAsync(CallId, new[] { "medic-1" })).Should().Be(1);
			(await _service.ClearPersonnelReleasesAsync(CallId, Array.Empty<string>())).Should().Be(0);
		}
	}

	[TestFixture]
	public class CallDispatchUnitReleasesTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 16, 14, 45, 0, DateTimeKind.Utc);

		private static List<CallDispatchUnit> Dispatches() => new List<CallDispatchUnit>
		{
			new CallDispatchUnit { CallDispatchUnitId = 1, CallId = 40, UnitId = 11, DispatchedOn = Now.AddMinutes(-6) },
			new CallDispatchUnit { CallDispatchUnitId = 2, CallId = 40, UnitId = 12, DispatchedOn = Now.AddMinutes(-6), DispatchCount = 1, ReleasedOn = Now.AddMinutes(-4), ReleasedByUserId = "dispatcher-1" }
		};

		[Test]
		public void active_leaves_out_released_dispatches()
		{
			CallDispatchUnitReleases.Active(Dispatches()).Select(x => x.UnitId).Should().Equal(11);
			CallDispatchUnitReleases.ActiveUnitIds(null).Should().BeEmpty();
		}

		[Test]
		public void a_list_leaving_out_a_released_unit_keeps_its_record()
		{
			var dispatches = Dispatches();

			var redispatched = CallDispatchUnitReleases.ApplyUnitList(dispatches, 40, new[] { 11 }, Now);

			redispatched.Should().BeEmpty();
			dispatches.Select(x => x.UnitId).Should().Equal(11, 12);
			dispatches.Single(x => x.UnitId == 12).IsReleased.Should().BeTrue();
		}

		[Test]
		public void a_list_naming_a_released_unit_sends_it_back_on_the_same_row()
		{
			var dispatches = Dispatches();

			var redispatched = CallDispatchUnitReleases.ApplyUnitList(dispatches, 40, new[] { 11, 12 }, Now);

			redispatched.Should().Equal(12);
			dispatches.Should().HaveCount(2);
			var mug = dispatches.Single(x => x.UnitId == 12);
			mug.CallDispatchUnitId.Should().Be(2);
			mug.IsReleased.Should().BeFalse();
			mug.ReleasedByUserId.Should().BeNull();
			mug.LastDispatchedOn.Should().Be(Now);
			mug.DispatchCount.Should().Be(2);
		}

		[Test]
		public void a_live_unit_left_out_is_removed_unless_the_editor_cannot_remove_it()
		{
			var dispatches = Dispatches();
			CallDispatchUnitReleases.ApplyUnitList(dispatches, 40, Array.Empty<int>(), Now, canRemove: _ => false);
			dispatches.Select(x => x.UnitId).Should().Equal(11, 12);

			CallDispatchUnitReleases.ApplyUnitList(dispatches, 40, Array.Empty<int>(), Now);
			dispatches.Select(x => x.UnitId).Should().Equal(12);
		}

		[Test]
		public void a_new_unit_is_added_only_when_allowed()
		{
			var dispatches = Dispatches();

			CallDispatchUnitReleases.ApplyUnitList(dispatches, 40, new[] { 11, 13, 14 }, Now, canAdd: id => id == 13);

			dispatches.Select(x => x.UnitId).Should().BeEquivalentTo(new[] { 11, 12, 13 });
			dispatches.Single(x => x.UnitId == 13).CallId.Should().Be(40);
		}

		[Test]
		public void a_released_unit_that_can_no_longer_be_dispatched_stays_released()
		{
			var dispatches = Dispatches();

			// Unit 12 was deleted since its release: the edit names it, the department's unit list does not.
			var redispatched = CallDispatchUnitReleases.ApplyUnitList(dispatches, 40, new[] { 11, 12 }, Now, canAdd: id => id != 12);

			redispatched.Should().BeEmpty();
			dispatches.Single(x => x.UnitId == 12).IsReleased.Should().BeTrue();
		}

		[Test]
		public void is_unit_on_call_reads_live_dispatches_only()
		{
			var call = new Call { UnitDispatches = Dispatches() };

			call.IsUnitOnCall(11).Should().BeTrue();
			call.IsUnitOnCall(12).Should().BeFalse();
			call.HasUnitBeenDispatched(12).Should().BeTrue();
		}

		[Test]
		public void a_released_dispatch_span_ends_at_the_release()
		{
			var windows = new[]
			{
				new CallDispatchWindow { CallId = 40, UnitId = 12, DispatchedOn = Now.AddMinutes(-6), LoggedOn = Now.AddMinutes(-7), ReleasedOn = Now.AddMinutes(-4) },
				new CallDispatchWindow { CallId = 41, UnitId = 12, DispatchedOn = Now.AddMinutes(-3), LoggedOn = Now.AddMinutes(-3) }
			};

			var spans = CallStatusAttribution.DispatchSpans(windows, Now);

			spans.Single(x => x.CallId == 40).End.Should().Be(Now.AddMinutes(-4));
			spans.Single(x => x.CallId == 41).End.Should().Be(Now);
		}

		[Test]
		public void a_release_after_the_call_closed_leaves_the_span_at_the_close()
		{
			var windows = new[]
			{
				new CallDispatchWindow { CallId = 40, UnitId = 12, DispatchedOn = Now.AddMinutes(-6), LoggedOn = Now.AddMinutes(-7), ClosedOn = Now.AddMinutes(-5), ReleasedOn = Now.AddMinutes(-4) }
			};

			CallStatusAttribution.DispatchSpans(windows, Now).Single().End.Should().Be(Now.AddMinutes(-5));
		}

		[Test]
		public void unit_times_carry_the_stand_down()
		{
			var rows = CallUnitTimesCalculator.Compute(Dispatches(), new List<UnitState>(), new Dictionary<int, int>());

			rows.Single(x => x.UnitId == 12).ReleasedOn.Should().Be(Now.AddMinutes(-4));
			rows.Single(x => x.UnitId == 11).ReleasedOn.Should().BeNull();
		}

		[Test]
		public void a_released_unit_is_not_chased_by_re_notification()
		{
			var call = new Call { UnitDispatches = Dispatches() };

			DispatchRenotificationService.IsStillDispatched(call, new DispatchRenotification { RecipientType = (int)DispatchRenotifyRecipientTypes.Unit, RecipientId = "12" })
				.Should().BeFalse();
			DispatchRenotificationService.IsStillDispatched(call, new DispatchRenotification { RecipientType = (int)DispatchRenotifyRecipientTypes.Unit, RecipientId = "11" })
				.Should().BeTrue();
		}

		[Test]
		public void a_released_member_is_not_chased_by_re_notification_however_they_were_reached()
		{
			var call = new Call
			{
				Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "medic-1" } },
				GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { DepartmentGroupId = 5 } },
				PersonnelReleases = new List<CallPersonnelRelease> { new CallPersonnelRelease { UserId = "medic-1" }, new CallPersonnelRelease { UserId = "doctor-1" } }
			};

			DispatchRenotificationService.IsStillDispatched(call, new DispatchRenotification { RecipientType = (int)DispatchRenotifyRecipientTypes.User, RecipientId = "medic-1" }).Should().BeFalse();
			DispatchRenotificationService.IsStillDispatched(call, new DispatchRenotification { RecipientType = (int)DispatchRenotifyRecipientTypes.User, RecipientId = "doctor-1", SourceType = (int)DispatchRenotifySourceTypes.Group, SourceId = "5" }).Should().BeFalse();
			DispatchRenotificationService.IsStillDispatched(call, new DispatchRenotification { RecipientType = (int)DispatchRenotifyRecipientTypes.User, RecipientId = "nurse-1", SourceType = (int)DispatchRenotifySourceTypes.Group, SourceId = "5" }).Should().BeTrue();
		}

		[Test]
		public void a_released_member_span_ends_at_the_release_on_every_route()
		{
			var windows = new[]
			{
				new CallDispatchWindow { CallId = 40, UserId = "doctor-1", DispatchedOn = Now.AddMinutes(-6), LoggedOn = Now.AddMinutes(-7), ReleasedOn = Now.AddMinutes(-4), Paged = true },
				new CallDispatchWindow { CallId = 40, UserId = "doctor-1", DispatchedOn = Now.AddMinutes(-5), LoggedOn = Now.AddMinutes(-7), ReleasedOn = Now.AddMinutes(-4) }
			};

			CallStatusAttribution.DispatchSpans(windows, Now).Single().End.Should().Be(Now.AddMinutes(-4));
		}

		[Test]
		public void a_rebroadcast_skips_a_released_unit()
		{
			new Call { UnitDispatches = Dispatches() }.GetActiveUnitDispatches().Select(x => x.UnitId).Should().Equal(11);
		}
	}
}
