using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
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

		private Mock<INotificationProvider> _notificationProvider;
		private Mock<INovuProvider> _novuProvider;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private PushService _pushService;

		[SetUp]
		public void SetUp()
		{
			_notificationProvider = new Mock<INotificationProvider>();
			_novuProvider = new Mock<INovuProvider>();
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();

			_notificationProvider
				.Setup(x => x.SendAllNotifications(
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<string>(),
					It.IsAny<bool>(),
					It.IsAny<int>(),
					It.IsAny<string>()))
				.Returns(Task.CompletedTask);

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
				_notificationProvider.Object,
				new Mock<IUserProfileService>().Object,
				new Mock<IUnitNotificationProvider>().Object,
				_novuProvider.Object,
				_departmentSettingsService.Object);
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
				MessageId = 42,
				Title = "Message",
				SubTitle = "Body"
			};

			await _pushService.PushMessage(message, UserId, profile);

			_notificationProvider.Verify(x => x.SendAllNotifications(
				message.Title,
				message.SubTitle,
				UserId,
				"M42",
				((int)expectedSound).ToString(),
				true,
				1,
				"#000000"), Times.Once);
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
				MessageId = 43,
				Title = "Notification",
				SubTitle = "Body"
			};

			await _pushService.PushNotification(message, UserId, profile);

			_notificationProvider.Verify(x => x.SendAllNotifications(
				message.Title,
				message.SubTitle,
				UserId,
				"N43",
				((int)PushSoundTypes.Notifiation).ToString(),
				true,
				1,
				"#000000"), Times.Once);
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
				DepartmentCode = "TEST",
				CallId = 99,
				Priority = (int)CallPriority.Emergency,
				Title = "Dispatch",
				SubTitle = "Address"
			};

			await _pushService.PushCall(call, UserId, profile);

			_notificationProvider.Verify(x => x.SendAllNotifications(
				call.SubTitle,
				call.Title,
				UserId,
				"C99",
				((int)expectedSound).ToString(),
				true,
				0,
				null), Times.Once);
		}
	}
}
