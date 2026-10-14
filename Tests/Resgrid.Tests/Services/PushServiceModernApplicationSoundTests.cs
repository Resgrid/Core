using System.Threading.Tasks;
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
	}
}
