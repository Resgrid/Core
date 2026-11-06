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
	[TestFixture]
	public class PushServiceModernApplicationSoundTests
	{
		private const int DepartmentId = 17;
		private const string UserId = "modern-sound-user";

		private const string DepartmentCode = "TEST";

		private Mock<INovuProvider> _novuProvider;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private PushService _pushService;
		private bool _departmentWasAlreadyBypassed;

		[SetUp]
		public void SetUp()
		{
			_departmentWasAlreadyBypassed = SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(DepartmentId);
			SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Add(DepartmentId);

			_novuProvider = new Mock<INovuProvider>();
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();

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

			_pushService = new PushService(
				new Mock<IPushLogsService>().Object,
				new Mock<IUserProfileService>().Object,
				_novuProvider.Object,
				_departmentSettingsService.Object,
				new Mock<IUnitsService>().Object);
		}

		[TearDown]
		public void TearDown()
		{
			if (!_departmentWasAlreadyBypassed)
				SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Remove(DepartmentId);
		}

		[TestCase(true, false, PushSoundTypes.ModernMessage)]
		[TestCase(false, true, PushSoundTypes.ModernMessage)]
		[TestCase(true, true, PushSoundTypes.ModernMessage)]
		[TestCase(false, false, PushSoundTypes.Message)]
		public async Task PushMessage_uses_effective_department_or_user_sound_setting(
			bool departmentEnabled,
			bool userEnabled,
			PushSoundTypes expectedSound)
		{
			_departmentSettingsService
				.Setup(x => x.GetModernNotificationsEnabledAsync(DepartmentId, false))
				.ReturnsAsync(departmentEnabled);

			var profile = new UserProfile
			{
				UserId = UserId,
				SendMessagePush = true,
				EnableModernApplicationSounds = userEnabled
			};
			var message = new StandardPushMessage
			{
				DepartmentId = DepartmentId,
				DepartmentCode = DepartmentCode,
				MessageId = 42,
				Title = "Message",
				SubTitle = "Body"
			};

			await _pushService.PushMessage(message, UserId, profile);

			_novuProvider.Verify(x => x.SendUserMessage(
				message.Title,
				message.SubTitle,
				UserId,
				DepartmentCode,
				"M42",
				((int)expectedSound).ToString()), Times.Once);
		}

		[Test]
		public async Task PushNotification_uses_legacy_sound_when_department_and_user_settings_are_disabled()
		{
			_departmentSettingsService
				.Setup(x => x.GetModernNotificationsEnabledAsync(DepartmentId, false))
				.ReturnsAsync(false);

			var profile = new UserProfile
			{
				UserId = UserId,
				SendNotificationPush = true,
				EnableModernApplicationSounds = false
			};
			var message = new StandardPushMessage
			{
				DepartmentId = DepartmentId,
				DepartmentCode = DepartmentCode,
				MessageId = 43,
				Title = "Notification",
				SubTitle = "Body"
			};

			await _pushService.PushNotification(message, UserId, profile);

			_novuProvider.Verify(x => x.SendUserNotification(
				message.Title,
				message.SubTitle,
				UserId,
				DepartmentCode,
				"N43",
				((int)PushSoundTypes.Notifiation).ToString()), Times.Once);
		}

		[TestCase(false, false, PushSoundTypes.CallEmergency)]
		[TestCase(false, true, PushSoundTypes.ModernCallEmergency)]
		[TestCase(true, false, PushSoundTypes.ModernCallEmergency)]
		public async Task PushCall_uses_effective_department_or_user_sound_setting(
			bool departmentEnabled,
			bool userEnabled,
			PushSoundTypes expectedSound)
		{
			_departmentSettingsService
				.Setup(x => x.GetModernNotificationsEnabledAsync(DepartmentId, false))
				.ReturnsAsync(departmentEnabled);

			var profile = new UserProfile
			{
				UserId = UserId,
				SendPush = true,
				EnableModernApplicationSounds = userEnabled
			};
			var call = new StandardPushCall
			{
				DepartmentId = DepartmentId,
				DepartmentCode = DepartmentCode,
				CallId = 99,
				Priority = (int)CallPriority.Emergency,
				Title = "Dispatch",
				SubTitle = "Address"
			};

			await _pushService.PushCall(call, UserId, profile);

			_novuProvider.Verify(x => x.SendUserDispatch(
				call.Title,
				call.SubTitle,
				UserId,
				DepartmentCode,
				"C99",
				((int)expectedSound).ToString(),
				true,
				0,
				null), Times.Once);
		}

		[TestCase(false, false, CallPriority.Low)]
		[TestCase(true, false, CallPriority.Emergency)]
		[TestCase(false, true, CallPriority.High)]
		public async Task A_dispatch_re_notification_always_plays_the_re_notify_sound(bool departmentEnabled, bool userEnabled, CallPriority priority)
		{
			_departmentSettingsService
				.Setup(x => x.GetModernNotificationsEnabledAsync(DepartmentId, false))
				.ReturnsAsync(departmentEnabled);

			var profile = new UserProfile { UserId = UserId, SendPush = true, EnableModernApplicationSounds = userEnabled };
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
