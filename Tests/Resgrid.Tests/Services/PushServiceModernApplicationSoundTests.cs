using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Modern application sounds are on for every department and user: the department-wide
	/// "Enable Modern Notifications" setting and the per-user profile opt-in were retired, so every
	/// push names a modern sound and nothing is looked up to choose between modern and legacy.
	/// </summary>
	[TestFixture]
	public class PushServiceModernApplicationSoundTests
	{
		private const int DepartmentId = 17;
		private const string UserId = "modern-sound-user";

		private const string DepartmentCode = "TEST";

		private Mock<INovuProvider> _novuProvider;
		private PushService _pushService;
		private bool _departmentWasAlreadyBypassed;

		[SetUp]
		public void SetUp()
		{
			_departmentWasAlreadyBypassed = SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(DepartmentId);
			SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Add(DepartmentId);

			_novuProvider = new Mock<INovuProvider>();

			_novuProvider
				.Setup(x => x.SendUserMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(true);

			_novuProvider
				.Setup(x => x.SendUserNotification(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(true);

			_novuProvider
				.Setup(x => x.SendUserDispatch(
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<bool>(),
					It.IsAny<int>(),
					It.IsAny<string>()))
				.ReturnsAsync(true);

			_novuProvider
				.Setup(x => x.SendUnitDispatch(
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<int>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<bool>(),
					It.IsAny<int>(),
					It.IsAny<string>()))
				.ReturnsAsync(true);

			_pushService = new PushService(
				new Mock<IPushLogsService>().Object,
				new Mock<IUserProfileService>().Object,
				_novuProvider.Object,
				new Mock<IUnitsService>().Object);
		}

		[TearDown]
		public void TearDown()
		{
			if (!_departmentWasAlreadyBypassed)
				SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Remove(DepartmentId);
		}

		private static string Sound(PushSoundTypes type) => ((int)type).ToString();

		private static StandardPushMessage Message(int messageId, string title) => new StandardPushMessage
		{
			DepartmentId = DepartmentId,
			DepartmentCode = DepartmentCode,
			MessageId = messageId,
			Title = title,
			SubTitle = "Body"
		};

		[Test]
		public async Task PushMessage_plays_the_modern_message_sound()
		{
			var profile = new UserProfile { UserId = UserId, SendMessagePush = true };
			var message = Message(42, "Message");

			await _pushService.PushMessage(message, UserId, profile);

			_novuProvider.Verify(x => x.SendUserMessage(message.Title, message.SubTitle, UserId, DepartmentCode, "M42",
				Sound(PushSoundTypes.ModernMessage)), Times.Once);
		}

		[Test]
		public async Task PushNotification_plays_the_modern_notification_sound()
		{
			var profile = new UserProfile { UserId = UserId, SendNotificationPush = true };
			var message = Message(43, "Notification");

			await _pushService.PushNotification(message, UserId, profile);

			_novuProvider.Verify(x => x.SendUserNotification(message.Title, message.SubTitle, UserId, DepartmentCode, "N43",
				Sound(PushSoundTypes.ModernNotification)), Times.Once);
		}

		[Test]
		public async Task PushICNotification_plays_the_modern_notification_sound()
		{
			var profile = new UserProfile { UserId = UserId, SendNotificationPush = true };
			var message = Message(44, "IC Notification");

			await _pushService.PushICNotification(message, UserId, profile);

			_novuProvider.Verify(x => x.SendICUserNotification(message.Title, message.SubTitle, UserId, DepartmentCode, "N44",
				Sound(PushSoundTypes.ModernNotification)), Times.Once);
		}

		[Test]
		public async Task PushChatMessage_plays_the_modern_chat_sound()
		{
			var profile = new UserProfile { UserId = UserId, SendMessagePush = true };
			var message = Message(0, "Chat");

			await _pushService.PushChatMessage(message, UserId, "CH:abc", 3, false, profile);

			_novuProvider.Verify(x => x.SendUserChatMessage(message.Title, message.SubTitle, UserId, DepartmentCode, "CH:abc",
				Sound(PushSoundTypes.ModernChat), 3), Times.Once);
		}

		[Test]
		public async Task PushNotificationUnit_plays_the_modern_notification_sound()
		{
			var message = Message(45, "Unit Notification");

			await _pushService.PushNotificationUnit(message, 31);

			_novuProvider.Verify(x => x.SendUnitDispatch(message.Title, message.SubTitle, 31, DepartmentCode, "N45",
				Sound(PushSoundTypes.ModernNotification), true, 0, "#000000"), Times.Once);
		}

		[TestCase(CallPriority.Low, PushSoundTypes.ModernCallLow)]
		[TestCase(CallPriority.Medium, PushSoundTypes.ModernCallMedium)]
		[TestCase(CallPriority.High, PushSoundTypes.ModernCallHigh)]
		[TestCase(CallPriority.Emergency, PushSoundTypes.ModernCallEmergency)]
		public async Task PushCall_plays_the_modern_sound_for_the_priority(CallPriority priority, PushSoundTypes expectedSound)
		{
			var profile = new UserProfile { UserId = UserId, SendPush = true };
			var call = new StandardPushCall
			{
				DepartmentId = DepartmentId,
				DepartmentCode = DepartmentCode,
				CallId = 99,
				Priority = (int)priority,
				Title = "Dispatch",
				SubTitle = "Address"
			};

			await _pushService.PushCall(call, UserId, profile);

			_novuProvider.Verify(x => x.SendUserDispatch(call.Title, call.SubTitle, UserId, DepartmentCode, "C99",
				Sound(expectedSound), true, 0, null), Times.Once);
		}

		[Test]
		public async Task PushCallUnit_plays_the_modern_sound_for_the_priority()
		{
			var call = new StandardPushCall { DepartmentId = DepartmentId, DepartmentCode = DepartmentCode, CallId = 102,
				Priority = (int)CallPriority.Emergency, Title = "Dispatch", SubTitle = "Address" };

			await _pushService.PushCallUnit(call, 31);

			_novuProvider.Verify(x => x.SendUnitDispatch(call.Title, call.SubTitle, 31, DepartmentCode, "C102",
				Sound(PushSoundTypes.ModernCallEmergency), true, 0, null), Times.Once);
		}

		[Test]
		public async Task A_custom_priority_without_a_tone_plays_the_modern_high_sound()
		{
			var profile = new UserProfile { UserId = UserId, SendPush = true };
			var call = new StandardPushCall { DepartmentId = DepartmentId, DepartmentCode = DepartmentCode, CallId = 103,
				Priority = 4, Title = "Dispatch", SubTitle = "Address" };

			await _pushService.PushCall(call, UserId, profile, new DepartmentCallPriority { Tone = 0, Color = "#123456" });

			_novuProvider.Verify(x => x.SendUserDispatch(call.Title, call.SubTitle, UserId, DepartmentCode, "C103",
				Sound(PushSoundTypes.ModernCallHigh), true, 0, "#123456"), Times.Once);
		}

		[Test]
		public async Task A_custom_priority_with_a_tone_plays_that_tone()
		{
			var profile = new UserProfile { UserId = UserId, SendPush = true };
			var call = new StandardPushCall { DepartmentId = DepartmentId, DepartmentCode = DepartmentCode, CallId = 104,
				Priority = 4, Title = "Dispatch", SubTitle = "Address" };

			await _pushService.PushCall(call, UserId, profile, new DepartmentCallPriority { Tone = 4, Color = "#123456" });

			_novuProvider.Verify(x => x.SendUserDispatch(call.Title, call.SubTitle, UserId, DepartmentCode, "C104",
				"c4", true, 0, "#123456"), Times.Once);
		}

		[TestCase(CallPriority.Low)]
		[TestCase(CallPriority.Emergency)]
		[TestCase(CallPriority.High)]
		public async Task A_dispatch_re_notification_always_plays_the_re_notify_sound(CallPriority priority)
		{
			var profile = new UserProfile { UserId = UserId, SendPush = true };
			var call = new StandardPushCall
			{
				DepartmentId = DepartmentId,
				DepartmentCode = DepartmentCode,
				CallId = 100,
				Priority = (int)priority,
				Title = "Reminder 1/3: Call Dispatch",
				SubTitle = "Address",
				SoundType = PushSoundTypes.ModernCallRenotify
			};

			await _pushService.PushCall(call, UserId, profile, new DepartmentCallPriority { Tone = 4 });

			_novuProvider.Verify(x => x.SendUserDispatch(call.Title, call.SubTitle, UserId, DepartmentCode, "C100",
				"27", true, 0, It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task A_unit_re_notification_plays_the_re_notify_sound()
		{
			var call = new StandardPushCall { DepartmentId = DepartmentId, DepartmentCode = DepartmentCode, CallId = 101, Priority = (int)CallPriority.Medium,
				Title = "Reminder 1/3: Call Dispatch", SoundType = PushSoundTypes.ModernCallRenotify };

			await _pushService.PushCallUnit(call, 31);

			_novuProvider.Verify(x => x.SendUnitDispatch(call.Title, call.SubTitle, 31, DepartmentCode, "C101", "27", true, 0, It.IsAny<string>()), Times.Once);
		}

		[Test]
		public void The_re_notify_sound_type_is_27_and_names_its_bundled_file()
		{
			((int)PushSoundTypes.ModernCallRenotify).Should().Be(27);
			PushSoundFile.GetModernStem("27").Should().Be("moderncallrenotify");
		}
	}
}
