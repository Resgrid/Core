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
	/// The event code is the only field that reaches the device with a caller-supplied value, and
	/// communication test push confirmation depends on it round-tripping the response token. The
	/// return value matters just as much: a caller that reports delivery cannot distinguish a real
	/// send from a swallowed provider failure unless this method says so.
	/// </summary>
	[TestFixture]
	public class PushServiceNotificationEventCodeTests
	{
		private const int DepartmentId = 23;
		private const string UserId = "event-code-user";
		private const string DepartmentCode = "DEPT";

		private Mock<INotificationProvider> _notificationProvider;
		private Mock<INovuProvider> _novuProvider;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private PushService _pushService;
		private bool _departmentWasAlreadyBypassed;

		[SetUp]
		public void SetUp()
		{
			_departmentWasAlreadyBypassed = SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(DepartmentId);
			SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Add(DepartmentId);

			_notificationProvider = new Mock<INotificationProvider>();
			_novuProvider = new Mock<INovuProvider>();
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();

			_notificationProvider
				.Setup(x => x.SendAllNotifications(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns(Task.CompletedTask);

			_novuProvider
				.Setup(x => x.SendUserNotification(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(true);

			_departmentSettingsService
				.Setup(x => x.GetModernNotificationsEnabledAsync(DepartmentId, false))
				.ReturnsAsync(false);

			_pushService = new PushService(
				new Mock<IPushLogsService>().Object,
				_notificationProvider.Object,
				new Mock<IUserProfileService>().Object,
				new Mock<IUnitNotificationProvider>().Object,
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

		private static UserProfile PushEnabledProfile()
		{
			return new UserProfile { UserId = UserId, SendNotificationPush = true };
		}

		private static StandardPushMessage Message(string id = null)
		{
			return new StandardPushMessage
			{
				DepartmentId = DepartmentId,
				DepartmentCode = DepartmentCode,
				MessageId = 42,
				Title = "Communication Test",
				SubTitle = "Body",
				Id = id
			};
		}

		[Test]
		public async Task should_send_a_supplied_event_code_through_to_both_providers()
		{
			await _pushService.PushNotification(Message("CT:9f2c4a1b"), UserId, PushEnabledProfile());

			_notificationProvider.Verify(x => x.SendAllNotifications(It.IsAny<string>(), It.IsAny<string>(), UserId, "CT:9f2c4a1b",
				It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);

			_novuProvider.Verify(x => x.SendUserNotification(It.IsAny<string>(), It.IsAny<string>(), UserId, DepartmentCode,
				"CT:9f2c4a1b", It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task should_fall_back_to_the_message_id_event_code_when_none_is_supplied()
		{
			await _pushService.PushNotification(Message(), UserId, PushEnabledProfile());

			// Existing notification callers set no Id and must keep their "N{MessageId}" codes.
			_notificationProvider.Verify(x => x.SendAllNotifications(It.IsAny<string>(), It.IsAny<string>(), UserId, "N42",
				It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task should_report_failure_when_the_user_has_push_turned_off()
		{
			var result = await _pushService.PushNotification(Message("CT:token"), UserId,
				new UserProfile { UserId = UserId, SendNotificationPush = false });

			result.Should().BeFalse();
			_notificationProvider.Verify(x => x.SendAllNotifications(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task should_report_failure_when_every_provider_fails()
		{
			_notificationProvider
				.Setup(x => x.SendAllNotifications(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>()))
				.ThrowsAsync(new System.Exception("hub down"));

			_novuProvider
				.Setup(x => x.SendUserNotification(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(false);

			var result = await _pushService.PushNotification(Message("CT:token"), UserId, PushEnabledProfile());

			// Previously this returned true unconditionally, so a communication test reported a send
			// that never happened.
			result.Should().BeFalse();
		}

		[Test]
		public async Task should_report_success_when_novu_accepts_even_if_the_legacy_provider_throws()
		{
			_notificationProvider
				.Setup(x => x.SendAllNotifications(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<string>()))
				.ThrowsAsync(new System.Exception("hub down"));

			var result = await _pushService.PushNotification(Message("CT:token"), UserId, PushEnabledProfile());

			result.Should().BeTrue();
		}
	}
}
