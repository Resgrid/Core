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
using Resgrid.Model.Identity;
using Resgrid.Model.Messages;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture]
	public class ChatbotTextResponseResolverTests
	{
		private Mock<IMessageService> _messages;
		private Mock<ICalendarService> _calendar;
		private Mock<IAuthorizationService> _authorization;
		private TextResponseResolver _resolver;

		[SetUp]
		public void SetUp()
		{
			_messages = new Mock<IMessageService>();
			_calendar = new Mock<ICalendarService>();
			_authorization = new Mock<IAuthorizationService>();
			_resolver = new TextResponseResolver(_messages.Object, _calendar.Object, _authorization.Object);
		}

		[Test]
		public async Task GetPendingResponses_ReturnsOnlyRecentUnansweredPolls()
		{
			var now = DateTime.UtcNow;
			_messages.Setup(m => m.GetInboxMessagesByUserIdAsync("user-1")).ReturnsAsync(new List<Message>
			{
				new Message { MessageId = 1, Type = (int)MessageTypes.Poll, Subject = "Poll: Recent", SentOn = now.AddHours(-2) },
				new Message { MessageId = 2, Type = (int)MessageTypes.Poll, Subject = "Poll: Old", SentOn = now.AddHours(-25) },
				new Message { MessageId = 3, Type = (int)MessageTypes.Poll, Subject = "Poll: Answered", SentOn = now.AddHours(-1) }
			});
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(1, "user-1"))
				.ReturnsAsync(new MessageRecipient
				{
					MessageId = 1,
					UserId = "user-1",
					Note = TextResponsePromptMetadata.ForPoll(10)
				});
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(3, "user-1"))
				.ReturnsAsync(new MessageRecipient { MessageId = 3, UserId = "user-1", Response = "Yes" });

			var pending = await _resolver.GetPendingResponsesAsync("user-1", 10, now.AddDays(-1));

			pending.Should().ContainSingle();
			pending[0].Type.Should().Be(PendingTextResponseType.Poll);
			pending[0].Label.Should().Be("Recent");
		}

		[Test]
		public async Task GetPendingResponses_ReturnsPollAndUnansweredCalendarRsvp()
		{
			var now = DateTime.UtcNow;
			_messages.Setup(m => m.GetInboxMessagesByUserIdAsync("user-1")).ReturnsAsync(new List<Message>
			{
				new Message { MessageId = 5, Type = (int)MessageTypes.CalendarRsvp, Subject = "Calendar RSVP: Drill", SentOn = now.AddMinutes(-10) },
				new Message { MessageId = 4, Type = (int)MessageTypes.Poll, Subject = "Poll: Staffing", SentOn = now.AddMinutes(-20) }
			});
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(4, "user-1"))
				.ReturnsAsync(new MessageRecipient
				{
					MessageId = 4,
					UserId = "user-1",
					Note = TextResponsePromptMetadata.ForPoll(10)
				});
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(5, "user-1"))
				.ReturnsAsync(new MessageRecipient
				{
					MessageId = 5,
					UserId = "user-1",
					Note = TextResponsePromptMetadata.ForCalendarRsvp(77)
				});
			_calendar.Setup(c => c.GetCalendarItemByIdAsync(77)).ReturnsAsync(new CalendarItem
			{
				CalendarItemId = 77,
				DepartmentId = 10,
				Title = "Drill",
				SignupType = (int)CalendarItemSignupTypes.RSVP
			});
			_calendar.Setup(c => c.GetCalendarItemAttendeeByUserAsync(77, "user-1"))
				.ReturnsAsync((CalendarItemAttendee)null);
			_authorization.Setup(a => a.CanUserCheckInToCalendarEventAsync("user-1", 77)).ReturnsAsync(true);

			var pending = await _resolver.GetPendingResponsesAsync("user-1", 10, now.AddDays(-1));

			pending.Should().HaveCount(2);
			pending.Should().Contain(p => p.Type == PendingTextResponseType.Poll && p.SourceId == 4);
			pending.Should().Contain(p => p.Type == PendingTextResponseType.CalendarRsvp && p.SourceId == 77);
		}

		[Test]
		public async Task GetPendingResponses_ExcludesPollFromAnotherDepartment()
		{
			// Arrange
			var now = DateTime.UtcNow;
			_messages.Setup(m => m.GetInboxMessagesByUserIdAsync("user-1")).ReturnsAsync(new List<Message>
			{
				new Message { MessageId = 4, Type = (int)MessageTypes.Poll, Subject = "Poll: Staffing", SentOn = now }
			});
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(4, "user-1"))
				.ReturnsAsync(new MessageRecipient
				{
					MessageId = 4,
					UserId = "user-1",
					Note = TextResponsePromptMetadata.ForPoll(20)
				});

			// Act
			var pending = await _resolver.GetPendingResponsesAsync("user-1", 10, now.AddDays(-1));

			// Assert
			pending.Should().BeEmpty();
		}

		[Test]
		public async Task GetPendingResponses_ExcludesCalendarEventAlreadyAnswered()
		{
			var now = DateTime.UtcNow;
			_messages.Setup(m => m.GetInboxMessagesByUserIdAsync("user-1")).ReturnsAsync(new List<Message>
			{
				new Message { MessageId = 5, Type = (int)MessageTypes.CalendarRsvp, Subject = "Calendar RSVP: Drill", SentOn = now }
			});
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(5, "user-1"))
				.ReturnsAsync(new MessageRecipient { MessageId = 5, UserId = "user-1", Note = TextResponsePromptMetadata.ForCalendarRsvp(77) });
			_calendar.Setup(c => c.GetCalendarItemByIdAsync(77)).ReturnsAsync(new CalendarItem
			{
				CalendarItemId = 77, DepartmentId = 10, SignupType = (int)CalendarItemSignupTypes.RSVP
			});
			_calendar.Setup(c => c.GetCalendarItemAttendeeByUserAsync(77, "user-1"))
				.ReturnsAsync(new CalendarItemAttendee { CalendarItemId = 77, UserId = "user-1" });

			var pending = await _resolver.GetPendingResponsesAsync("user-1", 10, now.AddDays(-1));

			pending.Should().BeEmpty();
		}

		[Test]
		public async Task RecordResponse_NoForCalendar_RecordsNotAttendingAndRecipientResponse()
		{
			var recipient = new MessageRecipient { MessageId = 5, UserId = "user-1" };
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(5, "user-1")).ReturnsAsync(recipient);
			_messages.Setup(m => m.SaveMessageRecipientAsync(recipient, It.IsAny<CancellationToken>())).ReturnsAsync(recipient);
			_calendar.Setup(c => c.GetCalendarItemByIdAsync(77)).ReturnsAsync(new CalendarItem
			{
				CalendarItemId = 77,
				DepartmentId = 10,
				Title = "Drill",
				SignupType = (int)CalendarItemSignupTypes.RSVP
			});
			_calendar.Setup(c => c.GetCalendarItemAttendeeByUserAsync(77, "user-1"))
				.ReturnsAsync((CalendarItemAttendee)null);
			_calendar.Setup(c => c.SignupForEvent(77, "user-1", "No",
				(int)CalendarItemAttendeeTypes.NotAttending, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new CalendarItemAttendee());
			_authorization.Setup(a => a.CanUserCheckInToCalendarEventAsync("user-1", 77)).ReturnsAsync(true);

			var response = await _resolver.RecordResponseAsync(new PendingTextResponse
			{
				Type = PendingTextResponseType.CalendarRsvp,
				SourceId = 77,
				MessageId = 5,
				Label = "Drill"
			}, "No", new ChatbotSession { UserId = "user-1", DepartmentId = 10, Culture = "en" });

			response.Processed.Should().BeTrue();
			recipient.Response.Should().Be("No");
			recipient.ReadOn.Should().NotBeNull();
			_calendar.Verify(c => c.SignupForEvent(77, "user-1", "No",
				(int)CalendarItemAttendeeTypes.NotAttending, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RecordResponse_PollFromAnotherDepartment_DoesNotSaveResponse()
		{
			// Arrange
			var recipient = new MessageRecipient
			{
				MessageId = 4,
				UserId = "user-1",
				Note = TextResponsePromptMetadata.ForPoll(20)
			};
			_messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(4, "user-1"))
				.ReturnsAsync(recipient);

			// Act
			var response = await _resolver.RecordResponseAsync(new PendingTextResponse
			{
				Type = PendingTextResponseType.Poll,
				SourceId = 4,
				MessageId = 4,
				Label = "Staffing"
			}, "Yes", new ChatbotSession { UserId = "user-1", DepartmentId = 10, Culture = "en" });

			// Assert
			response.Should().BeNull();
			recipient.Response.Should().BeNull();
			_messages.Verify(m => m.SaveMessageRecipientAsync(It.IsAny<MessageRecipient>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task PollCreateHandler_StoresDepartmentMetadataForEveryRecipient()
		{
			// Arrange
			Message saved = null;
			var departments = new Mock<IDepartmentsService>();
			var profiles = new Mock<IUserProfileService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(10, true)).ReturnsAsync(new Department());
			departments.Setup(d => d.GetDepartmentMemberAsync("admin", 10, true))
				.ReturnsAsync(new DepartmentMember { UserId = "admin", IsAdmin = true });
			departments.Setup(d => d.GetAllUsersForDepartmentAsync(10, false, false))
				.ReturnsAsync(new List<IdentityUser>
				{
					new IdentityUser { UserId = "admin" },
					new IdentityUser { UserId = "user-1" },
					new IdentityUser { UserId = "user-2" }
				});
			profiles.Setup(p => p.GetProfileByUserIdAsync("admin", false)).ReturnsAsync((UserProfile)null);
			_messages.Setup(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.Callback((Message message, CancellationToken _) => saved = message)
				.ReturnsAsync((Message message, CancellationToken _) => message);
			_messages.Setup(m => m.SendMessageAsync(It.IsAny<Message>(), It.IsAny<string>(), 10, true,
				It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var handler = new PollCreateHandler(_messages.Object, departments.Object, profiles.Object);

			// Act
			var response = await handler.HandleAsync(new ChatbotMessage(), new ChatbotIntent
			{
				Type = ChatbotIntentType.CreatePoll,
				Parameters = new Dictionary<string, string>
				{
					["question"] = "Available tonight?",
					["__confirmed"] = "true"
				}
			}, new ChatbotSession { UserId = "admin", DepartmentId = 10, Culture = "en" });

			// Assert
			response.Processed.Should().BeTrue();
			saved.Should().NotBeNull();
			saved.MessageRecipients.Should().HaveCount(2).And.OnlyContain(r =>
				r.Note == TextResponsePromptMetadata.ForPoll(10));
		}

		[Test]
		public async Task PromptService_StoresCalendarMetadataAndOneDayExpiry()
		{
			// Arrange
			Message saved = null;
			_messages.Setup(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.Callback((Message message, CancellationToken _) => saved = message)
				.ReturnsAsync((Message message, CancellationToken _) => message);
			var service = new TextResponsePromptService(_messages.Object);

			// Act
			await service.RecordCalendarRsvpPromptAsync(new CalendarItem
			{
				CalendarItemId = 44,
				Title = "Live Fire Training",
				SignupType = (int)CalendarItemSignupTypes.RSVP,
				CreatorUserId = "creator"
			}, "user-1");

			// Assert
			saved.Should().NotBeNull();
			saved.Type.Should().Be((int)MessageTypes.CalendarRsvp);
			saved.ExpireOn.Should().BeCloseTo(saved.SentOn.AddDays(1), TimeSpan.FromSeconds(1));
			saved.MessageRecipients.Should().ContainSingle();
			saved.MessageRecipients.Should().ContainSingle(r =>
				r.UserId == "user-1" && r.Note == TextResponsePromptMetadata.ForCalendarRsvp(44));
		}

		[Test]
		public async Task PromptService_ReusesMatchingUnexpiredCalendarPrompt()
		{
			// Arrange
			var recipient = new MessageRecipient
			{
				MessageRecipientId = 21,
				MessageId = 12,
				UserId = "user-1",
				Note = TextResponsePromptMetadata.ForCalendarRsvp(44)
			};
			var existing = new Message
			{
				MessageId = 12,
				Subject = "Calendar RSVP: Old title",
				Body = "Old body",
				SentOn = DateTime.UtcNow.AddHours(-1),
				ExpireOn = DateTime.UtcNow.AddHours(1),
				Type = (int)MessageTypes.CalendarRsvp,
				MessageRecipients = new List<MessageRecipient> { recipient }
			};
			Message saved = null;
			_messages.Setup(m => m.GetInboxMessagesByUserIdAsync("user-1"))
				.ReturnsAsync(new List<Message> { existing });
			_messages.Setup(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.Callback((Message message, CancellationToken _) => saved = message)
				.ReturnsAsync((Message message, CancellationToken _) => message);
			var service = new TextResponsePromptService(_messages.Object);

			// Act
			await service.RecordCalendarRsvpPromptAsync(new CalendarItem
			{
				CalendarItemId = 44,
				Title = "Updated title",
				SignupType = (int)CalendarItemSignupTypes.RSVP,
				CreatorUserId = "creator"
			}, "user-1");

			// Assert
			saved.Should().BeSameAs(existing);
			saved.MessageId.Should().Be(12);
			saved.Subject.Should().Be("Calendar RSVP: Updated title");
			saved.MessageRecipients.Should().ContainSingle().Which.Should().BeSameAs(recipient);
			saved.ExpireOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromSeconds(1));
		}

		[Test]
		public async Task PromptService_CreatesNewPromptWhenMatchingPromptIsExpired()
		{
			// Arrange
			var expired = new Message
			{
				MessageId = 12,
				SentOn = DateTime.UtcNow.AddDays(-2),
				ExpireOn = DateTime.UtcNow.AddMinutes(-1),
				Type = (int)MessageTypes.CalendarRsvp,
				MessageRecipients = new List<MessageRecipient>
				{
					new MessageRecipient
					{
						MessageRecipientId = 21,
						MessageId = 12,
						UserId = "user-1",
						Note = TextResponsePromptMetadata.ForCalendarRsvp(44)
					}
				}
			};
			Message saved = null;
			_messages.Setup(m => m.GetInboxMessagesByUserIdAsync("user-1"))
				.ReturnsAsync(new List<Message> { expired });
			_messages.Setup(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.Callback((Message message, CancellationToken _) => saved = message)
				.ReturnsAsync((Message message, CancellationToken _) => message);
			var service = new TextResponsePromptService(_messages.Object);

			// Act
			await service.RecordCalendarRsvpPromptAsync(new CalendarItem
			{
				CalendarItemId = 44,
				Title = "Live Fire Training",
				SignupType = (int)CalendarItemSignupTypes.RSVP,
				CreatorUserId = "creator"
			}, "user-1");

			// Assert
			saved.Should().NotBeSameAs(expired);
			saved.MessageId.Should().Be(0);
			saved.MessageRecipients.Should().ContainSingle(r =>
				r.UserId == "user-1" && r.Note == TextResponsePromptMetadata.ForCalendarRsvp(44));
		}

		[Test]
		public void ConversationEngine_OnlyCollectsMissingStatusOrStaffingParameters()
		{
			var engine = new ConversationEngine();

			engine.NeedsParameterCollection(new ChatbotIntent { Type = ChatbotIntentType.SetStatus }).Should().BeTrue();
			engine.NeedsParameterCollection(new ChatbotIntent
			{
				Type = ChatbotIntentType.SetStatus,
				Parameters = new Dictionary<string, string> { ["statusName"] = "Responding" }
			}).Should().BeFalse();
			engine.NeedsParameterCollection(new ChatbotIntent { Type = ChatbotIntentType.SetStaffing }).Should().BeTrue();
			engine.NeedsParameterCollection(new ChatbotIntent
			{
				Type = ChatbotIntentType.SetStaffing,
				Parameters = new Dictionary<string, string> { ["staffingType"] = "1" }
			}).Should().BeFalse();
		}

		[Test]
		public async Task ConversationEngine_StatusPrompt_ListsDepartmentCustomOptions()
		{
			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetActivePersonnelStateForDepartmentAsync(10)).ReturnsAsync(new CustomState
			{
				DepartmentId = 10,
				Type = (int)CustomStateTypes.Personnel,
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 101, ButtonText = "Rolling", Order = 0 },
					new CustomStateDetail { CustomStateDetailId = 102, ButtonText = "Declined", Order = 1 }
				}
			});
			var engine = new ConversationEngine(customStates.Object);

			var response = await engine.BeginDialogAsync(new ChatbotIntent { Type = ChatbotIntentType.SetStatus },
				new ChatbotSession { DepartmentId = 10, Culture = "en" });

			response.Text.Should().Contain("(1) Rolling").And.Contain("(2) Declined");
		}

		[Test]
		public async Task Ingress_MultipleOutstandingItems_AsksForNumberThenAppliesSelection()
		{
			var identity = new ChatbotUserIdentity
			{
				Id = "identity-1",
				UserId = "user-1",
				Platform = ChatbotPlatform.WebChat,
				PlatformUserId = "web-user",
				LinkingMethod = "test"
			};
			var identities = new Mock<IChatbotUserIdentityService>();
			identities.Setup(i => i.GetIdentityAsync(ChatbotPlatform.WebChat, "web-user")).ReturnsAsync(identity);
			identities.Setup(i => i.LinkUserAsync(identity.UserId, identity.Platform, identity.PlatformUserId,
				identity.PlatformUserName, identity.LinkingMethod)).ReturnsAsync(identity);

			var session = new ChatbotSession
			{
				SessionId = "session-1",
				UserId = "user-1",
				DepartmentId = 10,
				Platform = ChatbotPlatform.WebChat,
				Culture = "en",
				State = ChatbotDialogState.Idle,
				CreatedAt = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow
			};
			var sessions = new Mock<IChatbotSessionManager>();
			sessions.Setup(s => s.GetOrCreateSessionAsync("user-1", 10, ChatbotPlatform.WebChat, "web-user", 12))
				.ReturnsAsync(session);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveSmsDepartmentForUserAsync("user-1", false))
				.ReturnsAsync(new Department { DepartmentId = 10, Name = "Test Department" });
			var limits = new Mock<ILimitsService>();
			limits.Setup(l => l.CanDepartmentProvisionNumberAsync(10)).ReturnsAsync(true);
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.IsUserValidWithinLimitsAsync("user-1", 10)).ReturnsAsync(true);
			var rateLimiter = new Mock<IChatbotRateLimiter>();
			rateLimiter.Setup(r => r.TryAcquireAsync("user-1", 10, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
			var departmentConfig = new Mock<IChatbotDepartmentConfigService>();
			departmentConfig.Setup(c => c.GetConfigAsync(10, false)).ReturnsAsync(new ChatbotDepartmentConfig
			{
				DepartmentId = 10,
				IsEnabled = true,
				AllowedPlatforms = "*",
				SessionTtlMinutes = 12
			});

			var poll = new PendingTextResponse
			{
				Type = PendingTextResponseType.Poll, SourceId = 4, MessageId = 4, Label = "Staffing"
			};
			var calendar = new PendingTextResponse
			{
				Type = PendingTextResponseType.CalendarRsvp, SourceId = 77, MessageId = 5, Label = "Drill"
			};
			var resolver = new Mock<ITextResponseResolver>();
			resolver.Setup(r => r.GetPendingResponsesAsync("user-1", 10, It.IsAny<DateTime>()))
				.ReturnsAsync(new List<PendingTextResponse> { poll, calendar });
			resolver.Setup(r => r.RecordResponseAsync(It.Is<PendingTextResponse>(p => p.SourceId == 77), "Yes", session))
				.ReturnsAsync(new ChatbotResponse { Text = "RSVP recorded", Processed = true });

			var ingress = new ChatbotIngressService(
				identities.Object,
				sessions.Object,
				Mock.Of<IChatbotIntentRouter>(),
				Array.Empty<IChatbotActionHandler>(),
				new ConversationEngine(),
				Mock.Of<IChatbotTemplateRenderer>(),
				Mock.Of<IUserProfileService>(),
				departments.Object,
				Mock.Of<IDepartmentSettingsService>(),
				limits.Object,
				authorization.Object,
				departmentConfig.Object,
				rateLimiter.Object,
				Mock.Of<ISecurityPinService>(),
				resolver.Object);

			var first = await ingress.ProcessMessageAsync(new ChatbotMessage
			{
				From = "web-user", Text = "YES", Platform = ChatbotPlatform.WebChat
			});

			first.Text.Should().Contain("1. Poll: Staffing").And.Contain("2. Calendar: Drill");
			session.State.Should().Be(ChatbotDialogState.AwaitingResponseTarget);

			var second = await ingress.ProcessMessageAsync(new ChatbotMessage
			{
				From = "web-user", Text = "2", Platform = ChatbotPlatform.WebChat
			});

			second.Text.Should().Be("RSVP recorded");
			session.State.Should().Be(ChatbotDialogState.Idle);
			resolver.Verify(r => r.RecordResponseAsync(It.Is<PendingTextResponse>(p => p.SourceId == 77), "Yes", session), Times.Once);
		}
	}
}
