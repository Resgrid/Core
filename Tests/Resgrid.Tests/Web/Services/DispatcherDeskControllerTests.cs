using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.DispatcherDesk;
using Resgrid.Web.ServicesCore.Helpers;
using Task = System.Threading.Tasks.Task;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>v4 DispatcherDesk (M0324): who may use each endpoint, and how desk entities reach the Dispatch app.</summary>
	[TestFixture]
	public class DispatcherDeskControllerTests
	{
		private const int DepartmentId = 10;
		private const string Me = "dispatcher-1";
		private const string Other = "dispatcher-2";

		private Mock<IDispatchAccessService> _access;
		private Mock<IDispatcherDeskConfigService> _config;
		private Mock<IDispatcherPresenceService> _presence;
		private Mock<IDispatcherRoutingService> _routing;
		private Mock<IDispatcherBreaksService> _breaks;
		private Mock<IDispatcherConversationService> _conversation;
		private Mock<IDispatcherVoiceService> _voice;
		private Mock<IShiftDispatchersService> _shiftDispatchers;
		private Mock<IDispatcherDeskPushProvider> _push;
		private Mock<IShiftsService> _shifts;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<ICallsService> _calls;
		private DispatcherDeskConfig _deskConfig;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_deskConfig = new DispatcherDeskConfig { Enabled = true };

			_access = new Mock<IDispatchAccessService>();
			_access.Setup(x => x.CanUseDispatchAsync(DepartmentId, Me)).ReturnsAsync(true);

			_config = new Mock<IDispatcherDeskConfigService>();
			_config.Setup(x => x.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _deskConfig);

			_presence = new Mock<IDispatcherPresenceService>();
			_presence.Setup(x => x.GetMemberAsync(DepartmentId, Me, It.IsAny<DateTime>()))
				.ReturnsAsync(new DispatcherDeskMember { UserId = Me, DisplayState = DispatcherDisplayStates.Available, Capacity = 1 });

			_routing = new Mock<IDispatcherRoutingService>();
			_breaks = new Mock<IDispatcherBreaksService>();
			_conversation = new Mock<IDispatcherConversationService>();
			_voice = new Mock<IDispatcherVoiceService>();
			_shiftDispatchers = new Mock<IShiftDispatchersService>();
			_push = new Mock<IDispatcherDeskPushProvider>();
			_shifts = new Mock<IShiftsService>();
			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.GetShiftManagementScopeAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(ShiftManagementScope.None());

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });

			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Dictionary<string, UserProfile>
			{
				[Me] = new UserProfile { UserId = Me, FirstName = "Dana", LastName = "Desk" },
				[Other] = new UserProfile { UserId = Other, FirstName = "Sam", LastName = "Second" }
			});

			_calls = new Mock<ICallsService>();
			_calls.Setup(x => x.CallPriorityToColorAsync(It.IsAny<int>(), DepartmentId)).ReturnsAsync("#FF0000");

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("DispatcherDeskControllerTests").Start();
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private DispatcherDeskController Controller() =>
			new DispatcherDeskController(_access.Object, _config.Object, _presence.Object, _routing.Object, _breaks.Object, _conversation.Object, _voice.Object,
				_shiftDispatchers.Object, _push.Object, _shifts.Object, _authorization.Object, _departments.Object, _profiles.Object, _calls.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = ClaimsAuthorizationHelper._httpContextAccessor.HttpContext }
			};

		private void NotADispatcher() => _access.Setup(x => x.CanUseDispatchAsync(DepartmentId, Me)).ReturnsAsync(false);

		private void Supervisor(bool canSupervise = true) =>
			_presence.Setup(x => x.CanSuperviseAsync(DepartmentId, Me)).ReturnsAsync(canSupervise);

		private static T Value<T>(ActionResult<T> result) where T : class =>
			result.Value ?? (T)((ObjectResult)result.Result).Value;

		private static int? StatusOf<T>(ActionResult<T> result) =>
			result.Result is ObjectResult objectResult ? objectResult.StatusCode ?? StatusCodes.Status200OK : (result.Result as StatusCodeResult)?.StatusCode;

		private DispatcherQueueItem Item(string id, DispatcherQueueItemKinds kind, DispatcherQueueItemStates state = DispatcherQueueItemStates.Accepted,
			string assignedUserId = Me) => new DispatcherQueueItem
			{
				DispatcherQueueItemId = id,
				DepartmentId = DepartmentId,
				Kind = (int)kind,
				State = (int)state,
				AssignedUserId = assignedUserId,
				SourceAddress = "+15551234567",
				CreatedOn = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc),
				LastActivityOn = new DateTime(2026, 11, 19, 16, 5, 0, DateTimeKind.Utc)
			};

		private void Detail(DispatcherQueueItem item, List<DispatcherQueueEvent> events = null, List<DispatcherConversationMessage> messages = null, Call call = null) =>
			_routing.Setup(x => x.GetItemDetailAsync(DepartmentId, item.DispatcherQueueItemId, It.IsAny<string>())).ReturnsAsync(new DispatcherQueueItemDetail
			{
				Item = item,
				Events = events ?? new List<DispatcherQueueEvent>(),
				Messages = messages ?? new List<DispatcherConversationMessage>(),
				Call = call
			});

		#region Access

		[Test]
		public async Task A_caller_who_may_not_dispatch_gets_the_settings_only()
		{
			NotADispatcher();

			var data = Value(await Controller().GetDesk()).Data;

			data.IsDispatcher.Should().BeFalse();
			data.Enabled.Should().BeTrue();
			data.Config.Should().NotBeNull();
			data.Members.Should().BeEmpty();
			data.Queue.Should().BeEmpty();
			data.Me.Should().BeNull();
			_presence.Verify(x => x.GetDeskAsync(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
		}

		[Test]
		public async Task Desk_reads_and_actions_are_403_for_a_caller_who_may_not_dispatch()
		{
			NotADispatcher();
			var controller = Controller();

			StatusOf(await controller.GetQueue()).Should().Be(StatusCodes.Status403Forbidden);
			StatusOf(await controller.GetItem("i1")).Should().Be(StatusCodes.Status403Forbidden);
			StatusOf(await controller.GetCoverage(null, null, null)).Should().Be(StatusCodes.Status403Forbidden);
			StatusOf(await controller.GetConfig()).Should().Be(StatusCodes.Status403Forbidden);

			var signIn = await controller.SignIn(new DispatcherSignInInput(), CancellationToken.None);
			StatusOf(signIn).Should().Be(StatusCodes.Status403Forbidden);
			Value(signIn).Error.Should().Be(nameof(DispatcherDeskErrors.NotADispatcher), "the app reads a 4xx desk body as the refusal");

			StatusOf(await controller.AcceptItem(new DispatcherItemInput { ItemId = "i1" }, CancellationToken.None)).Should().Be(StatusCodes.Status403Forbidden);
			_presence.Verify(x => x.SignInAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DispatcherSignInRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.AcceptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Stats_are_for_supervisors()
		{
			Supervisor(false);
			StatusOf(await Controller().GetStats(null, null)).Should().Be(StatusCodes.Status403Forbidden);

			Supervisor();
			_routing.Setup(x => x.GetStatsAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new DispatcherQueueStats
			{
				Created = 4,
				CreatedByKind = new Dictionary<int, int> { [(int)DispatcherQueueItemKinds.Text] = 3 },
				Dispatchers = { new DispatcherDeskStats { UserId = Other, Accepted = 2 } }
			});

			var data = Value(await Controller().GetStats(null, null)).Data;
			data.Created.Should().Be(4);
			data.CreatedByKind.Should().ContainKey("3").WhoseValue.Should().Be(3);
			data.Dispatchers.Single().Name.Should().Be("Sam Second");
		}

		[Test]
		public void Every_planned_endpoint_exists_with_its_policy()
		{
			var shiftEndpoints = new[] { "GetShiftDispatchers", "SignupForDay", "WithdrawFromDay", "AssignToDay", "RemoveFromDay", "ReviewDaySignup" };
			var expected = new[]
			{
				"GetDesk", "GetQueue", "GetItem", "GetCoverage", "GetBreaks", "GetShiftDispatchers", "GetStats", "GetConfig",
				"SignIn", "SignOff", "Heartbeat", "SetAvailable", "SetAway", "SetCallbackNumber", "RegisterDevice", "UnregisterDevice",
				"AcceptItem", "DeclineItem", "ReleaseItem", "TransferItem", "CompleteItem", "LinkCall", "SendMessage", "AddNote",
				"ConnectVoice", "CallBack", "SaveBreak", "CancelBreak", "StartBreak", "EndBreak", "PlanBreaks", "SignupForDay",
				"WithdrawFromDay", "AssignToDay", "RemoveFromDay"
			};

			var actions = typeof(DispatcherDeskController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
				.Select(m => new
				{
					Method = m,
					Route = m.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template).SingleOrDefault(),
					Policy = m.GetCustomAttribute<AuthorizeAttribute>()?.Policy
				})
				.Where(x => x.Route != null)
				.ToList();

			actions.Select(x => x.Route).Should().Contain(expected);

			foreach (var action in actions)
			{
				action.Policy.Should().Be(shiftEndpoints.Contains(action.Route) ? ResgridResources.Shift_View : ResgridResources.Call_View, action.Route);

				var isGet = action.Method.GetCustomAttributes<HttpGetAttribute>().Any();
				if (!isGet)
					action.Method.ReturnType.Should().Be(typeof(System.Threading.Tasks.Task<ActionResult<DispatcherDeskActionResult>>), action.Route);
			}
		}

		#endregion Access

		#region Mapping

		[Test]
		public async Task GetDesk_maps_the_board_for_the_app()
		{
			Supervisor();
			var call = new Call { CallId = 77, DepartmentId = DepartmentId, Number = "26-77", Name = "Structure fire", Priority = 3 };
			var pending = Item("pending", DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Queued, null);
			pending.CallId = 77;
			var text = Item("text", DispatcherQueueItemKinds.Text);
			var voice = Item("voice", DispatcherQueueItemKinds.Voice, DispatcherQueueItemStates.Offered);
			voice.ExternalReference = "CA1";

			_calls.Setup(x => x.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(call);
			Detail(text, messages: new List<DispatcherConversationMessage>
			{
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Inbound, CreatedOn = DateTime.UtcNow.AddMinutes(-5) },
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Outbound, CreatedOn = DateTime.UtcNow.AddMinutes(-4) },
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Note, CreatedOn = DateTime.UtcNow.AddMinutes(-3) },
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Inbound, CreatedOn = DateTime.UtcNow.AddMinutes(-2) }
			});
			// A dispatcher-leg ending (their phone was busy) is not the caller hanging up.
			Detail(voice, events: new List<DispatcherQueueEvent>
			{
				new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceEnded, UserId = Other, OccurredOn = DateTime.UtcNow }
			});

			var meMember = new DispatcherDeskMember
			{
				UserId = Me,
				DisplayState = DispatcherDisplayStates.Busy,
				IsOnDuty = true,
				Presence = new DispatcherPresence { UserId = Me, State = (int)DispatcherPresenceStates.Available, CallbackNumber = "+15550001111", DutyEndsOn = new DateTime(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc) },
				Duty = new DispatcherDutyWindow { UserId = Me, ShiftId = 5, ShiftDayId = 50, Day = new DateTime(2026, 11, 19), StartUtc = new DateTime(2026, 11, 19, 14, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc) },
				ActiveItemCount = 1,
				Capacity = 1
			};
			var otherMember = new DispatcherDeskMember
			{
				UserId = Other,
				DisplayState = DispatcherDisplayStates.OnBreak,
				IsOnDuty = true,
				Presence = new DispatcherPresence { UserId = Other, State = (int)DispatcherPresenceStates.OnBreak, CallbackNumber = "+15550002222" },
				ActiveBreak = new DispatcherBreak { DispatcherBreakId = "b1", UserId = Other, State = (int)DispatcherBreakStates.Active, DurationMinutes = 30, StartedOn = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc), ExpectedReturnOn = new DateTime(2026, 11, 19, 16, 30, 0, DateTimeKind.Utc) }
			};

			_presence.Setup(x => x.GetDeskAsync(DepartmentId, It.IsAny<DateTime>())).ReturnsAsync(new DispatcherDeskSnapshot
			{
				Enabled = true,
				GeneratedOn = DateTime.UtcNow,
				Members = { meMember, otherMember },
				Queue = { voice, text, pending },
				MinimumAvailable = 2,
				AvailableCount = 1,
				OnBreakCount = 1,
				OnDutyCount = 2
			});
			_breaks.Setup(x => x.GetOpenBreaksForUserAsync(DepartmentId, Me, It.IsAny<DateTime>())).ReturnsAsync(new List<DispatcherBreak>
			{
				new DispatcherBreak { DispatcherBreakId = "b2", UserId = Me, State = (int)DispatcherBreakStates.Scheduled, DurationMinutes = 30, ScheduledStartOn = new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc) }
			});

			var data = Value(await Controller().GetDesk()).Data;

			data.IsDispatcher.Should().BeTrue();
			data.BelowMinimum.Should().BeTrue();
			data.Me.UserId.Should().Be(Me);
			data.Me.Name.Should().Be("Dana Desk");
			data.Me.CanSupervise.Should().BeTrue();
			data.Me.CallbackNumber.Should().Be("+15550001111");
			data.Me.DutyEndsOn.Should().Be("2026-11-20T02:00:00Z");
			data.Me.Duty.Day.Should().Be("2026-11-19");
			data.Me.Duty.StartUtc.Should().Be("2026-11-19T14:00:00Z");

			var other = data.Members.Single(x => x.UserId == Other);
			other.CallbackNumber.Should().BeNull("a dispatcher's own phone is never shown to the others");
			other.CanSupervise.Should().BeFalse("CanSupervise is about the caller");
			other.ActiveBreak.ExpectedReturnOn.Should().Be("2026-11-19T16:30:00Z");
			other.ActiveBreak.UserName.Should().Be("Sam Second");

			data.Queue.Select(x => x.DispatcherQueueItemId).Should().Equal("voice", "text", "pending");
			var mappedPending = data.Queue.Single(x => x.DispatcherQueueItemId == "pending");
			mappedPending.CallNumber.Should().Be("26-77");
			mappedPending.CallName.Should().Be("Structure fire");
			mappedPending.CallPriority.Should().Be(3);
			mappedPending.CallPriorityColor.Should().Be("#FF0000");
			mappedPending.CreatedOn.Should().Be("2026-11-19T16:00:00Z");

			var mappedText = data.Queue.Single(x => x.DispatcherQueueItemId == "text");
			mappedText.MessageCount.Should().Be(3, "desk notes are not conversation messages");
			mappedText.UnreadCount.Should().Be(1);
			mappedText.AssignedUserName.Should().Be("Dana Desk");

			data.Queue.Single(x => x.DispatcherQueueItemId == "voice").CallerWaiting.Should().BeTrue();

			data.MyBreaks.Single().ScheduledEndOn.Should().Be("2026-11-19T18:30:00Z");
		}

		[Test]
		public async Task GetItem_answers_404_outside_the_department_and_maps_the_history()
		{
			_routing.Setup(x => x.GetItemDetailAsync(DepartmentId, "missing", It.IsAny<string>())).ReturnsAsync((DispatcherQueueItemDetail)null);
			(await Controller().GetItem("missing")).Result.Should().BeOfType<NotFoundResult>();

			var voice = Item("voice", DispatcherQueueItemKinds.Voice);
			voice.ExternalReference = "CA1";
			Detail(voice, events: new List<DispatcherQueueEvent>
			{
				new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.Accepted, UserId = Me, OccurredOn = new DateTime(2026, 11, 19, 16, 1, 0, DateTimeKind.Utc) },
				new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceEnded, UserId = null, Detail = "Caller hung up", OccurredOn = new DateTime(2026, 11, 19, 16, 2, 0, DateTimeKind.Utc) }
			});

			var data = Value(await Controller().GetItem("voice")).Data;

			data.Item.CallerWaiting.Should().BeFalse("the caller's own leg ended");
			data.CanConnectVoice.Should().BeTrue("the caller can still be called back");
			data.CanReply.Should().BeFalse();
			data.Events.Select(x => x.UserName).Should().Equal("Dana Desk", null);
			data.Events.Last().OccurredOn.Should().Be("2026-11-19T16:02:00Z");
		}

		[TestCase(DispatcherQueueItemKinds.Text, "+15551234567", true)]
		[TestCase(DispatcherQueueItemKinds.Chat, "700001122", false)]
		[TestCase(DispatcherQueueItemKinds.Chat, "+15551234567", false)]
		[TestCase(DispatcherQueueItemKinds.Email, "pat@example.com", false)]
		public async Task Call_back_is_offered_only_for_a_text_or_phone_sender(DispatcherQueueItemKinds kind, string sender, bool canCallBack)
		{
			// A chat's sender is the chat app's user id (a Telegram id is all digits), never a number to ring.
			var item = Item("item", kind);
			item.SourceAddress = sender;
			Detail(item);

			Value(await Controller().GetItem("item")).Data.CanConnectVoice.Should().Be(canCallBack);
		}

		[Test]
		public void Unread_messages_are_the_inbound_ones_since_the_last_reply()
		{
			var t = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc);
			DispatcherDeskController.UnreadCount(new[]
			{
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Inbound, CreatedOn = t },
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Inbound, CreatedOn = t.AddMinutes(1) }
			}).Should().Be(2);

			DispatcherDeskController.UnreadCount(new[]
			{
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Inbound, CreatedOn = t },
				new DispatcherConversationMessage { Direction = (int)DispatcherMessageDirections.Outbound, CreatedOn = t.AddMinutes(1) }
			}).Should().Be(0);

			DispatcherDeskController.UnreadCount(null).Should().Be(0);
		}

		[Test]
		public async Task A_refusal_carries_the_error_name_warnings_and_the_callers_member()
		{
			_presence.Setup(x => x.SignInAsync(DepartmentId, Me, It.IsAny<DispatcherSignInRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherDeskMember>.Fail(DispatcherDeskErrors.NotOnDuty, null, new List<DispatcherDeskWarning>
				{
					new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.CoverageBelowMinimum, Available = 0, Minimum = 1, StartUtc = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc) }
				}));

			var result = await Controller().SignIn(new DispatcherSignInInput { DeviceKind = "phone", Unscheduled = true, DutyEndsOn = "2026-11-20T02:00:00Z" },
				CancellationToken.None);

			StatusOf(result).Should().Be(StatusCodes.Status200OK, "refusals are answers, not HTTP errors");
			var action = Value(result);
			action.Success.Should().BeFalse();
			action.Error.Should().Be("NotOnDuty");
			action.Warnings.Single().Kind.Should().Be("CoverageBelowMinimum");
			action.Warnings.Single().StartUtc.Should().Be("2026-11-19T16:00:00Z");
			action.Me.UserId.Should().Be(Me);
			_presence.Verify(x => x.SignInAsync(DepartmentId, Me, It.Is<DispatcherSignInRequest>(r => r.Unscheduled && r.DeviceKind == "phone" &&
				r.DutyEndsOn == new DateTime(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc)), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Sign_in_with_a_bad_duty_end_is_refused()
		{
			var action = Value(await Controller().SignIn(new DispatcherSignInInput { DutyEndsOn = "tomorrow-ish" }, CancellationToken.None));

			action.Error.Should().Be("InvalidInput");
			_presence.Verify(x => x.SignInAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DispatcherSignInRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Mapping

		#region Work

		[Test]
		public async Task Moving_work_someone_else_holds_needs_a_supervisor()
		{
			var held = Item("held", DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Accepted, Other);
			Detail(held);
			Supervisor(false);

			var transfer = Value(await Controller().TransferItem(new DispatcherTransferInput { ItemId = "held", ToUserId = "dispatcher-3" }, CancellationToken.None));
			var release = Value(await Controller().ReleaseItem(new DispatcherItemInput { ItemId = "held" }, CancellationToken.None));

			transfer.Error.Should().Be("NotAssignedToYou");
			release.Error.Should().Be("NotAssignedToYou");
			_routing.Verify(x => x.TransferAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			Supervisor();
			_routing.Setup(x => x.TransferAsync(DepartmentId, "held", Me, "dispatcher-3", "busy", It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Ok(held));

			var moved = Value(await Controller().TransferItem(new DispatcherTransferInput { ItemId = "held", ToUserId = "dispatcher-3", Note = " busy " }, CancellationToken.None));
			moved.Success.Should().BeTrue();
			moved.Item.DispatcherQueueItemId.Should().Be("held");
		}

		[Test]
		public async Task The_holder_releases_their_own_work()
		{
			var mine = Item("mine", DispatcherQueueItemKinds.Text);
			Detail(mine);
			_routing.Setup(x => x.ReleaseAsync(DepartmentId, "mine", Me, It.IsAny<CancellationToken>())).ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Ok(mine));

			Value(await Controller().ReleaseItem(new DispatcherItemInput { ItemId = "mine" }, CancellationToken.None)).Success.Should().BeTrue();
			_presence.Verify(x => x.CanSuperviseAsync(DepartmentId, Me), Times.AtMostOnce(), "only the action result's own member asks");
		}

		[Test]
		public async Task Complete_and_link_refuse_a_call_of_another_department()
		{
			_calls.Setup(x => x.GetCallByIdAsync(900, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 900, DepartmentId = 99 });

			Value(await Controller().CompleteItem(new DispatcherCompleteInput { ItemId = "i1", Outcome = (int)DispatcherQueueItemOutcomes.CallCreated, CallId = 900 },
				CancellationToken.None)).Error.Should().Be("InvalidInput");
			Value(await Controller().LinkCall(new DispatcherLinkCallInput { ItemId = "i1", CallId = 900 }, CancellationToken.None)).Error.Should().Be("InvalidInput");
			Value(await Controller().CompleteItem(new DispatcherCompleteInput { ItemId = "i1", Outcome = 0 }, CancellationToken.None)).Error.Should().Be("InvalidInput");

			_routing.Verify(x => x.CompleteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DispatcherQueueItemOutcomes>(), It.IsAny<int?>(),
				It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.LinkCallAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Send_message_answers_the_message_and_the_refreshed_item()
		{
			var text = Item("text", DispatcherQueueItemKinds.Text);
			Detail(text);
			_conversation.Setup(x => x.SendReplyAsync(DepartmentId, "text", Me, "On our way", It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherConversationMessage>.Ok(new DispatcherConversationMessage
				{
					DispatcherConversationMessageId = "m1",
					Direction = (int)DispatcherMessageDirections.Outbound,
					Body = "On our way",
					UserId = Me,
					DeliveryState = (int)DispatcherMessageDeliveryStates.Sent,
					CreatedOn = new DateTime(2026, 11, 19, 16, 10, 0, DateTimeKind.Utc)
				}));

			var action = Value(await Controller().SendMessage(new DispatcherMessageInput { ItemId = "text", Body = "  On our way " }, CancellationToken.None));

			action.Success.Should().BeTrue();
			action.Message.UserName.Should().Be("Dana Desk");
			action.Message.CreatedOn.Should().Be("2026-11-19T16:10:00Z");
			action.Item.DispatcherQueueItemId.Should().Be("text");

			Value(await Controller().SendMessage(new DispatcherMessageInput { ItemId = "text", Body = new string('x', DispatcherConversationMessage.BodyMaxLength + 1) },
				CancellationToken.None)).Error.Should().Be("InvalidInput");
		}

		[Test]
		public async Task Devices_are_registered_on_the_dispatch_subscriber()
		{
			Value(await Controller().RegisterDevice(new DispatcherDeviceInput { Platform = 9, Token = "t" }, CancellationToken.None)).Error.Should().Be("InvalidInput");
			Value(await Controller().RegisterDevice(new DispatcherDeviceInput { Platform = 2, Token = " " }, CancellationToken.None)).Error.Should().Be("InvalidInput");

			_push.Setup(x => x.RegisterDeviceAsync(DepartmentId, Me, 2, "fcm-token", It.IsAny<CancellationToken>())).ReturnsAsync(true);
			Value(await Controller().RegisterDevice(new DispatcherDeviceInput { Platform = 2, Token = " fcm-token " }, CancellationToken.None)).Success.Should().BeTrue();

			_push.Setup(x => x.UnregisterDeviceAsync(DepartmentId, Me, 1, "apns", It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Value(await Controller().UnregisterDevice(new DispatcherDeviceInput { Platform = 1, Token = "apns" }, CancellationToken.None)).Error.Should().Be("ChannelUnavailable");
		}

		[Test]
		public async Task Answer_and_call_back_go_through_the_voice_service()
		{
			var voice = Item("voice", DispatcherQueueItemKinds.Voice);
			Detail(voice);
			_voice.Setup(x => x.ConnectAsync(DepartmentId, "voice", Me, "+15550001111", It.IsAny<CancellationToken>())).ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Ok(voice));
			_voice.Setup(x => x.CallBackAsync(DepartmentId, "voice", Me, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.NoCallbackNumber));

			Value(await Controller().ConnectVoice(new DispatcherVoiceInput { ItemId = "voice", CallbackNumber = "+15550001111" }, CancellationToken.None))
				.Success.Should().BeTrue();
			Value(await Controller().CallBack(new DispatcherVoiceInput { ItemId = "voice" }, CancellationToken.None)).Error.Should().Be("NoCallbackNumber");
		}

		#endregion Work

		#region Breaks

		[Test]
		public async Task Someone_elses_break_needs_a_supervisor()
		{
			Supervisor(false);

			Value(await Controller().StartBreak(new DispatcherStartBreakInput { UserId = Other, DurationMinutes = 15 }, CancellationToken.None))
				.Error.Should().Be("NotPermitted");
			Value(await Controller().EndBreak(new DispatcherEndBreakInput { UserId = Other }, CancellationToken.None)).Error.Should().Be("NotPermitted");
			_breaks.Verify(x => x.StartBreakAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			Supervisor();
			_breaks.Setup(x => x.EndBreakAsync(DepartmentId, Other, Me, DispatcherBreakEndReasons.Supervisor, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherBreak>.Ok(new DispatcherBreak { DispatcherBreakId = "b1", UserId = Other, State = (int)DispatcherBreakStates.Completed }));

			var ended = Value(await Controller().EndBreak(new DispatcherEndBreakInput { UserId = Other }, CancellationToken.None));
			ended.Success.Should().BeTrue();
			ended.Break.UserName.Should().Be("Sam Second");
		}

		[Test]
		public async Task A_dispatcher_takes_and_ends_their_own_break()
		{
			_breaks.Setup(x => x.StartBreakAsync(DepartmentId, Me, null, (int)DispatcherBreakTypes.Meal, 30, true, Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherBreak>.Ok(new DispatcherBreak { DispatcherBreakId = "b9", UserId = Me, State = (int)DispatcherBreakStates.Active, DurationMinutes = 30 }));
			_breaks.Setup(x => x.EndBreakAsync(DepartmentId, Me, Me, DispatcherBreakEndReasons.Returned, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherBreak>.Ok(new DispatcherBreak { DispatcherBreakId = "b9", UserId = Me, State = (int)DispatcherBreakStates.Completed }));

			var started = Value(await Controller().StartBreak(new DispatcherStartBreakInput { BreakType = (int)DispatcherBreakTypes.Meal, DurationMinutes = 30, ReturnWork = true },
				CancellationToken.None));
			started.Break.DispatcherBreakId.Should().Be("b9");

			Value(await Controller().EndBreak(new DispatcherEndBreakInput(), CancellationToken.None)).Success.Should().BeTrue();

			Value(await Controller().StartBreak(new DispatcherStartBreakInput(), CancellationToken.None)).Error.Should().Be("InvalidInput", "a break on the spot needs its length");
		}

		[Test]
		public async Task Planning_your_own_break_needs_a_shift_that_allows_it()
		{
			Supervisor(false);
			var window = new DispatcherDutyWindow { UserId = Me, ShiftId = 5, ShiftDayId = 50 };
			_shiftDispatchers.Setup(x => x.GetDayRosterAsync(DepartmentId, 50)).ReturnsAsync(new List<DispatcherDutyWindow> { window });
			_shiftDispatchers.Setup(x => x.GetSettingsForShiftAsync(5)).ReturnsAsync(new ShiftDispatchSetting { ShiftId = 5, DepartmentId = DepartmentId, IsEnabled = true });
			var input = new DispatcherSaveBreakInput { ShiftDayId = 50, BreakType = (int)DispatcherBreakTypes.Meal, ScheduledStartOn = "2026-11-19T18:00:00Z", DurationMinutes = 30 };

			Value(await Controller().SaveBreak(input, CancellationToken.None)).Error.Should().Be("NotPermitted");

			_shiftDispatchers.Setup(x => x.GetSettingsForShiftAsync(5)).ReturnsAsync(new ShiftDispatchSetting { ShiftId = 5, DepartmentId = DepartmentId, IsEnabled = true, AllowSelfScheduledBreaks = true });
			_breaks.Setup(x => x.SaveBreakAsync(DepartmentId, It.IsAny<DispatcherBreak>(), Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, DispatcherBreak b, string __, CancellationToken ___) => DispatcherDeskResult<DispatcherBreak>.Ok(b, new List<DispatcherDeskWarning>
				{
					new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.BreaksOverlap, UserIds = { Me, Other } }
				}));

			var saved = Value(await Controller().SaveBreak(input, CancellationToken.None));

			saved.Success.Should().BeTrue();
			saved.Warnings.Single().Kind.Should().Be("BreaksOverlap");
			saved.Break.ScheduledStartOn.Should().Be("2026-11-19T18:00:00Z");
			saved.Break.ScheduledEndOn.Should().Be("2026-11-19T18:30:00Z");
			_breaks.Verify(x => x.SaveBreakAsync(DepartmentId, It.Is<DispatcherBreak>(b => b.UserId == Me && b.ShiftId == 5 && b.ShiftDayId == 50 &&
				b.BreakType == (int)DispatcherBreakTypes.Meal && b.State == (int)DispatcherBreakStates.Scheduled), Me, It.IsAny<CancellationToken>()), Times.Once);

			// Someone else's break, or a break off any shift, is the supervisors'.
			Value(await Controller().SaveBreak(new DispatcherSaveBreakInput { UserId = Other, ShiftDayId = 50, ScheduledStartOn = "2026-11-19T18:00:00Z", DurationMinutes = 30 },
				CancellationToken.None)).Error.Should().Be("NotPermitted");
			Value(await Controller().SaveBreak(new DispatcherSaveBreakInput { ScheduledStartOn = "2026-11-19T18:00:00Z", DurationMinutes = 30 }, CancellationToken.None))
				.Error.Should().Be("NotPermitted");
			Value(await Controller().SaveBreak(new DispatcherSaveBreakInput { ScheduledStartOn = "not a time", DurationMinutes = 30 }, CancellationToken.None))
				.Error.Should().Be("InvalidInput");
		}

		[Test]
		public async Task Plan_breaks_is_for_supervisors_on_a_department_shift_day()
		{
			_shifts.Setup(x => x.GetShiftDayByIdAsync(50)).ReturnsAsync(new ShiftDay { ShiftDayId = 50, ShiftId = 5 });
			_shifts.Setup(x => x.GetShiftByIdAsync(5)).ReturnsAsync(new Shift { ShiftId = 5, DepartmentId = DepartmentId });
			_shifts.Setup(x => x.GetShiftDayByIdAsync(60)).ReturnsAsync(new ShiftDay { ShiftDayId = 60, ShiftId = 6 });
			_shifts.Setup(x => x.GetShiftByIdAsync(6)).ReturnsAsync(new Shift { ShiftId = 6, DepartmentId = 99 });

			Value(await Controller().PlanBreaks(new DispatcherPlanBreaksInput { ShiftDayId = 60 }, CancellationToken.None)).Error.Should().Be("ShiftNotFound");

			Supervisor(false);
			Value(await Controller().PlanBreaks(new DispatcherPlanBreaksInput { ShiftDayId = 50 }, CancellationToken.None)).Error.Should().Be("NotPermitted");

			Supervisor();
			_breaks.Setup(x => x.PlanBreaksAsync(DepartmentId, 50, true, Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<List<DispatcherBreak>>.Ok(new List<DispatcherBreak>()));
			Value(await Controller().PlanBreaks(new DispatcherPlanBreaksInput { ShiftDayId = 50, Replace = true }, CancellationToken.None)).Success.Should().BeTrue();
		}

		[Test]
		public async Task Coverage_defaults_to_the_department_local_day()
		{
			_breaks.Setup(x => x.GetCoverageAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new DispatcherCoverage());

			StatusOf(await Controller().GetCoverage(null, null, null)).Should().Be(StatusCodes.Status200OK);

			_breaks.Verify(x => x.GetCoverageAsync(DepartmentId, It.Is<DateTime>(f => f.Kind == DateTimeKind.Utc), It.Is<DateTime>(t => t.Kind == DateTimeKind.Utc), 5), Times.Once);
			var invocation = _breaks.Invocations.Single(i => i.Method.Name == nameof(IDispatcherBreaksService.GetCoverageAsync));
			var from = (DateTime)invocation.Arguments[1];
			var to = (DateTime)invocation.Arguments[2];
			(to - from).TotalHours.Should().BeInRange(23, 25, "one local day (DST days are 23 or 25 hours)");
			from.Hour.Should().BeOneOf(new[] { 4, 5 }, "midnight in New York is 04:00 or 05:00 UTC");

			(await Controller().GetCoverage("2026-11-20T00:00:00Z", "2026-11-19T00:00:00Z", null)).Result.Should().BeOfType<BadRequestResult>();
			(await Controller().GetCoverage("2026-11-19T00:00:00Z", "2026-11-20T00:00:00Z", 0)).Result.Should().BeOfType<BadRequestResult>();
			(await Controller().GetCoverage("2026-01-01T00:00:00Z", "2026-12-01T00:00:00Z", null)).Result.Should().BeOfType<BadRequestResult>();
		}

		#endregion Breaks

		#region Shift day rosters

		private void ShiftDay(int departmentId = DepartmentId, ShiftAssignmentTypes type = ShiftAssignmentTypes.Signup, DateTime? day = null)
		{
			_shifts.Setup(x => x.GetShiftDayByIdAsync(50)).ReturnsAsync(new ShiftDay { ShiftDayId = 50, ShiftId = 5, Day = day ?? DateTime.UtcNow.Date.AddDays(3) });
			_shifts.Setup(x => x.GetShiftByIdAsync(5)).ReturnsAsync(new Shift { ShiftId = 5, DepartmentId = departmentId, Name = "A Shift", Color = "#123456", AssignmentType = (int)type });
		}

		[Test]
		public async Task Rosters_are_changed_by_shift_managers_only()
		{
			ShiftDay();
			var input = new ShiftDayDispatcherInput { ShiftDayId = 50, UserId = Other, IsSupervisor = true };

			Value(await Controller().AssignToDay(input, CancellationToken.None)).Error.Should().Be("NotPermitted");
			Value(await Controller().RemoveFromDay(input, CancellationToken.None)).Error.Should().Be("NotPermitted");
			_shiftDispatchers.Verify(x => x.AssignToDayAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			// A group admin of one group does not manage the whole shift's dispatch roster.
			_authorization.Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(new ShiftManagementScope { GroupIds = { 3 } });
			Value(await Controller().AssignToDay(input, CancellationToken.None)).Error.Should().Be("NotPermitted");

			_authorization.Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(new ShiftManagementScope { AllGroups = true });
			_shiftDispatchers.Setup(x => x.AssignToDayAsync(DepartmentId, 50, Other, true, Me, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<ShiftDispatcher>.Ok(new ShiftDispatcher()));
			Value(await Controller().AssignToDay(input, CancellationToken.None)).Success.Should().BeTrue();

			// Shift managers need not be dispatchers themselves.
			_access.Verify(x => x.CanUseDispatchAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Roster_changes_refuse_another_departments_shift_day()
		{
			ShiftDay(departmentId: 99);
			_authorization.Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(new ShiftManagementScope { AllGroups = true });

			Value(await Controller().AssignToDay(new ShiftDayDispatcherInput { ShiftDayId = 50, UserId = Other }, CancellationToken.None)).Error.Should().Be("ShiftNotFound");
			Value(await Controller().SignupForDay(new ShiftDayDispatcherInput { ShiftDayId = 50 }, CancellationToken.None)).Error.Should().Be("ShiftNotFound");
		}

		[Test]
		public async Task A_signup_is_reviewed_only_on_its_own_day()
		{
			ShiftDay();
			_authorization.Setup(x => x.GetShiftManagementScopeAsync(Me, DepartmentId)).ReturnsAsync(new ShiftManagementScope { AllGroups = true });
			_shiftDispatchers.Setup(x => x.GetDayRosterAsync(DepartmentId, 50)).ReturnsAsync(new List<DispatcherDutyWindow>
			{
				new DispatcherDutyWindow { UserId = Other, ShiftDispatcherId = "sd-1", IsPending = true, Source = (int)ShiftDispatcherSources.Signup }
			});

			Value(await Controller().ReviewDaySignup(new ShiftDispatcherReviewInput { ShiftDayId = 50, ShiftDispatcherId = "sd-other-day", Approve = true }, CancellationToken.None))
				.Error.Should().Be("InvalidInput");

			_shiftDispatchers.Setup(x => x.ReviewSignupAsync(DepartmentId, "sd-1", true, Me, "ok", It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<ShiftDispatcher>.Ok(new ShiftDispatcher()));
			Value(await Controller().ReviewDaySignup(new ShiftDispatcherReviewInput { ShiftDayId = 50, ShiftDispatcherId = "sd-1", Approve = true, Note = "ok" }, CancellationToken.None))
				.Success.Should().BeTrue();
		}

		[Test]
		public async Task A_dispatcher_withdraws_only_their_own_signup()
		{
			ShiftDay();
			_shiftDispatchers.Setup(x => x.GetDayRosterAsync(DepartmentId, 50)).ReturnsAsync(new List<DispatcherDutyWindow>
			{
				new DispatcherDutyWindow { UserId = Me, ShiftDispatcherId = "sd-1", Source = (int)ShiftDispatcherSources.Assigned }
			});

			Value(await Controller().WithdrawFromDay(new ShiftDayDispatcherInput { ShiftDayId = 50 }, CancellationToken.None)).Error.Should().Be("NotPermitted",
				"a shift manager's assignment is changed by a shift manager");

			_shiftDispatchers.Setup(x => x.GetDayRosterAsync(DepartmentId, 50)).ReturnsAsync(new List<DispatcherDutyWindow>
			{
				new DispatcherDutyWindow { UserId = Me, ShiftDispatcherId = "sd-1", Source = (int)ShiftDispatcherSources.Signup, IsPending = true }
			});
			_shiftDispatchers.Setup(x => x.RemoveFromDayAsync(DepartmentId, 50, Me, Me, null, (int)ShiftDispatcherSources.Signup, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<bool>.Ok(true));

			Value(await Controller().WithdrawFromDay(new ShiftDayDispatcherInput { ShiftDayId = 50, UserId = Other }, CancellationToken.None)).Success.Should().BeTrue();
			_shiftDispatchers.Verify(x => x.RemoveFromDayAsync(DepartmentId, 50, Me, Me, null, (int)ShiftDispatcherSources.Signup, It.IsAny<CancellationToken>()), Times.Once,
				"withdrawing is always the caller's own row, whatever UserId says, and only while it is still a sign-up");
		}

		[Test]
		public async Task Shift_dispatchers_shows_the_day_roster_or_the_standing_one()
		{
			ShiftDay();
			_shifts.Setup(x => x.GetShiftByIdAsync(7)).ReturnsAsync(new Shift { ShiftId = 7, DepartmentId = 99 });
			(await Controller().GetShiftDispatchers(7, null)).Result.Should().BeOfType<NotFoundResult>();

			_shiftDispatchers.Setup(x => x.GetSettingsForShiftAsync(5)).ReturnsAsync(new ShiftDispatchSetting
			{
				ShiftId = 5, DepartmentId = DepartmentId, IsEnabled = true, RequiredDispatchers = 2,
				BreakPlanJson = "[{\"BreakType\":2,\"DurationMinutes\":30,\"TargetOffsetMinutes\":240}]"
			});
			_shiftDispatchers.Setup(x => x.IsEligibleDispatcherAsync(DepartmentId, Me)).ReturnsAsync(true);
			_shiftDispatchers.Setup(x => x.GetDayRosterAsync(DepartmentId, 50)).ReturnsAsync(new List<DispatcherDutyWindow>
			{
				new DispatcherDutyWindow { UserId = Other, ShiftId = 5, ShiftDayId = 50, Day = new DateTime(2026, 11, 19), StartUtc = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc),
					EndUtc = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), Source = (int)ShiftDispatcherSources.Signup }
			});
			_shiftDispatchers.Setup(x => x.GetRosterForShiftAsync(5)).ReturnsAsync(new List<ShiftDispatcher>
			{
				new ShiftDispatcher { ShiftDispatcherId = "standing", ShiftId = 5, UserId = Other, Source = (int)ShiftDispatcherSources.Assigned, IsSupervisor = true },
				new ShiftDispatcher { ShiftDispatcherId = "day-row", ShiftId = 5, ShiftDayId = 50, UserId = Me, Source = (int)ShiftDispatcherSources.Signup },
				new ShiftDispatcher { ShiftDispatcherId = "denied", ShiftId = 5, UserId = "dispatcher-3", Status = (int)ShiftDispatcherStatuses.Denied }
			});

			var day = Value(await Controller().GetShiftDispatchers(5, 50)).Data;
			day.IsEnabled.Should().BeTrue();
			day.BreakPlan.Single().TargetOffsetMinutes.Should().Be(240);
			day.Dispatchers.Single().StartUtc.Should().Be("2026-11-19T12:00:00Z");
			day.Names[Other].Should().Be("Sam Second");
			day.IsSignedUp.Should().BeFalse();
			day.CanSignup.Should().BeTrue();

			var standing = Value(await Controller().GetShiftDispatchers(5, null)).Data;
			standing.Dispatchers.Select(x => x.ShiftDispatcherId).Should().Equal("standing");
			standing.Dispatchers.Single().ShiftName.Should().Be("A Shift");
			standing.CanSignup.Should().BeFalse("signing up is for a day");

			// Shift viewers need not be dispatchers to see a roster.
			_access.Verify(x => x.CanUseDispatchAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Signup_is_offered_only_on_a_signup_shift()
		{
			ShiftDay(type: ShiftAssignmentTypes.Assigned);
			_shiftDispatchers.Setup(x => x.GetSettingsForShiftAsync(5)).ReturnsAsync(new ShiftDispatchSetting { ShiftId = 5, DepartmentId = DepartmentId, IsEnabled = true });
			_shiftDispatchers.Setup(x => x.IsEligibleDispatcherAsync(DepartmentId, Me)).ReturnsAsync(true);
			_shiftDispatchers.Setup(x => x.GetDayRosterAsync(DepartmentId, 50)).ReturnsAsync(new List<DispatcherDutyWindow>());

			Value(await Controller().GetShiftDispatchers(5, 50)).Data.CanSignup.Should().BeFalse();
		}

		[Test]
		public async Task Signup_is_not_offered_once_the_shift_day_is_over()
		{
			// PR #589: SignupForDayAsync refuses an ended day, so the flag must not advertise it.
			ShiftDay(day: DateTime.UtcNow.Date.AddDays(-2));
			_shiftDispatchers.Setup(x => x.GetSettingsForShiftAsync(5)).ReturnsAsync(new ShiftDispatchSetting { ShiftId = 5, DepartmentId = DepartmentId, IsEnabled = true });
			_shiftDispatchers.Setup(x => x.IsEligibleDispatcherAsync(DepartmentId, Me)).ReturnsAsync(true);
			_shiftDispatchers.Setup(x => x.GetDayRosterAsync(DepartmentId, 50)).ReturnsAsync(new List<DispatcherDutyWindow>());

			Value(await Controller().GetShiftDispatchers(5, 50)).Data.CanSignup.Should().BeFalse();

			ShiftDay(day: DateTime.UtcNow.Date.AddDays(2));
			Value(await Controller().GetShiftDispatchers(5, 50)).Data.CanSignup.Should().BeTrue();
		}

		#endregion Shift day rosters

		#region PR 589 review

		[TestCase("2026-11-19T16:00:00Z")]
		[TestCase("2026-11-19T16:00:00.000Z")]
		[TestCase("2026-11-19T11:00:00-05:00")]
		[TestCase("2026-11-19T16:00:00")]
		[TestCase("2026-11-19T16:00")]
		public void An_instant_is_a_full_iso_date_and_time(string value)
		{
			DispatcherDeskController.TryParseInstant(value, out var utc).Should().BeTrue();
			utc.Should().Be(new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc));
			utc.Kind.Should().Be(DateTimeKind.Utc);
		}

		[TestCase("09:00")]
		[TestCase("16:00:00Z")]
		[TestCase("2026-11-19")]
		[TestCase("tomorrow")]
		[TestCase("19/11/2026 16:00")]
		[TestCase("")]
		[TestCase(null)]
		public void A_time_without_its_date_is_not_an_instant(string value)
		{
			// A bare time would take today's date from the server clock (a 09:00 to 10:00 coverage window passed validation).
			DispatcherDeskController.TryParseInstant(value, out _).Should().BeFalse();
		}

		[Test]
		public async Task Coverage_refuses_times_without_dates()
		{
			(await Controller().GetCoverage("09:00", "10:00", null)).Result.Should().BeOfType<BadRequestResult>();
			_breaks.Verify(x => x.GetCoverageAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_sent_reply_stays_a_success_when_the_refresh_reads_fail()
		{
			// The reply is already out: an error here would make the app send it again.
			_presence.Setup(x => x.GetMemberAsync(DepartmentId, Me, It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("presence read failed"));
			_routing.Setup(x => x.GetItemDetailAsync(DepartmentId, "text", It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("detail read failed"));
			_conversation.Setup(x => x.SendReplyAsync(DepartmentId, "text", Me, "On our way", It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherConversationMessage>.Ok(new DispatcherConversationMessage
				{
					DispatcherConversationMessageId = "m1",
					Direction = (int)DispatcherMessageDirections.Outbound,
					Body = "On our way",
					UserId = Me,
					CreatedOn = new DateTime(2026, 11, 19, 16, 10, 0, DateTimeKind.Utc)
				}));

			var action = Value(await Controller().SendMessage(new DispatcherMessageInput { ItemId = "text", Body = "On our way" }, CancellationToken.None));

			action.Success.Should().BeTrue();
			action.Message.DispatcherConversationMessageId.Should().Be("m1");
			action.Me.Should().BeNull();
			action.Item.Should().BeNull();
			_conversation.Verify(x => x.SendReplyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_item_action_keeps_its_result_when_the_item_refresh_fails()
		{
			var held = Item("i1", DispatcherQueueItemKinds.PendingCall);
			var item = Item("i1", DispatcherQueueItemKinds.PendingCall, DispatcherQueueItemStates.Queued, assignedUserId: null);
			_routing.Setup(x => x.ReleaseAsync(DepartmentId, "i1", Me, It.IsAny<CancellationToken>())).ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Ok(item));
			// The holder check reads the item first; the refresh after the release fails.
			_routing.SetupSequence(x => x.GetItemDetailAsync(DepartmentId, "i1", It.IsAny<string>()))
				.ReturnsAsync(new DispatcherQueueItemDetail { Item = held })
				.ThrowsAsync(new InvalidOperationException("detail read failed"));

			var action = Value(await Controller().ReleaseItem(new DispatcherItemInput { ItemId = "i1" }, CancellationToken.None));

			action.Success.Should().BeTrue();
			action.Item.DispatcherQueueItemId.Should().Be("i1", "the item as the release returned it");
		}

		[Test]
		public async Task A_revoked_dispatcher_can_still_take_their_device_off_desk_pushes()
		{
			NotADispatcher();
			_push.Setup(x => x.UnregisterDeviceAsync(DepartmentId, Me, 2, "fcm-token", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var result = await Controller().UnregisterDevice(new DispatcherDeviceInput { Platform = 2, Token = "fcm-token" }, CancellationToken.None);

			StatusOf(result).Should().Be(StatusCodes.Status200OK);
			Value(result).Success.Should().BeTrue();
			_push.Verify(x => x.UnregisterDeviceAsync(DepartmentId, Me, 2, "fcm-token", It.IsAny<CancellationToken>()), Times.Once);

			// Registering stays for dispatchers.
			StatusOf(await Controller().RegisterDevice(new DispatcherDeviceInput { Platform = 2, Token = "fcm-token" }, CancellationToken.None))
				.Should().Be(StatusCodes.Status403Forbidden);
		}

		[Test]
		public async Task A_queue_row_takes_its_call_link_and_fields_from_the_same_read()
		{
			// The list read had no call; LinkCall set one before the detail read: the row shows the linked call, not a null CallId beside its fields.
			var listed = Item("text", DispatcherQueueItemKinds.Text);
			var linked = Item("text", DispatcherQueueItemKinds.Text);
			linked.CallId = 77;
			_routing.Setup(x => x.GetItemDetailAsync(DepartmentId, "text", It.IsAny<string>())).ReturnsAsync(new DispatcherQueueItemDetail
			{
				Item = linked,
				Call = new Call { CallId = 77, DepartmentId = DepartmentId, Number = "26-77", Name = "Linked call", Priority = 1 }
			});

			// A second item finished between the two reads: it is no longer open work.
			var finished = Item("done", DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Queued, assignedUserId: null);
			var closed = Item("done", DispatcherQueueItemKinds.Text, DispatcherQueueItemStates.Completed, assignedUserId: null);
			_routing.Setup(x => x.GetItemDetailAsync(DepartmentId, "done", It.IsAny<string>())).ReturnsAsync(new DispatcherQueueItemDetail { Item = closed });
			_routing.Setup(x => x.GetOpenItemsAsync(DepartmentId)).ReturnsAsync(new List<DispatcherQueueItem> { listed, finished });

			var rows = Value(await Controller().GetQueue()).Data;

			rows.Select(x => x.DispatcherQueueItemId).Should().Equal(new[] { "text" });
			rows.Single().CallId.Should().Be(77);
			rows.Single().CallNumber.Should().Be("26-77");
			_calls.Verify(x => x.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		#endregion PR 589 review
	}
}
