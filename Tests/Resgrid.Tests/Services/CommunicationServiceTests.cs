using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Model.Identity;

namespace Resgrid.Tests.Services
{
	namespace CommunicationServiceTests
	{
		public class with_the_communication_service : TestBase
		{
			protected Mock<ISmsService> _smsServiceMock;
			protected Mock<IEmailService> _emailServiceMock;
			protected Mock<IPushService> _pushServiceMock;
			protected Mock<IGeoLocationProvider> _geoLocationProviderMock;
			protected Mock<IOutboundVoiceProvider> _outboundVoiceProviderMock;
			protected Mock<IUserProfileService> _userProfileServiceMock;
			protected Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;
			protected Mock<ISubscriptionsService> _subscriptionsServiceMock;
			protected Mock<IUserStateService> _userStateServiceMock;
			protected Mock<IDepartmentsService> _departmentsServiceMock;
			protected Mock<IChatbotOutboundService> _chatbotOutboundServiceMock;
			protected Mock<IProtectedProjectionService> _protectedProjectionServiceMock;
			protected ICommunicationService _communicationService;

			// NUnit builds one fixture instance for the whole class, so mocks created in a constructor are
			// shared by every test in it and calls recorded by one test leak into another's Times.Never()
			// assertions. Build them per test instead.
			[SetUp]
			public void SetUpCommunicationService()
			{
				_smsServiceMock = new Mock<ISmsService>();
				_emailServiceMock = new Mock<IEmailService>();
				_pushServiceMock = new Mock<IPushService>();
				_geoLocationProviderMock = new Mock<IGeoLocationProvider>();
				_outboundVoiceProviderMock = new Mock<IOutboundVoiceProvider>();
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();
				_subscriptionsServiceMock = new Mock<ISubscriptionsService>();
				_userStateServiceMock = new Mock<IUserStateService>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();

				// CanSendToUser requires a valid DepartmentMember for the user to proceed.
				_departmentsServiceMock
					.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync(new DepartmentMember());

				_chatbotOutboundServiceMock = new Mock<IChatbotOutboundService>();

				// Pass-through by default: these tests exercise unprotected departments, where the
				// notification-safe view is the original call.
				_protectedProjectionServiceMock = new Mock<IProtectedProjectionService>();
				_protectedProjectionServiceMock
					.Setup(x => x.BuildNotificationSafeCallAsync(It.IsAny<int>(), It.IsAny<Call>(),
						It.IsAny<ProtectedDataEgressChannel>(), It.IsAny<string>()))
					.Returns<int, Call, ProtectedDataEgressChannel, string>((d, c, ch, culture) => Task.FromResult(c));
				_protectedProjectionServiceMock
					.Setup(x => x.BuildNotificationSafeMessageAsync(It.IsAny<int>(), It.IsAny<Message>(),
						It.IsAny<ProtectedDataEgressChannel>(), It.IsAny<string>()))
					.Returns<int, Message, ProtectedDataEgressChannel, string>((d, m, ch, culture) => Task.FromResult(m));
				_protectedProjectionServiceMock
					.Setup(x => x.IsChannelSanitizedAsync(It.IsAny<int>(), It.IsAny<ProtectedDataEgressChannel>()))
					.ReturnsAsync(false);

				_communicationService = new CommunicationService(_smsServiceMock.Object, _emailServiceMock.Object, _pushServiceMock.Object,
					_geoLocationProviderMock.Object, _outboundVoiceProviderMock.Object, _userProfileServiceMock.Object, _departmentSettingsServiceMock.Object,
					_subscriptionsServiceMock.Object, _userStateServiceMock.Object, _chatbotOutboundServiceMock.Object,
					_departmentsServiceMock.Object, _protectedProjectionServiceMock.Object, new System.Lazy<IAdpReleaseService>(() => Mock.Of<IAdpReleaseService>()));
			}
		}

		[TestFixture]
		[NonParallelizable]
		public class when_sending_chat_notifications : with_the_communication_service
		{
			[TestCase(false, ChatbotOutboundType.Notification)]
			[TestCase(true, ChatbotOutboundType.Reminder)]
			public async Task notifications_and_calendar_use_the_chat_projection(bool calendar, ChatbotOutboundType expectedType)
			{
				var profile = new UserProfile { UserId = TestData.Users.TestUser1Id, Language = "en" };
				_protectedProjectionServiceMock
					.Setup(x => x.BuildNotificationSafeMessageAsync(1, It.Is<Message>(m =>
						m.Subject == "Private title" && m.Body == "Private location" && m.ReceivingUserId == profile.UserId),
						ProtectedDataEgressChannel.ChatPlatform, profile.Language))
					.ReturnsAsync(new Message { Subject = "Protected notification", Body = "Sign in to Resgrid" });

				if (calendar)
					await _communicationService.SendCalendarAsync(profile.UserId, 1, "Private location", null, "Private title", profile);
				else
					await _communicationService.SendNotificationAsync(profile.UserId, 1, "Private location", null, null, "Private title", profile);

				_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync(profile.UserId, 1,
					It.Is<ChatbotOutboundMessage>(m => m.Type == expectedType && m.Title == "Protected notification"
						&& m.Body == "Sign in to Resgrid")), Times.Once);
			}

			[Test]
			public async Task cancellation_uses_the_chat_projection_instead_of_the_supplied_address()
			{
				var profile = new UserProfile { UserId = TestData.Users.TestUser1Id };
				var call = new Call { CallId = 71, DepartmentId = 1, Name = "Private call", Address = "Private address" };
				_protectedProjectionServiceMock.Setup(x => x.BuildNotificationSafeCallAsync(1, call,
					ProtectedDataEgressChannel.ChatPlatform, It.IsAny<string>()))
					.ReturnsAsync(new Call { CallId = 71, Name = "Call 71", NatureOfCall = "Sign in to Resgrid" });

				await _communicationService.SendCancelCallAsync(call, new CallDispatch { UserId = profile.UserId },
					null, 1, profile, "Resolved private address");

				_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync(profile.UserId, 1,
					It.Is<ChatbotOutboundMessage>(m => m.Type == ChatbotOutboundType.Dispatch &&
						m.Title == "Dispatch Cancelled - Call 71" && m.Body == "Sign in to Resgrid" && m.ReferenceId == "71")), Times.Once);
			}

			[TestCase(false)]
			[TestCase(true)]
			public async Task protected_trouble_alerts_omit_locations_and_personnel_with_or_without_a_call(bool hasCall)
			{
				var profile = new UserProfile { UserId = TestData.Users.TestUser1Id };
				_protectedProjectionServiceMock.Setup(x => x.IsChannelSanitizedAsync(1, ProtectedDataEgressChannel.ChatPlatform))
					.ReturnsAsync(true);
				var alert = new TroubleAlertEvent { Roles = new List<TroubleAlertRole> { new TroubleAlertRole { UserFullName = "Private person" } } };
				var call = hasCall ? new Call { CallId = 71, Name = "Private call" } : null;

				await _communicationService.SendTroubleAlertAsync(alert, new Unit { Name = "Engine 1" }, call,
					null, 1, "Private call location", "Private unit location", new List<UserProfile> { profile });

				_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync(profile.UserId, 1,
					It.Is<ChatbotOutboundMessage>(m => m.Title == "TROUBLE ALERT for Engine 1"
						&& m.Body == "Protected — sign in to Resgrid")), Times.Once);
			}

			[TestCase("notification")]
			[TestCase("calendar")]
			[TestCase("cancellation")]
			[TestCase("trouble")]
			public async Task disabled_members_do_not_receive_chat_notifications(string kind)
			{
				_departmentsServiceMock.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), 1, It.IsAny<bool>()))
					.ReturnsAsync(new DepartmentMember { IsDisabled = true });

				await SendNotificationKind(kind, 1);

				_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync(It.IsAny<string>(), It.IsAny<int>(),
					It.IsAny<ChatbotOutboundMessage>()), Times.Never);
			}

			[TestCase("notification")]
			[TestCase("calendar")]
			[TestCase("cancellation")]
			[TestCase("trouble")]
			public async Task broadcast_suppression_prevents_chat_notifications(string kind)
			{
				var original = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
				try
				{
					Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = true;
					await SendNotificationKind(kind, int.MaxValue);
					_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync(It.IsAny<string>(), It.IsAny<int>(),
						It.IsAny<ChatbotOutboundMessage>()), Times.Never);
				}
				finally
				{
					Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = original;
				}
			}

			[Test]
			public async Task failed_chat_projection_does_not_send_private_content_or_block_sms()
			{
				var profile = new UserProfile { UserId = TestData.Users.TestUser1Id, SendNotificationSms = true };
				_protectedProjectionServiceMock.Setup(x => x.BuildNotificationSafeMessageAsync(1, It.IsAny<Message>(),
					ProtectedDataEgressChannel.ChatPlatform, It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("Projection unavailable"));

				var sent = await _communicationService.SendNotificationAsync(profile.UserId, 1, "Private location", null,
					null, "Private title", profile);

				Assert.That(sent, Is.True);
				_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync(It.IsAny<string>(), It.IsAny<int>(),
					It.IsAny<ChatbotOutboundMessage>()), Times.Never);
				_smsServiceMock.Verify(x => x.SendNotificationAsync(profile.UserId, 1, "Private title Private location", null, profile), Times.Once);
			}

			[Test]
			public async Task failed_chat_send_does_not_block_other_trouble_alert_recipients_or_push()
			{
				var first = new UserProfile { UserId = TestData.Users.TestUser1Id, SendPush = true };
				var second = new UserProfile { UserId = TestData.Users.TestUser2Id, SendPush = true };
				_chatbotOutboundServiceMock.Setup(x => x.SendToUserAsync(first.UserId, 1, It.IsAny<ChatbotOutboundMessage>()))
					.ThrowsAsync(new InvalidOperationException("Platform unavailable"));

				var sent = await _communicationService.SendTroubleAlertAsync(new TroubleAlertEvent(), new Unit { Name = "Engine 1" },
					null, null, 1, null, "Station 1", new List<UserProfile> { first, second });

				Assert.That(sent, Is.True);
				_chatbotOutboundServiceMock.Verify(x => x.SendToUserAsync(second.UserId, 1,
					It.Is<ChatbotOutboundMessage>(m => m.Body.Contains("Station 1"))), Times.Once);
				_pushServiceMock.Verify(x => x.PushCall(It.IsAny<StandardPushCall>(), first.UserId, first, null), Times.Once);
				_pushServiceMock.Verify(x => x.PushCall(It.IsAny<StandardPushCall>(), second.UserId, second, null), Times.Once);
			}

			private Task<bool> SendNotificationKind(string kind, int departmentId)
			{
				var profile = new UserProfile { UserId = TestData.Users.TestUser1Id };
				switch (kind)
				{
					case "notification":
						return _communicationService.SendNotificationAsync(profile.UserId, departmentId, "Body", null, null, profile: profile);
					case "calendar":
						return _communicationService.SendCalendarAsync(profile.UserId, departmentId, "Body", null, profile: profile);
					case "cancellation":
						return _communicationService.SendCancelCallAsync(new Call { DepartmentId = departmentId },
							new CallDispatch { UserId = profile.UserId }, null, departmentId, profile);
					default:
						return _communicationService.SendTroubleAlertAsync(new TroubleAlertEvent(), new Unit(), null, null,
							departmentId, null, null, new List<UserProfile> { profile });
				}
			}
		}

		[TestFixture]
		public class when_sending_a_communication : with_the_communication_service
		{
			[Test]
			public async Task weather_alerts_use_email_and_push_only()
			{
				var message = new Message
				{
					MessageId = 42,
					Type = (int)MessageTypes.WeatherAlert,
					Subject = "Tornado Warning",
					Body = "Take shelter immediately.",
					ReceivingUserId = TestData.Users.TestUser1Id,
					SystemGenerated = true
				};
				var profile = new UserProfile
				{
					UserId = TestData.Users.TestUser1Id,
					SendNotificationSms = true,
					SendNotificationEmail = true,
					SendNotificationPush = true,
					MobileNumberVerified = true,
					EmailVerified = true
				};

				await _communicationService.SendMessageAsync(message, "Weather Alert System", null, 1, profile);

				_smsServiceMock.Verify(m => m.SendMessageAsync(
					It.IsAny<Message>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<UserProfile>(), It.IsAny<Payment>()), Times.Never());
				_emailServiceMock.Verify(m => m.SendMessageAsync(
					message, "Weather Alert System", 1, profile, message.ReceivingUser), Times.Once());
				_pushServiceMock.Verify(m => m.PushMessage(
					It.IsAny<StandardPushMessage>(), TestData.Users.TestUser1Id, profile), Times.Once());
				_chatbotOutboundServiceMock.Verify(m => m.SendToUserAsync(
					It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ChatbotOutboundMessage>()), Times.Never());
			}

			[Test]
			public async Task normal_messages_are_still_sent_to_chatbot()
			{
				var message = new Message
				{
					MessageId = 43,
					Type = (int)MessageTypes.Normal,
					Subject = "Station update",
					Body = "Briefing starts at 18:00.",
					ReceivingUserId = TestData.Users.TestUser1Id,
					SystemGenerated = true
				};
				var profile = new UserProfile { UserId = TestData.Users.TestUser1Id };

				await _communicationService.SendMessageAsync(message, "System", null, 1, profile);

				_chatbotOutboundServiceMock.Verify(m => m.SendToUserAsync(
					TestData.Users.TestUser1Id,
					1,
					It.Is<ChatbotOutboundMessage>(outbound =>
						outbound.Type == ChatbotOutboundType.Message &&
						outbound.Title == message.Subject &&
						outbound.Body == message.Body &&
						outbound.ReferenceId == message.MessageId.ToString())), Times.Once());
			}

			//[Test]
			public async Task should_be_able_to_send_message()
			{
				Message message = new Message();
				message.Subject = "Test";
				message.Body = "Test Body";
				message.IsBroadcast = true;
				message.ReceivingUserId = TestData.Users.TestUser1Id;
				message.ReceivingUser = new IdentityUser()
				{
					UserId = TestData.Users.TestUser1Id,
					Email = "test@resgrid.com"
				};

				UserProfile profile = new UserProfile();
				profile.UserId = TestData.Users.TestUser1Id;
				profile.MobileNumber = "555-555-5555";
				profile.MobileCarrier = (int)MobileCarriers.Att;
				profile.SendMessageEmail = true;
				profile.SendMessagePush = true;
				profile.SendMessageSms = true;

				Payment payment = new Payment();
				payment.PlanId = 2;

				await _communicationService.SendMessageAsync(message, "Test Sender", "0000000", 1, profile);
				_smsServiceMock.Verify(m => m.SendMessageAsync(message, "0000000", 1, profile, payment));
				_emailServiceMock.Verify(m => m.SendMessageAsync(message, "Test Sender", 1, profile, message.ReceivingUser));
				_pushServiceMock.Verify(m => m.PushMessage(It.IsAny<StandardPushMessage>(), TestData.Users.TestUser1Id, profile));
			}

			//[Test]
			public async Task should_be_able_to_send_call()
			{
				Call call = new Call();
				call.DepartmentId = 1;
				call.Name = "Priority 1E Cardiac Arrest D12";
				call.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call.MapPage = "22T";
				call.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call.Dispatches = new Collection<CallDispatch>();
				call.LoggedOn = DateTime.Now;
				call.ReportingUserId = TestData.Users.TestUser1Id;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser1Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd1);

				Payment payment = new Payment();
				payment.PlanId = 2;

				await _communicationService.SendCallAsync(call, cd, null, 1, null);
				_smsServiceMock.Verify(m => m.SendCallAsync(call, cd, null, 1, null, null, payment));
				_emailServiceMock.Verify(m => m.SendCallAsync(call, cd, null));
				//_pushServiceMock.Verify(m => m.PushCall(It.IsAny<StandardPushCall>(), Users.TestUser1Id));
			}

			[TestCase("NWO:00000000-0000-0000-0000-000000000019", "NWO:00000000-0000-0000-0000-000000000019")]
			[TestCase(null, null)]
			[TestCase(" ", null)]
			public async Task notification_event_code_rides_only_on_the_push(string eventCode, string expectedId)
			{
				var profile = new UserProfile { UserId = TestData.Users.TestUser1Id, SendNotificationSms = true, MobileNumberVerified = true, SendNotificationPush = true };

				await _communicationService.SendNotificationAsync(profile.UserId, 1, "WO-2026-000019: needs attention", "15555550100", new Department { Code = "ABCD" }, "Work order", profile, false, eventCode);

				_pushServiceMock.Verify(m => m.PushNotification(It.Is<StandardPushMessage>(s => s.Id == expectedId && s.Title == "Work order"), profile.UserId, profile), Times.Once);
				_smsServiceMock.Verify(m => m.SendNotificationAsync(profile.UserId, 1, "Work order WO-2026-000019: needs attention", "15555550100", profile), Times.Once);
			}

			[TestCase(true, true)]
			[TestCase(null, true)]
			[TestCase(false, false)]
			public async Task notification_sms_respects_mobile_verification(bool? mobileNumberVerified, bool shouldSend)
			{
				// Arrange
				var profile = new UserProfile
				{
					UserId = TestData.Users.TestUser1Id,
					SendNotificationSms = true,
					MobileNumberVerified = mobileNumberVerified
				};

				// Act
				await _communicationService.SendNotificationAsync(profile.UserId, 1,
					"An event is coming up.", "15555550100", new Department(), "Notification", profile);

				// Assert
				_smsServiceMock.Verify(m => m.SendNotificationAsync(profile.UserId, 1,
					"Notification An event is coming up.", "15555550100", profile),
					shouldSend ? Times.Once() : Times.Never());
			}

			[TestCase(true, true)]
			[TestCase(null, true)]
			[TestCase(false, false)]
			public async Task calendar_notification_sms_respects_mobile_verification(bool? mobileNumberVerified, bool shouldSend)
			{
				// Arrange
				var profile = new UserProfile
				{
					UserId = TestData.Users.TestUser1Id,
					SendNotificationSms = true,
					MobileNumberVerified = mobileNumberVerified
				};

				// Act
				await _communicationService.SendCalendarAsync(profile.UserId, 1,
					"on 7/22 at 18:00. Reply YES or NO.", "15555550100", "New: Drill", profile);

				// Assert
				_smsServiceMock.Verify(m => m.SendNotificationAsync(profile.UserId, 1,
					"New: Drill on 7/22 at 18:00. Reply YES or NO.", "15555550100", profile),
					shouldSend ? Times.Once() : Times.Never());
			}
		}
	}
}
