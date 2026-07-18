using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Behavioral + security tests for the Phase 3 action handlers: ownership/anti-IDOR, per-handler
	/// authorization, the destructive-action confirmation flow, and the happy paths. Mirrors the
	/// conventions in <see cref="ChatbotSecurityTests"/> (NUnit + Moq + FluentAssertions).
	/// </summary>
	[TestFixture]
	public class ChatbotHandlerTests
	{
		private static ChatbotSession Session(string userId = "user-1", int departmentId = 1) =>
			new ChatbotSession { SessionId = "s1", UserId = userId, DepartmentId = departmentId, Platform = ChatbotPlatform.SmsTwilio };

		private static ChatbotIntent Intent(ChatbotIntentType type, params (string key, string value)[] parameters)
		{
			var intent = new ChatbotIntent { Type = type };
			foreach (var (key, value) in parameters)
				intent.Parameters[key] = value;
			return intent;
		}

		private static ChatbotMessage Msg(string text = "test") => new ChatbotMessage { Text = text };

		// ===================== MessageReadHandler (MessageDetail) =====================

		[Test]
		public async Task MessageRead_NotARecipientOrSender_ReturnsNotFound_AndDoesNotMarkRead()
		{
			var messages = new Mock<IMessageService>();
			messages.Setup(m => m.GetMessageByIdAsync(42))
				.ReturnsAsync(new Message { MessageId = 42, Subject = "Secret", Body = "hidden", SendingUserId = "someone-else" });
			messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(42, "user-1")).ReturnsAsync((MessageRecipient)null);

			var handler = new MessageReadHandler(messages.Object);
			var response = await handler.HandleAsync(Msg("#42"), Intent(ChatbotIntentType.MessageDetail, ("messageId", "42")), Session());

			response.Text.Should().Contain("not found");
			response.Text.Should().NotContain("hidden");
			messages.Verify(m => m.ReadMessageRecipientAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task MessageRead_OwnedUnread_ReturnsBody_AndMarksRead()
		{
			var messages = new Mock<IMessageService>();
			messages.Setup(m => m.GetMessageByIdAsync(42))
				.ReturnsAsync(new Message { MessageId = 42, Subject = "Hello", Body = "the body", SentOn = DateTime.UtcNow });
			messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(42, "user-1"))
				.ReturnsAsync(new MessageRecipient { MessageId = 42, UserId = "user-1", ReadOn = null });

			var handler = new MessageReadHandler(messages.Object);
			var response = await handler.HandleAsync(Msg("#42"), Intent(ChatbotIntentType.MessageDetail, ("messageId", "42")), Session());

			response.Processed.Should().BeTrue();
			response.Text.Should().Contain("the body");
			messages.Verify(m => m.ReadMessageRecipientAsync(42, "user-1", It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== MessageDeleteHandler =====================

		[Test]
		public async Task MessageDelete_NotARecipient_ReturnsNotFound_AndDoesNotDelete()
		{
			var messages = new Mock<IMessageService>();
			messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(42, "user-1")).ReturnsAsync((MessageRecipient)null);

			var handler = new MessageDeleteHandler(messages.Object);
			var response = await handler.HandleAsync(Msg("delete #42"), Intent(ChatbotIntentType.DeleteMessage, ("messageId", "42")), Session());

			response.Text.Should().Contain("not found");
			messages.Verify(m => m.MarkMessageRecipientAsDeletedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task MessageDelete_OwnRecipient_Deletes()
		{
			var messages = new Mock<IMessageService>();
			messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(42, "user-1"))
				.ReturnsAsync(new MessageRecipient { MessageId = 42, UserId = "user-1" });

			var handler = new MessageDeleteHandler(messages.Object);
			var response = await handler.HandleAsync(Msg("delete #42"), Intent(ChatbotIntentType.DeleteMessage, ("messageId", "42")), Session());

			response.Processed.Should().BeTrue();
			messages.Verify(m => m.MarkMessageRecipientAsDeletedAsync(42, "user-1", It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== MessageSendHandler =====================

		[Test]
		public async Task MessageSend_MissingBody_ReturnsUsage_AndDoesNotSend()
		{
			var messages = new Mock<IMessageService>();
			var search = new Mock<IChatbotUserSearchService>();

			var handler = new MessageSendHandler(messages.Object, search.Object);
			var response = await handler.HandleAsync(Msg("send message to John"),
				Intent(ChatbotIntentType.SendMessage, ("recipient", "John")), Session());

			response.Processed.Should().BeFalse();
			messages.Verify(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task MessageSend_AmbiguousRecipient_DoesNotSend()
		{
			var messages = new Mock<IMessageService>();
			var search = new Mock<IChatbotUserSearchService>();
			search.Setup(s => s.ResolveSingleAsync(1, "John")).ReturnsAsync((ChatbotUserMatch)null);

			var handler = new MessageSendHandler(messages.Object, search.Object);
			var response = await handler.HandleAsync(Msg("send message to John: hi"),
				Intent(ChatbotIntentType.SendMessage, ("recipient", "John"), ("body", "hi")), Session());

			response.Text.Should().Contain("couldn't find");
			messages.Verify(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task MessageSend_ResolvedRecipient_SavesAndSends()
		{
			var messages = new Mock<IMessageService>();
			messages.Setup(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Message m, CancellationToken _) => m);
			var search = new Mock<IChatbotUserSearchService>();
			search.Setup(s => s.ResolveSingleAsync(1, "John Smith"))
				.ReturnsAsync(new ChatbotUserMatch { UserId = "user-2", FullName = "John Smith" });

			var handler = new MessageSendHandler(messages.Object, search.Object);
			var response = await handler.HandleAsync(Msg("send message to John Smith: running late"),
				Intent(ChatbotIntentType.SendMessage, ("recipient", "John Smith"), ("body", "running late")), Session());

			response.Processed.Should().BeTrue();
			response.Text.Should().Contain("John Smith");
			messages.Verify(m => m.SaveMessageAsync(It.Is<Message>(x => x.SendingUserId == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
			messages.Verify(m => m.SendMessageAsync(It.IsAny<Message>(), It.IsAny<string>(), 1, false, It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== CalendarRsvpHandler =====================

		[Test]
		public async Task CalendarRsvp_EventInDifferentDepartment_ReturnsNotFound_AndDoesNotSignup()
		{
			var calendar = new Mock<ICalendarService>();
			calendar.Setup(c => c.GetCalendarItemByIdAsync(3))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 3, DepartmentId = 2, Title = "Other Dept Drill" });
			var authz = new Mock<IAuthorizationService>();

			var handler = new CalendarRsvpHandler(calendar.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("rsvp yes to 3"),
				Intent(ChatbotIntentType.RsvpCalendar, ("response", "yes"), ("eventId", "3")), Session(departmentId: 1));

			response.Text.Should().Contain("not found");
			calendar.Verify(c => c.SignupForEvent(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task CalendarRsvp_Authorized_SignsUpWithRsvpType()
		{
			var calendar = new Mock<ICalendarService>();
			calendar.Setup(c => c.GetCalendarItemByIdAsync(3))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 3, DepartmentId = 1, Title = "Drill" });
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCheckInToCalendarEventAsync("user-1", 3)).ReturnsAsync(true);

			var handler = new CalendarRsvpHandler(calendar.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("rsvp yes to 3"),
				Intent(ChatbotIntentType.RsvpCalendar, ("response", "yes"), ("eventId", "3")), Session(departmentId: 1));

			response.Processed.Should().BeTrue();
			calendar.Verify(c => c.SignupForEvent(3, "user-1", It.IsAny<string>(), (int)CalendarItemAttendeeTypes.RSVP, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task CalendarRsvp_NotAuthorized_IsDenied()
		{
			var calendar = new Mock<ICalendarService>();
			calendar.Setup(c => c.GetCalendarItemByIdAsync(3))
				.ReturnsAsync(new CalendarItem { CalendarItemId = 3, DepartmentId = 1, Title = "Drill" });
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCheckInToCalendarEventAsync("user-1", 3)).ReturnsAsync(false);

			var handler = new CalendarRsvpHandler(calendar.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("rsvp yes to 3"),
				Intent(ChatbotIntentType.RsvpCalendar, ("response", "yes"), ("eventId", "3")), Session(departmentId: 1));

			response.Processed.Should().BeFalse();
			calendar.Verify(c => c.SignupForEvent(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ===================== CloseCallHandler (destructive + confirmation) =====================

		[Test]
		public async Task CloseCall_CallInDifferentDepartment_ReturnsNotFound_AndNoAuthzNoSave()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 5, Name = "Secret", DepartmentId = 2 });
			var authz = new Mock<IAuthorizationService>();

			var handler = new CloseCallHandler(calls.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("close call 5"), Intent(ChatbotIntentType.CloseCall, ("callId", "5")), Session(departmentId: 1));

			response.Text.Should().Contain("not found");
			authz.Verify(a => a.CanUserCloseCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
			calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task CloseCall_WithoutPermission_IsDenied()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 5, Name = "Fire", DepartmentId = 1, State = (int)CallStates.Active });
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCloseCallAsync("user-1", 5, 1)).ReturnsAsync(false);

			var handler = new CloseCallHandler(calls.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("close call 5"), Intent(ChatbotIntentType.CloseCall, ("callId", "5")), Session(departmentId: 1));

			response.Processed.Should().BeFalse();
			response.Text.Should().Contain("permission");
			calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task CloseCall_FirstPass_AsksForConfirmation_AndDoesNotClose()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 5, Name = "Structure Fire", DepartmentId = 1, State = (int)CallStates.Active });
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCloseCallAsync("user-1", 5, 1)).ReturnsAsync(true);

			var session = Session(departmentId: 1);
			var handler = new CloseCallHandler(calls.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("close call 5"), Intent(ChatbotIntentType.CloseCall, ("callId", "5")), session);

			response.Text.Should().Contain("Close Call #5");
			session.State.Should().Be(ChatbotDialogState.AwaitingConfirmation);
			session.PendingIntent.Should().Be(ChatbotIntentType.CloseCall);
			calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task CloseCall_Confirmed_ClosesTheCall()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 5, Name = "Structure Fire", DepartmentId = 1, State = (int)CallStates.Active });
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCloseCallAsync("user-1", 5, 1)).ReturnsAsync(true);

			var handler = new CloseCallHandler(calls.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("YES"),
				Intent(ChatbotIntentType.CloseCall, ("callId", "5"), ("__confirmed", "true")), Session(departmentId: 1));

			response.Processed.Should().BeTrue();
			calls.Verify(c => c.SaveCallAsync(It.Is<Call>(x => x.State == (int)CallStates.Closed && x.ClosedByUserId == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== DispatchCallHandler (destructive + confirmation) =====================

		[Test]
		public async Task SetStatus_WhenDepartmentRequiresConfirmation_DoesNotChangeStatusOnFirstPass()
		{
			var actionLogs = new Mock<IActionLogsService>();
			var config = new Mock<IChatbotDepartmentConfigService>();
			config.Setup(c => c.GetConfigAsync(1, false)).ReturnsAsync(new ChatbotDepartmentConfig
			{
				DepartmentId = 1,
				RequireConfirmationForStatusChange = true
			});
			var session = Session();
			var handler = new StatusActionHandler(actionLogs.Object, Mock.Of<ICustomStateService>(), config.Object);

			var response = await handler.HandleAsync(Msg("set status responding"),
				Intent(ChatbotIntentType.SetStatus, ("actionType", ((int)ActionTypes.Responding).ToString())), session);

			response.Processed.Should().BeTrue();
			session.State.Should().Be(ChatbotDialogState.AwaitingConfirmation);
			session.PendingIntent.Should().Be(ChatbotIntentType.SetStatus);
			actionLogs.Verify(a => a.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SetStatus_BuiltInResponding_UsesDepartmentCustomBaseType()
		{
			var actionLogs = new Mock<IActionLogsService>();
			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(1)).ReturnsAsync(new List<CustomStateDetail>
			{
				new CustomStateDetail
				{
					CustomStateDetailId = 101,
					ButtonText = "Rolling",
					BaseType = (int)ActionBaseTypes.Responding
				}
			});

			var handler = new StatusActionHandler(actionLogs.Object, customStates.Object);
			var response = await handler.HandleAsync(Msg("set status responding"),
				Intent(ChatbotIntentType.SetStatus, ("actionType", ((int)ActionTypes.Responding).ToString())), Session());

			response.Text.Should().Contain("Rolling");
			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, 101, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SetStatus_AvailableName_UsesBuiltInAvailableStationWhenNoCustomSetExists()
		{
			var actionLogs = new Mock<IActionLogsService>();
			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(1)).ReturnsAsync(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = (int)ActionTypes.Responding, ButtonText = "Responding" },
				new CustomStateDetail { CustomStateDetailId = (int)ActionTypes.AvailableStation, ButtonText = "Available Station" }
			});

			var handler = new StatusActionHandler(actionLogs.Object, customStates.Object);
			await handler.HandleAsync(Msg("set my status to available"),
				Intent(ChatbotIntentType.SetStatus, ("statusName", "available")), Session());

			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, (int)ActionTypes.AvailableStation,
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SetStaffing_AvailableShortcut_DoesNotUseCustomLevelPosition()
		{
			// Arrange
			var userStates = new Mock<IUserStateService>();
			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomPersonnelStaffingsOrDefaultsAsync(1)).ReturnsAsync(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = 201, ButtonText = "On Duty", Order = 0 },
				new CustomStateDetail { CustomStateDetailId = 202, ButtonText = "Off Duty", Order = 1 }
			});

			var handler = new StaffingActionHandler(userStates.Object, customStates.Object);

			// Act
			var response = await handler.HandleAsync(Msg("available"),
				Intent(ChatbotIntentType.SetStaffing, ("staffingType", ((int)UserStateTypes.Available).ToString())), Session());

			// Assert
			response.Processed.Should().BeFalse();
			userStates.Verify(x => x.CreateUserState(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DispatchCall_WhenDisabledByDepartment_IsDeniedBeforeAuthorization()
		{
			var calls = new Mock<ICallsService>();
			var authz = new Mock<IAuthorizationService>();
			var config = new Mock<IChatbotDepartmentConfigService>();
			config.Setup(c => c.GetConfigAsync(1, false)).ReturnsAsync(new ChatbotDepartmentConfig
			{
				DepartmentId = 1,
				AllowDispatchViaChatbot = false
			});

			var handler = new DispatchCallHandler(calls.Object, authz.Object, config.Object);
			var response = await handler.HandleAsync(Msg("dispatch structure fire"),
				Intent(ChatbotIntentType.DispatchCall, ("description", "structure fire")), Session());

			response.Processed.Should().BeFalse();
			response.Text.Should().Contain("disabled");
			authz.Verify(a => a.CanUserCreateCallAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task DispatchCall_WithoutPermission_IsDenied()
		{
			var calls = new Mock<ICallsService>();
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCreateCallAsync("user-1", 1)).ReturnsAsync(false);

			var handler = new DispatchCallHandler(calls.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("dispatch structure fire"),
				Intent(ChatbotIntentType.DispatchCall, ("description", "structure fire")), Session());

			response.Processed.Should().BeFalse();
			calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DispatchCall_FirstPass_AsksForConfirmation_AndDoesNotCreate()
		{
			var calls = new Mock<ICallsService>();
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCreateCallAsync("user-1", 1)).ReturnsAsync(true);

			var session = Session();
			var handler = new DispatchCallHandler(calls.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("dispatch structure fire at 123 Main"),
				Intent(ChatbotIntentType.DispatchCall, ("description", "structure fire at 123 Main")), session);

			session.State.Should().Be(ChatbotDialogState.AwaitingConfirmation);
			session.PendingIntent.Should().Be(ChatbotIntentType.DispatchCall);
			calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DispatchCall_Confirmed_CreatesActiveCall()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<DepartmentCallPriority> { new DepartmentCallPriority { DepartmentCallPriorityId = 7, IsDefault = true } });
			calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call c, CancellationToken _) => { c.CallId = 99; return c; });
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserCreateCallAsync("user-1", 1)).ReturnsAsync(true);

			var handler = new DispatchCallHandler(calls.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("YES"),
				Intent(ChatbotIntentType.DispatchCall, ("description", "structure fire"), ("__confirmed", "true")), Session());

			response.Text.Should().Contain("#99");
			calls.Verify(c => c.SaveCallAsync(It.Is<Call>(x => x.State == (int)CallStates.Active && x.Priority == 7 && x.ReportingUserId == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== RespondToCallHandler =====================

		[Test]
		public async Task RespondToCall_CallInDifferentDepartment_ReturnsNotFound_AndDoesNotSetStatus()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 7, Name = "Fire", DepartmentId = 2 });
			var actionLogs = new Mock<IActionLogsService>();

			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object);
			var response = await handler.HandleAsync(Msg("respond to c7"), Intent(ChatbotIntentType.RespondToCall, ("callId", "7")), Session(departmentId: 1));

			// Cross-department call resolves to the same no-match reply as a nonexistent one (anti-IDOR).
			response.Text.Should().Contain("No active call found matching");
			actionLogs.Verify(a => a.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task RespondToCall_Valid_SetsRespondingWithDestination()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 7, Name = "Fire", DepartmentId = 1 });
			var actionLogs = new Mock<IActionLogsService>();

			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object);
			var response = await handler.HandleAsync(Msg("respond to c7"), Intent(ChatbotIntentType.RespondToCall, ("callId", "7")), Session(departmentId: 1));

			response.Processed.Should().BeTrue();
			actionLogs.Verify(a => a.SetUserActionAsync("user-1", 1, (int)ActionTypes.Responding,
				It.IsAny<string>(), 7, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RespondToCall_BareResponse_UsesMostRecentDirectDispatchAndCustomStatus()
		{
			var recentCall = new Call
			{
				CallId = 8,
				Name = "Medical",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = DateTime.UtcNow.AddMinutes(-10),
				Dispatches = new List<CallDispatch>
				{
					new CallDispatch { UserId = "user-1", DispatchedOn = DateTime.UtcNow.AddMinutes(-5) }
				}
			};
			var unrelatedCall = new Call
			{
				CallId = 9,
				Name = "Alarm",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = DateTime.UtcNow,
				Dispatches = new List<CallDispatch> { new CallDispatch { UserId = "someone-else", DispatchedOn = DateTime.UtcNow } }
			};
			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(1)).ReturnsAsync(new List<Call> { unrelatedCall, recentCall });
			calls.Setup(x => x.PopulateCallData(It.IsAny<Call>(), true, false, false, true, false, true, false, false, false, false))
				.ReturnsAsync((Call call, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _) => call);

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(1)).ReturnsAsync(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = 101, ButtonText = "Rolling", BaseType = (int)ActionBaseTypes.Responding }
			});
			var actionLogs = new Mock<IActionLogsService>();

			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object, customStates.Object);
			var response = await handler.HandleAsync(Msg("omw"),
				Intent(ChatbotIntentType.RespondToCall, ("response", "yes")), Session());

			response.Processed.Should().BeTrue();
			response.Text.Should().Contain("Call #8");
			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, 101, It.IsAny<string>(), 8,
				(int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RespondToCall_BareResponse_FallsBackToGroupDispatchMembership()
		{
			var call = new Call
			{
				CallId = 10,
				Name = "Group Call",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = DateTime.UtcNow.AddMinutes(-20),
				Dispatches = new List<CallDispatch>(),
				GroupDispatches = new List<CallDispatchGroup>
				{
					new CallDispatchGroup { DepartmentGroupId = 5, DispatchedOn = DateTime.UtcNow.AddMinutes(-10) }
				},
				RoleDispatches = new List<CallDispatchRole>()
			};
			var calls = CallsWithPopulatedDispatches(call);
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllMembersForGroupAsync(5)).ReturnsAsync(new List<DepartmentGroupMember>
			{
				new DepartmentGroupMember { UserId = "user-1", DepartmentGroupId = 5 }
			});
			var actionLogs = new Mock<IActionLogsService>();
			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object, null, groups.Object);

			var response = await handler.HandleAsync(Msg("responding"),
				Intent(ChatbotIntentType.RespondToCall, ("response", "yes")), Session());

			response.Text.Should().Contain("Call #10");
			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, (int)ActionTypes.Responding,
				It.IsAny<string>(), 10, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RespondToCall_BareResponse_FallsBackToRoleDispatchMembership()
		{
			var call = new Call
			{
				CallId = 11,
				Name = "Role Call",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = DateTime.UtcNow.AddMinutes(-20),
				Dispatches = new List<CallDispatch>(),
				GroupDispatches = new List<CallDispatchGroup>(),
				RoleDispatches = new List<CallDispatchRole>
				{
					new CallDispatchRole { RoleId = 7, DispatchedOn = DateTime.UtcNow.AddMinutes(-5) }
				}
			};
			var calls = CallsWithPopulatedDispatches(call);
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync("user-1", 1)).ReturnsAsync(new List<PersonnelRole>
			{
				new PersonnelRole { PersonnelRoleId = 7 }
			});
			var actionLogs = new Mock<IActionLogsService>();
			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object, null, null, roles.Object);

			var response = await handler.HandleAsync(Msg("omw"),
				Intent(ChatbotIntentType.RespondToCall, ("response", "yes")), Session());

			response.Text.Should().Contain("Call #11");
			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, (int)ActionTypes.Responding,
				It.IsAny<string>(), 11, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RespondToCall_BareResponse_DirectAndNewerGroupDispatch_UsesNewestApplicableTimestamp()
		{
			// Arrange
			var now = DateTime.UtcNow;
			var groupDispatchedCall = new Call
			{
				CallId = 12,
				Name = "Group Dispatched Call",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = now.AddMinutes(-20),
				Dispatches = new List<CallDispatch>
				{
					new CallDispatch { UserId = "user-1", DispatchedOn = now.AddMinutes(-15) }
				},
				GroupDispatches = new List<CallDispatchGroup>
				{
					new CallDispatchGroup { DepartmentGroupId = 5, DispatchedOn = now.AddMinutes(-1) }
				}
			};
			var directDispatchedCall = new Call
			{
				CallId = 13,
				Name = "Direct Call",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = now.AddMinutes(-10),
				Dispatches = new List<CallDispatch>
				{
					new CallDispatch { UserId = "user-1", DispatchedOn = now.AddMinutes(-5) }
				}
			};
			var calls = CallsWithPopulatedDispatches(directDispatchedCall, groupDispatchedCall);
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllMembersForGroupAsync(5)).ReturnsAsync(new List<DepartmentGroupMember>
			{
				new DepartmentGroupMember { UserId = "user-1", DepartmentGroupId = 5 }
			});
			var actionLogs = new Mock<IActionLogsService>();
			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object, null, groups.Object);

			// Act
			var response = await handler.HandleAsync(Msg("responding"),
				Intent(ChatbotIntentType.RespondToCall, ("response", "yes")), Session());

			// Assert
			response.Text.Should().Contain("Call #12");
			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, (int)ActionTypes.Responding,
				It.IsAny<string>(), 12, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RespondToCall_BareResponse_DirectAndNewerRoleDispatch_UsesNewestApplicableTimestamp()
		{
			// Arrange
			var now = DateTime.UtcNow;
			var roleDispatchedCall = new Call
			{
				CallId = 14,
				Name = "Role Dispatched Call",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = now.AddMinutes(-20),
				Dispatches = new List<CallDispatch>
				{
					new CallDispatch { UserId = "user-1", DispatchedOn = now.AddMinutes(-15) }
				},
				RoleDispatches = new List<CallDispatchRole>
				{
					new CallDispatchRole { RoleId = 7, DispatchedOn = now.AddMinutes(-1) }
				}
			};
			var directDispatchedCall = new Call
			{
				CallId = 15,
				Name = "Direct Call",
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = now.AddMinutes(-10),
				Dispatches = new List<CallDispatch>
				{
					new CallDispatch { UserId = "user-1", DispatchedOn = now.AddMinutes(-5) }
				}
			};
			var calls = CallsWithPopulatedDispatches(directDispatchedCall, roleDispatchedCall);
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync("user-1", 1)).ReturnsAsync(new List<PersonnelRole>
			{
				new PersonnelRole { PersonnelRoleId = 7 }
			});
			var actionLogs = new Mock<IActionLogsService>();
			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object, null, null, roles.Object);

			// Act
			var response = await handler.HandleAsync(Msg("responding"),
				Intent(ChatbotIntentType.RespondToCall, ("response", "yes")), Session());

			// Assert
			response.Text.Should().Contain("Call #14");
			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, (int)ActionTypes.Responding,
				It.IsAny<string>(), 14, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RespondToCall_NotGoing_UsesCustomNotRespondingStatus()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetCallByIdAsync(7, It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = 7, Name = "Fire", DepartmentId = 1, State = (int)CallStates.Active });
			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomPersonnelStatusesOrDefaultsAsync(1)).ReturnsAsync(new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = 102, ButtonText = "Declined", BaseType = (int)ActionBaseTypes.NotResponding }
			});
			var actionLogs = new Mock<IActionLogsService>();

			var handler = new RespondToCallHandler(calls.Object, actionLogs.Object, customStates.Object);
			var response = await handler.HandleAsync(Msg("not going to c7"),
				Intent(ChatbotIntentType.RespondToCall, ("callId", "7"), ("response", "no")), Session());

			response.Text.Should().Contain("Declined").And.Contain("Call #7");
			actionLogs.Verify(x => x.SetUserActionAsync("user-1", 1, 102, It.IsAny<string>(), 7,
				(int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
		}

		private static Mock<ICallsService> CallsWithPopulatedDispatches(params Call[] callsToReturn)
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(1)).ReturnsAsync(new List<Call>(callsToReturn));
			calls.Setup(x => x.PopulateCallData(It.IsAny<Call>(), true, false, false, true, false, true, false, false, false, false))
				.ReturnsAsync((Call call, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _) => call);
			return calls;
		}

		// ===================== SetUnitStatusHandler (destructive + confirmation) =====================

		private static Mock<IUnitsService> UnitsWith(int unitId, string unitName)
		{
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetAllLatestStatusForUnitsByDepartmentIdAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<UnitState> { new UnitState { Unit = new Unit { UnitId = unitId, Name = unitName } } });
			return units;
		}

		private static Mock<ICustomStateService> CustomUnitStatesWith(int detailId, string buttonText)
		{
			var states = new Mock<ICustomStateService>();
			states.Setup(s => s.GetAllActiveCustomStatesForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<CustomState>
				{
					new CustomState
					{
						Type = (int)CustomStateTypes.Unit,
						Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = detailId, ButtonText = buttonText, IsDeleted = false } }
					}
				});
			return states;
		}

		[Test]
		public async Task SetUnitStatus_UnitNotFound_ReturnsNotFound()
		{
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetAllLatestStatusForUnitsByDepartmentIdAsync(It.IsAny<int>())).ReturnsAsync(new List<UnitState>());
			var states = new Mock<ICustomStateService>();
			var authz = new Mock<IAuthorizationService>();

			var handler = new SetUnitStatusHandler(units.Object, states.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("set unit Engine 1 to Available"),
				Intent(ChatbotIntentType.SetUnitStatus, ("unitName", "Engine 1"), ("status", "Available")), Session());

			response.Text.Should().Contain("not found");
			units.Verify(u => u.SetUnitStateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SetUnitStatus_WithoutPermission_IsDenied()
		{
			var units = UnitsWith(7, "Engine 1");
			var states = CustomUnitStatesWith(20, "Available");
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserModifyUnitAsync("user-1", 7)).ReturnsAsync(false);

			var handler = new SetUnitStatusHandler(units.Object, states.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("set unit Engine 1 to Available"),
				Intent(ChatbotIntentType.SetUnitStatus, ("unitName", "Engine 1"), ("status", "Available")), Session());

			response.Processed.Should().BeFalse();
			units.Verify(u => u.SetUnitStateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SetUnitStatus_UnknownStatus_ReturnsUnknown()
		{
			var units = UnitsWith(7, "Engine 1");
			var states = CustomUnitStatesWith(20, "Available");
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserModifyUnitAsync("user-1", 7)).ReturnsAsync(true);

			var handler = new SetUnitStatusHandler(units.Object, states.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("set unit Engine 1 to Bogus"),
				Intent(ChatbotIntentType.SetUnitStatus, ("unitName", "Engine 1"), ("status", "Bogus")), Session());

			response.Text.Should().Contain("Unknown unit status");
			units.Verify(u => u.SetUnitStateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SetUnitStatus_FirstPass_AsksForConfirmation_AndDoesNotSet()
		{
			var units = UnitsWith(7, "Engine 1");
			var states = CustomUnitStatesWith(20, "Available");
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserModifyUnitAsync("user-1", 7)).ReturnsAsync(true);

			var session = Session();
			var handler = new SetUnitStatusHandler(units.Object, states.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("set unit Engine 1 to Available"),
				Intent(ChatbotIntentType.SetUnitStatus, ("unitName", "Engine 1"), ("status", "Available")), session);

			session.State.Should().Be(ChatbotDialogState.AwaitingConfirmation);
			session.PendingIntent.Should().Be(ChatbotIntentType.SetUnitStatus);
			units.Verify(u => u.SetUnitStateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SetUnitStatus_Confirmed_SetsTheState()
		{
			var units = UnitsWith(7, "Engine 1");
			var states = CustomUnitStatesWith(20, "Available");
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserModifyUnitAsync("user-1", 7)).ReturnsAsync(true);

			var handler = new SetUnitStatusHandler(units.Object, states.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("YES"),
				Intent(ChatbotIntentType.SetUnitStatus, ("unitName", "Engine 1"), ("status", "Available"), ("__confirmed", "true")), Session());

			response.Processed.Should().BeTrue();
			units.Verify(u => u.SetUnitStateAsync(7, 20, 1, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SetUnitStatus_UsesStateSetAssignedToUnitTypeAndMatchesBaseType()
		{
			var units = UnitsWith(7, "Engine 1");
			var state = (await units.Object.GetAllLatestStatusForUnitsByDepartmentIdAsync(1))[0];
			state.Unit.Type = "Engine";
			units.Setup(x => x.GetUnitTypeByNameAsync(1, "Engine"))
				.ReturnsAsync(new UnitType { UnitTypeId = 3, DepartmentId = 1, Type = "Engine", CustomStatesId = 55 });

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomSateByIdAsync(55)).ReturnsAsync(new CustomState
			{
				CustomStateId = 55,
				DepartmentId = 1,
				Type = (int)CustomStateTypes.Unit,
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 301, ButtonText = "Rolling", BaseType = (int)ActionBaseTypes.Responding }
				}
			});
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(x => x.CanUserModifyUnitAsync("user-1", 7)).ReturnsAsync(true);

			var handler = new SetUnitStatusHandler(units.Object, customStates.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("set unit Engine 1 to responding"),
				Intent(ChatbotIntentType.SetUnitStatus, ("unitName", "Engine 1"), ("status", "Responding"), ("__confirmed", "true")), Session());

			response.Text.Should().Contain("Rolling");
			units.Verify(x => x.SetUnitStateAsync(7, 301, 1, It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== ShiftSignupHandler =====================

		private static Mock<IShiftsService> ShiftsWith(int shiftDayId, int shiftId, int shiftDeptId)
		{
			var shifts = new Mock<IShiftsService>();
			shifts.Setup(s => s.GetShiftDayByIdAsync(shiftDayId))
				.ReturnsAsync(new ShiftDay { ShiftDayId = shiftDayId, ShiftId = shiftId, Day = new DateTime(2026, 6, 1) });
			shifts.Setup(s => s.GetShiftByIdAsync(shiftId))
				.ReturnsAsync(new Shift { ShiftId = shiftId, DepartmentId = shiftDeptId, Name = "A Shift" });
			return shifts;
		}

		[Test]
		public async Task ShiftSignup_ShiftInDifferentDepartment_ReturnsNotFound()
		{
			var shifts = ShiftsWith(5, 3, shiftDeptId: 2);

			var handler = new ShiftSignupHandler(shifts.Object);
			var response = await handler.HandleAsync(Msg("sign up shift 5"), Intent(ChatbotIntentType.ShiftSignup, ("shiftId", "5")), Session(departmentId: 1));

			response.Text.Should().Contain("not found");
			shifts.Verify(s => s.SignupForShiftDayAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ShiftSignup_AlreadySignedUp_DoesNotSignupAgain()
		{
			var shifts = ShiftsWith(5, 3, shiftDeptId: 1);
			shifts.Setup(s => s.IsUserSignedUpForShiftDayAsync(It.IsAny<ShiftDay>(), "user-1", It.IsAny<int?>())).ReturnsAsync(true);

			var handler = new ShiftSignupHandler(shifts.Object);
			var response = await handler.HandleAsync(Msg("sign up shift 5"), Intent(ChatbotIntentType.ShiftSignup, ("shiftId", "5")), Session(departmentId: 1));

			response.Text.Should().Contain("already signed up");
			shifts.Verify(s => s.SignupForShiftDayAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ShiftSignup_DayFull_DoesNotSignup()
		{
			var shifts = ShiftsWith(5, 3, shiftDeptId: 1);
			shifts.Setup(s => s.IsUserSignedUpForShiftDayAsync(It.IsAny<ShiftDay>(), "user-1", It.IsAny<int?>())).ReturnsAsync(false);
			shifts.Setup(s => s.IsShiftDayFilledAsync(5)).ReturnsAsync(true);

			var handler = new ShiftSignupHandler(shifts.Object);
			var response = await handler.HandleAsync(Msg("sign up shift 5"), Intent(ChatbotIntentType.ShiftSignup, ("shiftId", "5")), Session(departmentId: 1));

			response.Text.Should().Contain("full");
			shifts.Verify(s => s.SignupForShiftDayAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ShiftSignup_Valid_SignsUp()
		{
			var shifts = ShiftsWith(5, 3, shiftDeptId: 1);
			shifts.Setup(s => s.IsUserSignedUpForShiftDayAsync(It.IsAny<ShiftDay>(), "user-1", It.IsAny<int?>())).ReturnsAsync(false);
			shifts.Setup(s => s.IsShiftDayFilledAsync(5)).ReturnsAsync(false);

			var handler = new ShiftSignupHandler(shifts.Object);
			var response = await handler.HandleAsync(Msg("sign up shift 5"), Intent(ChatbotIntentType.ShiftSignup, ("shiftId", "5")), Session(departmentId: 1));

			response.Processed.Should().BeTrue();
			shifts.Verify(s => s.SignupForShiftDayAsync(3, It.IsAny<DateTime>(), 0, "user-1", It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== ShiftDropHandler =====================

		[Test]
		public async Task ShiftDrop_NotSignedUp_DoesNotDelete()
		{
			var shifts = ShiftsWith(5, 3, shiftDeptId: 1);
			shifts.Setup(s => s.GetShiftSignpsForShiftDayAsync(5)).ReturnsAsync(new List<ShiftSignup>());
			var authz = new Mock<IAuthorizationService>();

			var handler = new ShiftDropHandler(shifts.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("drop shift 5"), Intent(ChatbotIntentType.ShiftDrop, ("shiftId", "5")), Session(departmentId: 1));

			response.Text.Should().Contain("not signed up");
			shifts.Verify(s => s.DeleteShiftSignupAsync(It.IsAny<ShiftSignup>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ShiftDrop_Valid_DropsOwnSignup()
		{
			var shifts = ShiftsWith(5, 3, shiftDeptId: 1);
			shifts.Setup(s => s.GetShiftSignpsForShiftDayAsync(5))
				.ReturnsAsync(new List<ShiftSignup> { new ShiftSignup { ShiftSignupId = 11, UserId = "user-1" } });
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserDeleteShiftSignupAsync("user-1", 1, 11)).ReturnsAsync(true);

			var handler = new ShiftDropHandler(shifts.Object, authz.Object);
			var response = await handler.HandleAsync(Msg("drop shift 5"), Intent(ChatbotIntentType.ShiftDrop, ("shiftId", "5")), Session(departmentId: 1));

			response.Processed.Should().BeTrue();
			shifts.Verify(s => s.DeleteShiftSignupAsync(It.Is<ShiftSignup>(x => x.ShiftSignupId == 11), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ===================== WeatherAlertHandler =====================

		[Test]
		public async Task WeatherAlert_NoAlerts_ReturnsNone()
		{
			var weather = new Mock<IWeatherAlertService>();
			weather.Setup(w => w.GetActiveAlertsByDepartmentIdAsync(1)).ReturnsAsync(new List<WeatherAlert>());

			var handler = new WeatherAlertHandler(weather.Object);
			var response = await handler.HandleAsync(Msg("weather"), Intent(ChatbotIntentType.WeatherAlert), Session());

			response.Text.Should().Contain("No active weather alerts");
		}

		[Test]
		public async Task WeatherAlert_WithAlerts_ListsThem()
		{
			var weather = new Mock<IWeatherAlertService>();
			weather.Setup(w => w.GetActiveAlertsByDepartmentIdAsync(1))
				.ReturnsAsync(new List<WeatherAlert> { new WeatherAlert { Headline = "Tornado Warning", Severity = 5, EffectiveUtc = DateTime.UtcNow } });

			var handler = new WeatherAlertHandler(weather.Object);
			var response = await handler.HandleAsync(Msg("weather"), Intent(ChatbotIntentType.WeatherAlert), Session());

			response.Text.Should().Contain("Tornado Warning");
		}

		// ===================== PersonnelActionHandler query filter =====================

		[Test]
		public async Task Personnel_WithQuery_FiltersByName()
		{
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserViewAllPeopleAsync("user-1", 1)).ReturnsAsync(true);

			var users = new Mock<IUsersService>();
			users.Setup(u => u.GetUserGroupAndRolesByDepartmentIdInLimitAsync(1, false, false, false))
				.ReturnsAsync(new List<UserGroupRole>
				{
					new UserGroupRole { UserId = "a", FirstName = "John", LastName = "Smith" },
					new UserGroupRole { UserId = "b", FirstName = "Jane", LastName = "Doe" }
				});

			var actionLogs = new Mock<IActionLogsService>();
			actionLogs.Setup(a => a.GetLastActionLogsForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>());
			var userStates = new Mock<IUserStateService>();
			userStates.Setup(u => u.GetLatestStatesForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new List<UserState>());

			var handler = new PersonnelActionHandler(
				Mock.Of<IDepartmentsService>(), users.Object, actionLogs.Object, userStates.Object, Mock.Of<ICustomStateService>(), authz.Object);

			var response = await handler.HandleAsync(Msg("who is John"),
				Intent(ChatbotIntentType.PersonnelLookup, ("query", "John Smith")), Session());

			response.Text.Should().Contain("Smith");
			response.Text.Should().NotContain("Doe");
		}

		// ===================== ChatbotUserSearchService =====================

		[Test]
		public async Task UserSearch_ExactFullName_ResolvesSingle()
		{
			var users = new Mock<IUsersService>();
			users.Setup(u => u.GetUserGroupAndRolesByDepartmentIdInLimitAsync(1, false, false, false))
				.ReturnsAsync(new List<UserGroupRole>
				{
					new UserGroupRole { UserId = "a", FirstName = "John", LastName = "Smith" },
					new UserGroupRole { UserId = "b", FirstName = "John", LastName = "Doe" }
				});

			var service = new ChatbotUserSearchService(users.Object);
			var match = await service.ResolveSingleAsync(1, "John Smith");

			match.Should().NotBeNull();
			match.UserId.Should().Be("a");
		}

		[Test]
		public async Task UserSearch_AmbiguousFirstName_ReturnsNull()
		{
			var users = new Mock<IUsersService>();
			users.Setup(u => u.GetUserGroupAndRolesByDepartmentIdInLimitAsync(1, false, false, false))
				.ReturnsAsync(new List<UserGroupRole>
				{
					new UserGroupRole { UserId = "a", FirstName = "John", LastName = "Smith" },
					new UserGroupRole { UserId = "b", FirstName = "John", LastName = "Doe" }
				});

			var service = new ChatbotUserSearchService(users.Object);
			var match = await service.ResolveSingleAsync(1, "John");

			match.Should().BeNull();
		}
	}
}
