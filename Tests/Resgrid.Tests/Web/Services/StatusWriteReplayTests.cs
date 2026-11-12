using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Helpers;
using Resgrid.Tests.Mocks;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.PersonnelStaffing;
using Resgrid.Web.Services.Models.v4.PersonnelStatuses;
using Resgrid.Web.Services.Models.v4.UnitStatus;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 status writes replayed from an app's offline outbox. Omni queues every unit status, personnel status and staffing and,
	/// when the answer is lost (15 s timeout, dropped connection), sends the same body again with the same EventId (its outbox
	/// event id). A replay returns the row the first attempt saved: no second status, no second UnitStatusEvent, no second call
	/// attribution or auto-close. Writes without an EventId, and Responder's statuses (which carry the destination's call id as
	/// their EventId), are saved exactly as before.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class StatusWriteReplayTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;
		private const int CallId = 1234;
		private const string UserId = "responder-1";
		private const string RiderId = "rider-1";
		private const string OutboxEventId = "6f1c2a7e-9b1d-4c55-8a43-2f0e7d9b1c11";

		private static readonly DateTime TappedAt = new DateTime(2026, 11, 11, 9, 30, 15, 250, DateTimeKind.Utc);

		private MockInboundEmailReceiptsRepository _receipts;
		private Mock<IUnitsService> _units;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<IUserStateService> _userStates;
		private Mock<IDepartmentsService> _departments;
		private Mock<IAuthorizationService> _authorization;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentSettingsService> _settings;
		private List<UnitState> _savedStates;
		private List<ActionLog> _savedLogs;
		private List<UserState> _savedStaffings;
		private int _roleSaveFailures;
		private Func<Task> _duringFirstUnitStateSave;
		private DefaultHttpContext _http;

		[SetUp]
		public void SetUp()
		{
			_receipts = new MockInboundEmailReceiptsRepository();
			_savedStates = new List<UnitState>();
			_savedLogs = new List<ActionLog>();
			_savedStaffings = new List<UserState>();
			_roleSaveFailures = 0;
			_duringFirstUnitStateSave = null;

			_units = new Mock<IUnitsService>();
			_units.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "PMRT A1" });
			_units.Setup(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), DepartmentId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Returns(async (UnitState state, int departmentId, CancellationToken _, bool __) =>
				{
					state.UnitStateId = 900 + _savedStates.Count;
					_savedStates.Add(state);

					var during = _duringFirstUnitStateSave;
					_duringFirstUnitStateSave = null;
					if (during != null)
						await during();

					return state;
				});
			_units.Setup(x => x.GetRolesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitRole> { new UnitRole { UnitRoleId = 7, UnitId = UnitId, Name = "Driver" } });
			_units.Setup(x => x.AddAllUnitStateRolesAsync(It.IsAny<List<UnitStateRole>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((List<UnitStateRole> roles, CancellationToken _) =>
				{
					if (_roleSaveFailures > 0)
					{
						_roleSaveFailures--;
						throw new InvalidOperationException("UnitStateRoles insert failed.");
					}

					return true;
				});

			_actionLogs = new Mock<IActionLogsService>();
			_actionLogs.Setup(x => x.SaveActionLogAsync(It.IsAny<ActionLog>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ActionLog log, CancellationToken _) =>
				{
					log.ActionLogId = 500 + _savedLogs.Count;
					_savedLogs.Add(log);
					return log;
				});

			_userStates = new Mock<IUserStateService>();
			_userStates.Setup(x => x.CreateUserStateAsync(It.IsAny<UserState>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((UserState state, CancellationToken _) =>
				{
					state.UserStateId = 300 + _savedStaffings.Count;
					_savedStaffings.Add(state);
					return state;
				});

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int departmentId, bool _) => new DepartmentMember { UserId = userId, DepartmentId = departmentId });

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewUnitAsync(UserId, UnitId)).ReturnsAsync(true);
			_authorization.Setup(x => x.IsUserValidWithinLimitsAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(true);

			_calls = new Mock<ICallsService>();
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId });

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(x => x.GetPersonnelOnUnitSetUnitStatusAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(true);

			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private IInboundEmailReceiptsService Receipts() => new InboundEmailReceiptsService(_receipts, TimeProvider.System);

		private UnitStatusController UnitStatus() =>
			new UnitStatusController(_calls.Object, _units.Object, Mock.Of<IDepartmentGroupsService>(), _settings.Object, _actionLogs.Object,
				Mock.Of<IMappingService>(), Mock.Of<IIncidentCommandService>(), _departments.Object, DispatchScopeMocks.Off(), _authorization.Object,
				Receipts())
			{
				ControllerContext = new ControllerContext { HttpContext = _http }
			};

		private PersonnelStatusesController PersonStatus() =>
			new PersonnelStatusesController(Mock.Of<IUsersService>(), _actionLogs.Object, _departments.Object, Mock.Of<IUserProfileService>(),
				_userStates.Object, Mock.Of<IDepartmentGroupsService>(), _calls.Object, Mock.Of<IMappingService>(), Mock.Of<IPersonnelRolesService>(),
				_settings.Object, _authorization.Object, Mock.Of<IIncidentCommandService>(), DispatchScopeMocks.Off(), Receipts())
			{
				ControllerContext = new ControllerContext { HttpContext = _http }
			};

		private PersonnelStaffingController PersonStaffing() =>
			new PersonnelStaffingController(Mock.Of<IUsersService>(), _actionLogs.Object, _departments.Object, Mock.Of<IUserProfileService>(),
				_userStates.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), _settings.Object, _authorization.Object,
				Mock.Of<IProtectedReadService>(), Receipts())
			{
				ControllerContext = new ControllerContext { HttpContext = _http }
			};

		private static UnitStatusInput UnitInput(string eventId = OutboxEventId, DateTime? timestamp = null, bool withRoles = false) => new UnitStatusInput
		{
			Id = UnitId.ToString(),
			Type = ((int)UnitStateTypes.Responding).ToString(),
			Timestamp = timestamp ?? TappedAt,
			TimestampUtc = timestamp ?? TappedAt,
			Latitude = "50.8503",
			Longitude = "4.3517",
			EventId = eventId,
			Roles = withRoles ? new List<UnitStatusRoleInput> { new UnitStatusRoleInput { UserId = RiderId, RoleId = "7", EventId = eventId } } : null
		};

		private static SavePersonStatusInput PersonInput(string eventId = OutboxEventId, int type = (int)ActionTypes.RespondingToScene, DateTime? timestamp = null,
			string respondingTo = null) => new SavePersonStatusInput
		{
			UserId = UserId,
			Type = type.ToString(),
			Timestamp = timestamp ?? TappedAt,
			TimestampUtc = timestamp ?? TappedAt,
			RespondingTo = respondingTo,
			RespondingToType = respondingTo == null ? null : (int)DestinationEntityTypes.Call,
			EventId = eventId
		};

		private static SavePersonnelStaffingInput StaffingInput(string eventId = OutboxEventId) => new SavePersonnelStaffingInput
		{
			UserId = UserId,
			Type = ((int)UserStateTypes.Committed).ToString(),
			Timestamp = TappedAt,
			TimestampUtc = TappedAt,
			EventId = eventId
		};

		private static string Id<T>(ActionResult<T> result) => (result.Result as ObjectResult)?.Value switch
		{
			SaveUnitStatusResult unit => unit.Id,
			SavePersonStatusResult person => person.Id,
			SavePersonnelStaffingResult staffing => staffing.Id,
			_ => null
		};

		private static int StatusCodeOf<T>(ActionResult<T> result) => result.Result switch
		{
			ObjectResult o => o.StatusCode ?? 200,
			StatusCodeResult s => s.StatusCode,
			_ => 0
		};

		#region SaveUnitStatus

		[Test]
		public async Task a_unit_status_replayed_with_its_outbox_event_id_returns_the_first_state_without_saving_again()
		{
			var first = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);
			var replay = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);

			first.Result.Should().BeOfType<CreatedAtActionResult>();
			replay.Result.Should().BeOfType<CreatedAtActionResult>();
			Id(first).Should().Be("900");
			Id(replay).Should().Be("900");
			_units.Verify(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);

			var receipt = _receipts.Receipts.Should().ContainSingle().Subject;
			receipt.Kind.Should().Be((int)InboundEmailKinds.ApiUnitStatus);
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Completed);
			receipt.EntityId.Should().Be(900);
			receipt.Deliveries.Should().Be(2);
		}

		[Test]
		public async Task a_unit_status_without_an_event_id_is_saved_every_time_as_before()
		{
			var first = await UnitStatus().SaveUnitStatus(UnitInput(eventId: ""), CancellationToken.None);
			var again = await UnitStatus().SaveUnitStatus(UnitInput(eventId: null), CancellationToken.None);

			Id(first).Should().Be("900");
			Id(again).Should().Be("901");
			_savedStates.Should().HaveCount(2);
			_receipts.Receipts.Should().BeEmpty();
		}

		[Test]
		public async Task a_unit_status_without_a_client_timestamp_is_not_deduplicated()
		{
			// Nothing tells a replay apart from the same status set again on purpose, so it is saved as before.
			var input = UnitInput();
			input.Timestamp = null;
			input.TimestampUtc = null;

			await UnitStatus().SaveUnitStatus(input, CancellationToken.None);
			await UnitStatus().SaveUnitStatus(input, CancellationToken.None);

			_savedStates.Should().HaveCount(2);
			_receipts.Receipts.Should().BeEmpty();
		}

		[Test]
		public async Task statuses_that_share_an_event_id_but_were_set_at_different_times_are_both_saved()
		{
			await UnitStatus().SaveUnitStatus(UnitInput(eventId: CallId.ToString(), timestamp: TappedAt), CancellationToken.None);
			await UnitStatus().SaveUnitStatus(UnitInput(eventId: CallId.ToString(), timestamp: TappedAt.AddMinutes(4)), CancellationToken.None);

			_savedStates.Should().HaveCount(2);
		}

		[Test]
		public async Task a_replay_while_the_first_attempt_is_still_saving_is_told_to_come_back_and_saves_nothing()
		{
			ActionResult<SaveUnitStatusResult> replay = null;
			_duringFirstUnitStateSave = async () => replay = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);

			var first = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);

			StatusCodeOf(replay).Should().Be(StatusCodes.Status409Conflict);
			Id(first).Should().Be("900");
			_savedStates.Should().ContainSingle();

			// Once the first attempt has answered, the next replay gets its state.
			var later = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);
			Id(later).Should().Be("900");
			_savedStates.Should().ContainSingle();
		}

		[Test]
		public async Task a_replay_after_the_riding_roles_failed_saves_only_the_roles()
		{
			_roleSaveFailures = 1;

			var first = await UnitStatus().SaveUnitStatus(UnitInput(withRoles: true), CancellationToken.None);

			first.Result.Should().BeOfType<BadRequestResult>();
			var receipt = _receipts.Receipts.Should().ContainSingle().Subject;
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Saved);
			receipt.EntityId.Should().Be(900);
			((InboundEmailPendingSteps)receipt.PendingSteps).Should().Be(InboundEmailPendingSteps.UnitStateRoles | InboundEmailPendingSteps.UnitLinkedStatuses);
			receipt.ClaimedOn.Should().BeNull("the failed attempt gave its claim back");

			var replay = await UnitStatus().SaveUnitStatus(UnitInput(withRoles: true), CancellationToken.None);

			Id(replay).Should().Be("900");
			_savedStates.Should().ContainSingle();
			_units.Verify(x => x.AddAllUnitStateRolesAsync(It.Is<List<UnitStateRole>>(r => r.Single().UnitStateId == 900 && r.Single().UserId == RiderId),
				It.IsAny<CancellationToken>()), Times.Exactly(2));
			_actionLogs.Verify(x => x.CreateUnitLinkedStatus(RiderId, DepartmentId, 900, "PMRT A1", It.IsAny<CancellationToken>()), Times.Once);
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_replay_after_a_linked_status_failed_sets_only_the_riders_not_yet_set_and_seats_nobody_again()
		{
			// Two riders: both are seated, the first rider's linked status commits, the second rider's fails.
			const string secondRider = "rider-2";
			var input = UnitInput(withRoles: true);
			input.Roles.Add(new UnitStatusRoleInput { UserId = secondRider, RoleId = "7", EventId = OutboxEventId });

			var linked = new Dictionary<string, ActionLog>();
			var failSecondRider = true;
			_actionLogs.Setup(x => x.CreateUnitLinkedStatus(It.IsAny<string>(), DepartmentId, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string userId, int departmentId, int unitStateId, string unitName, CancellationToken _) =>
				{
					if (userId == secondRider && failSecondRider)
					{
						failSecondRider = false;
						throw new InvalidOperationException("ActionLogs insert failed.");
					}

					return linked[userId] = new ActionLog { ActionLogId = 700 + linked.Count, UserId = userId, UnitStateId = unitStateId };
				});
			_actionLogs.Setup(x => x.GetLastActionLogForUserNoLimitAsync(It.IsAny<string>()))
				.ReturnsAsync((string userId) => linked.TryGetValue(userId, out var log) ? log : null);
			var seated = new List<UnitStateRole>();
			_units.Setup(x => x.AddAllUnitStateRolesAsync(It.IsAny<List<UnitStateRole>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((List<UnitStateRole> roles, CancellationToken _) => { seated.AddRange(roles); return roles.Count > 0; });
			_units.Setup(x => x.GetRolesForUnitStateAsync(900)).ReturnsAsync(() => seated.ToList());

			var first = await UnitStatus().SaveUnitStatus(input, CancellationToken.None);

			first.Result.Should().BeOfType<BadRequestResult>();
			((InboundEmailPendingSteps)_receipts.Receipts.Single().PendingSteps).Should().Be(InboundEmailPendingSteps.UnitLinkedStatuses,
				"the riders were seated before the linked statuses failed");

			var replay = await UnitStatus().SaveUnitStatus(input, CancellationToken.None);

			Id(replay).Should().Be("900");
			seated.Select(r => r.UserId).Should().Equal(RiderId, secondRider);
			_actionLogs.Verify(x => x.CreateUnitLinkedStatus(RiderId, DepartmentId, 900, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_actionLogs.Verify(x => x.CreateUnitLinkedStatus(secondRider, DepartmentId, 900, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
			linked.Keys.Should().BeEquivalentTo(new[] { RiderId, secondRider });
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_replay_after_some_riders_were_seated_seats_only_the_rest()
		{
			const string secondRider = "rider-2";
			var input = UnitInput(withRoles: true);
			input.Roles.Add(new UnitStatusRoleInput { UserId = secondRider, RoleId = "7", EventId = OutboxEventId });

			// Roles are saved one by one: the first rider's seat committed before the insert failed.
			var seated = new List<UnitStateRole>();
			var failOnce = true;
			_units.Setup(x => x.AddAllUnitStateRolesAsync(It.IsAny<List<UnitStateRole>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((List<UnitStateRole> roles, CancellationToken _) =>
				{
					if (failOnce)
					{
						failOnce = false;
						seated.Add(roles.First());
						throw new InvalidOperationException("UnitStateRoles insert failed.");
					}

					seated.AddRange(roles);
					return true;
				});
			_units.Setup(x => x.GetRolesForUnitStateAsync(900)).ReturnsAsync(() => seated.ToList());

			await UnitStatus().SaveUnitStatus(input, CancellationToken.None);
			var replay = await UnitStatus().SaveUnitStatus(input, CancellationToken.None);

			Id(replay).Should().Be("900");
			seated.Select(r => r.UserId).Should().Equal(RiderId, secondRider);
		}

		[Test]
		public async Task a_unit_status_whose_save_failed_after_the_row_committed_is_answered_with_that_row_on_replay()
		{
			// SetUnitStateAsync saves (and announces) the state before its protection step, then throws.
			_units.Setup(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), DepartmentId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Returns((UnitState state, int departmentId, CancellationToken _, bool __) =>
				{
					state.UnitStateId = 900 + _savedStates.Count;
					_savedStates.Add(state);
					throw new InvalidOperationException("Protected write blocked (broker_unavailable).");
				});

			var first = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);
			var replay = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);

			first.Result.Should().BeOfType<BadRequestResult>();
			Id(replay).Should().Be("900");
			_savedStates.Should().ContainSingle();
			_units.Verify(x => x.ProtectUnitStateAsync(DepartmentId, 900, It.IsAny<CancellationToken>()), Times.Once, "the replay finishes the protection the save never reached");
			_units.Verify(x => x.FinishUnitStateAsync(DepartmentId, 900, It.IsAny<CancellationToken>()), Times.Once, "and the call close-out after it");
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_unit_status_replay_whose_protection_is_still_refused_stays_open_for_the_next_replay()
		{
			_units.Setup(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), DepartmentId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Returns((UnitState state, int departmentId, CancellationToken _, bool __) =>
				{
					state.UnitStateId = 900 + _savedStates.Count;
					_savedStates.Add(state);
					throw new InvalidOperationException("Protected write blocked (broker_unavailable).");
				});
			_units.SetupSequence(x => x.ProtectUnitStateAsync(DepartmentId, 900, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Protected write blocked (broker_unavailable)."))
				.Returns(Task.CompletedTask);

			await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);
			var refused = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);

			refused.Result.Should().BeOfType<BadRequestResult>();
			var receipt = _receipts.Receipts.Single();
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Saved);
			((InboundEmailPendingSteps)receipt.PendingSteps).Should().HaveFlag(InboundEmailPendingSteps.ProtectedWrite);
			((InboundEmailPendingSteps)receipt.PendingSteps).Should().HaveFlag(InboundEmailPendingSteps.StatusFollowUp);
			_units.Verify(x => x.FinishUnitStateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
				"calls are not closed out on a state that is not protected yet");

			var replay = await UnitStatus().SaveUnitStatus(UnitInput(), CancellationToken.None);

			Id(replay).Should().Be("900");
			_savedStates.Should().ContainSingle();
			_units.Verify(x => x.FinishUnitStateAsync(DepartmentId, 900, It.IsAny<CancellationToken>()), Times.Once);
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_replay_seats_a_riders_second_role_when_only_the_first_was_saved()
		{
			// One rider holding two seats: the first seat committed, the second failed.
			_units.Setup(x => x.GetRolesForUnitAsync(UnitId)).ReturnsAsync(new List<UnitRole>
			{
				new UnitRole { UnitRoleId = 7, UnitId = UnitId, Name = "Driver" },
				new UnitRole { UnitRoleId = 8, UnitId = UnitId, Name = "Officer" }
			});
			var input = UnitInput(withRoles: true);
			input.Roles.Add(new UnitStatusRoleInput { UserId = RiderId, RoleId = "8", EventId = OutboxEventId });

			var seated = new List<UnitStateRole>();
			var failOnce = true;
			_units.Setup(x => x.AddAllUnitStateRolesAsync(It.IsAny<List<UnitStateRole>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((List<UnitStateRole> roles, CancellationToken _) =>
				{
					if (failOnce)
					{
						failOnce = false;
						seated.Add(roles.First());
						throw new InvalidOperationException("UnitStateRoles insert failed.");
					}

					seated.AddRange(roles);
					return true;
				});
			_units.Setup(x => x.GetRolesForUnitStateAsync(900)).ReturnsAsync(() => seated.ToList());

			await UnitStatus().SaveUnitStatus(input, CancellationToken.None);
			var replay = await UnitStatus().SaveUnitStatus(input, CancellationToken.None);

			Id(replay).Should().Be("900");
			seated.Select(r => r.Role).Should().Equal("Driver", "Officer");
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_replay_sets_the_linked_statuses_its_receipt_owes_even_after_the_setting_was_turned_off()
		{
			_roleSaveFailures = 1;
			await UnitStatus().SaveUnitStatus(UnitInput(withRoles: true), CancellationToken.None);
			((InboundEmailPendingSteps)_receipts.Receipts.Single().PendingSteps).Should().HaveFlag(InboundEmailPendingSteps.UnitLinkedStatuses);

			_settings.Setup(x => x.GetPersonnelOnUnitSetUnitStatusAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(false);
			await UnitStatus().SaveUnitStatus(UnitInput(withRoles: true), CancellationToken.None);

			_actionLogs.Verify(x => x.CreateUnitLinkedStatus(RiderId, DepartmentId, 900, "PMRT A1", It.IsAny<CancellationToken>()), Times.Once);
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_completed_replay_writes_no_riding_roles_or_linked_statuses_again()
		{
			await UnitStatus().SaveUnitStatus(UnitInput(withRoles: true), CancellationToken.None);
			await UnitStatus().SaveUnitStatus(UnitInput(withRoles: true), CancellationToken.None);

			_units.Verify(x => x.AddAllUnitStateRolesAsync(It.IsAny<List<UnitStateRole>>(), It.IsAny<CancellationToken>()), Times.Once);
			_actionLogs.Verify(x => x.CreateUnitLinkedStatus(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Once);
		}

		[Test]
		public async Task a_rejected_unit_status_leaves_nothing_for_its_retry_to_trip_on()
		{
			var invalid = UnitInput();
			invalid.Type = "not-a-status";

			var rejected = await UnitStatus().SaveUnitStatus(invalid, CancellationToken.None);

			rejected.Result.Should().BeOfType<BadRequestResult>();
			_receipts.Receipts.Should().BeEmpty();
		}

		#endregion SaveUnitStatus

		#region SavePersonStatus

		[Test]
		public async Task a_personnel_status_replayed_with_its_outbox_event_id_returns_the_first_status_without_saving_again()
		{
			var first = await PersonStatus().SavePersonStatus(PersonInput(), CancellationToken.None);
			var replay = await PersonStatus().SavePersonStatus(PersonInput(), CancellationToken.None);

			first.Result.Should().BeOfType<CreatedResult>();
			replay.Result.Should().BeOfType<CreatedResult>();
			Id(first).Should().Be("500");
			Id(replay).Should().Be("500");
			_savedLogs.Should().ContainSingle();

			var receipt = _receipts.Receipts.Should().ContainSingle().Subject;
			receipt.Kind.Should().Be((int)InboundEmailKinds.ApiPersonStatus);
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task responder_statuses_that_carry_the_call_id_as_their_event_id_are_all_saved()
		{
			// Responder sends the destination's call id in EventId, so different statuses to one call share it.
			var callEventId = CallId.ToString();

			await PersonStatus().SavePersonStatus(PersonInput(callEventId, (int)ActionTypes.RespondingToScene, TappedAt, callEventId), CancellationToken.None);
			await PersonStatus().SavePersonStatus(PersonInput(callEventId, (int)ActionTypes.OnScene, TappedAt.AddMinutes(6), callEventId), CancellationToken.None);
			await PersonStatus().SavePersonStatus(PersonInput(callEventId, (int)ActionTypes.RespondingToScene, TappedAt.AddMinutes(20), callEventId), CancellationToken.None);

			_savedLogs.Select(l => l.ActionTypeId).Should().Equal((int)ActionTypes.RespondingToScene, (int)ActionTypes.OnScene, (int)ActionTypes.RespondingToScene);
			_savedLogs.Should().OnlyContain(l => l.DestinationId == CallId);
		}

		[Test]
		public async Task a_personnel_status_whose_save_failed_after_the_row_committed_is_answered_with_that_row_on_replay()
		{
			_actionLogs.Setup(x => x.SaveActionLogAsync(It.IsAny<ActionLog>(), It.IsAny<CancellationToken>()))
				.Returns((ActionLog log, CancellationToken _) =>
				{
					log.ActionLogId = 500 + _savedLogs.Count;
					_savedLogs.Add(log);
					throw new InvalidOperationException("Protected write blocked (broker_unavailable).");
				});

			var first = await PersonStatus().SavePersonStatus(PersonInput(), CancellationToken.None);
			var replay = await PersonStatus().SavePersonStatus(PersonInput(), CancellationToken.None);

			first.Result.Should().BeOfType<BadRequestResult>();
			Id(replay).Should().Be("500");
			_savedLogs.Should().ContainSingle();
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_personnel_status_without_an_event_id_is_saved_every_time_as_before()
		{
			await PersonStatus().SavePersonStatus(PersonInput(eventId: ""), CancellationToken.None);
			await PersonStatus().SavePersonStatus(PersonInput(eventId: ""), CancellationToken.None);

			_savedLogs.Should().HaveCount(2);
			_receipts.Receipts.Should().BeEmpty();
		}

		#endregion SavePersonStatus

		#region SavePersonStaffing

		[Test]
		public async Task a_staffing_replayed_with_its_outbox_event_id_returns_the_first_staffing_without_saving_again()
		{
			var first = await PersonStaffing().SavePersonStaffing(StaffingInput(), CancellationToken.None);
			var replay = await PersonStaffing().SavePersonStaffing(StaffingInput(), CancellationToken.None);

			first.Result.Should().BeOfType<CreatedResult>();
			Id(first).Should().Be("300");
			Id(replay).Should().Be("300");
			_savedStaffings.Should().ContainSingle();

			var receipt = _receipts.Receipts.Should().ContainSingle().Subject;
			receipt.Kind.Should().Be((int)InboundEmailKinds.ApiPersonStaffing);
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_staffing_whose_save_failed_after_the_row_committed_is_answered_with_that_row_on_replay()
		{
			_userStates.Setup(x => x.CreateUserStateAsync(It.IsAny<UserState>(), It.IsAny<CancellationToken>()))
				.Returns((UserState state, CancellationToken _) =>
				{
					state.UserStateId = 300 + _savedStaffings.Count;
					_savedStaffings.Add(state);
					throw new InvalidOperationException("Protected write blocked (broker_unavailable).");
				});

			var first = await PersonStaffing().SavePersonStaffing(StaffingInput(), CancellationToken.None);
			var replay = await PersonStaffing().SavePersonStaffing(StaffingInput(), CancellationToken.None);

			first.Result.Should().BeOfType<BadRequestResult>();
			Id(replay).Should().Be("300");
			_savedStaffings.Should().ContainSingle();
			_savedStaffings.Single().State.Should().Be((int)UserStateTypes.Committed);
			_savedStaffings.Single().UserId.Should().Be(UserId);
			_userStates.Verify(x => x.ProtectUserStateAsync(DepartmentId, 300, It.IsAny<CancellationToken>()), Times.Once, "the replay finishes the protection the save never reached");
			_userStates.Verify(x => x.AnnounceUserStateAsync(DepartmentId, 300), Times.Once, "and sends the staffing event the save never sent");
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_staffing_replay_whose_protection_is_still_refused_stays_open_for_the_next_replay()
		{
			_userStates.Setup(x => x.CreateUserStateAsync(It.IsAny<UserState>(), It.IsAny<CancellationToken>()))
				.Returns((UserState state, CancellationToken _) =>
				{
					state.UserStateId = 300 + _savedStaffings.Count;
					_savedStaffings.Add(state);
					throw new InvalidOperationException("Protected write blocked (broker_unavailable).");
				});
			_userStates.SetupSequence(x => x.ProtectUserStateAsync(DepartmentId, 300, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Protected write blocked (broker_unavailable)."))
				.Returns(Task.CompletedTask);

			await PersonStaffing().SavePersonStaffing(StaffingInput(), CancellationToken.None);
			var refused = await PersonStaffing().SavePersonStaffing(StaffingInput(), CancellationToken.None);

			refused.Result.Should().BeOfType<BadRequestResult>();
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Saved, "a staffing is not answered for until it is protected");
			_userStates.Verify(x => x.AnnounceUserStateAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never, "an unprotected staffing is not announced");

			var replay = await PersonStaffing().SavePersonStaffing(StaffingInput(), CancellationToken.None);

			Id(replay).Should().Be("300");
			_savedStaffings.Should().ContainSingle();
			_userStates.Verify(x => x.AnnounceUserStateAsync(DepartmentId, 300), Times.Once);
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task a_staffing_without_an_event_id_is_saved_every_time_as_before()
		{
			await PersonStaffing().SavePersonStaffing(StaffingInput(eventId: ""), CancellationToken.None);
			await PersonStaffing().SavePersonStaffing(StaffingInput(eventId: ""), CancellationToken.None);

			_savedStaffings.Should().HaveCount(2);
			_receipts.Receipts.Should().BeEmpty();
		}

		[Test]
		public async Task a_staffing_with_an_unreadable_type_is_refused_before_it_is_claimed()
		{
			var input = StaffingInput();
			input.Type = "Committed";

			var result = await PersonStaffing().SavePersonStaffing(input, CancellationToken.None);

			result.Result.Should().BeOfType<BadRequestResult>();
			_savedStaffings.Should().BeEmpty();
			_receipts.Receipts.Should().BeEmpty();
		}

		#endregion SavePersonStaffing

		#region StatusWriteReplayKeys

		[Test]
		public void replay_keys_are_untracked_without_an_event_id_or_a_client_timestamp()
		{
			StatusWriteReplayKeys.ForUnitStatus(UnitId, UnitInput(eventId: null)).Should().BeNull();
			StatusWriteReplayKeys.ForUnitStatus(UnitId, UnitInput(eventId: "   ")).Should().BeNull();

			var noTime = UnitInput();
			noTime.Timestamp = null;
			noTime.TimestampUtc = null;
			StatusWriteReplayKeys.ForUnitStatus(UnitId, noTime).Should().BeNull();

			var utcOnly = UnitInput();
			utcOnly.Timestamp = null;
			StatusWriteReplayKeys.ForUnitStatus(UnitId, utcOnly).Should().NotBeNull();

			StatusWriteReplayKeys.ForPersonStatus(PersonInput(eventId: "")).Should().BeNull();
			StatusWriteReplayKeys.ForPersonStaffing(StaffingInput(eventId: "")).Should().BeNull();
		}

		[Test]
		public void replay_keys_match_only_the_same_write()
		{
			var key = StatusWriteReplayKeys.ForUnitStatus(UnitId, UnitInput());

			StatusWriteReplayKeys.ForUnitStatus(UnitId, UnitInput()).Should().Be(key);
			StatusWriteReplayKeys.ForUnitStatus(UnitId + 1, UnitInput()).Should().NotBe(key);
			StatusWriteReplayKeys.ForUnitStatus(UnitId, UnitInput(eventId: OutboxEventId + "x")).Should().NotBe(key);
			StatusWriteReplayKeys.ForUnitStatus(UnitId, UnitInput(timestamp: TappedAt.AddMilliseconds(1))).Should().NotBe(key);

			var otherType = UnitInput();
			otherType.Type = ((int)UnitStateTypes.OnScene).ToString();
			StatusWriteReplayKeys.ForUnitStatus(UnitId, otherType).Should().NotBe(key);

			var otherDestination = UnitInput();
			otherDestination.RespondingTo = CallId.ToString();
			StatusWriteReplayKeys.ForUnitStatus(UnitId, otherDestination).Should().NotBe(key);

			var otherMember = PersonInput();
			otherMember.UserId = "someone-else";
			StatusWriteReplayKeys.ForPersonStatus(otherMember).Should().NotBe(StatusWriteReplayKeys.ForPersonStatus(PersonInput()));
		}

		[Test]
		public void replay_receipt_keys_are_per_department_fit_the_column_and_carry_no_member_id()
		{
			var key = StatusWriteReplayKeys.ForPersonStatus(PersonInput());

			var here = InboundEmailReceiptsService.ApiRequestKey(InboundEmailKinds.ApiPersonStatus, DepartmentId, key);
			var elsewhere = InboundEmailReceiptsService.ApiRequestKey(InboundEmailKinds.ApiPersonStatus, DepartmentId + 1, key);
			var asStaffing = InboundEmailReceiptsService.ApiRequestKey(InboundEmailKinds.ApiPersonStaffing, DepartmentId, key);

			here.Should().NotBe(elsewhere);
			here.Should().NotBe(asStaffing);
			here.Should().StartWith($"{(int)InboundEmailKinds.ApiPersonStatus}:{DepartmentId}:sha256:");
			here.Length.Should().BeLessThanOrEqualTo(InboundEmailReceipt.ProviderMessageIdMaxLength);
			here.Should().NotContain(UserId).And.NotContain(OutboxEventId);
		}

		#endregion StatusWriteReplayKeys
	}
}
