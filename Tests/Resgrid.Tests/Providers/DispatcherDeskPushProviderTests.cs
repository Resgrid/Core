using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Messaging;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Desk pushes (M0324, plan D6) go to the Dispatch app's own Novu subscriber, {code}_Dispatch_User_{userId}, which only
	/// the Dispatch app registers on, so the Responder app on the same person's phone never rings for desk work.
	/// </summary>
	[TestFixture]
	public class DispatcherDeskPushProviderTests
	{
		private const int DepartmentId = 77;
		private const string Code = "DESK";
		private const string UserId = "dispatcher-1";
		private const string Token = "device-token";

		private Mock<INovuProvider> _novu;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private DispatcherDeskPushProvider _provider;

		[SetUp]
		public void SetUp()
		{
			_novu = new Mock<INovuProvider>();
			_novu.Setup(n => n.CreateDispatchUserSubscriber(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(true);
			_novu.Setup(n => n.UpdateDispatchUserSubscriberApns(UserId, Code, Token)).ReturnsAsync(true);
			_novu.Setup(n => n.UpdateDispatchUserSubscriberFcm(UserId, Code, Token)).ReturnsAsync(true);
			_novu.Setup(n => n.AddDispatchUserSubscriberWebPushToken(UserId, Code, Token)).ReturnsAsync(true);
			_novu.Setup(n => n.RemoveDispatchUserSubscriberNativeToken(UserId, Code, Token)).ReturnsAsync(true);
			_novu.Setup(n => n.RemoveDispatchUserSubscriberWebPushToken(UserId, Code, Token)).ReturnsAsync(true);
			_novu.Setup(n => n.SendDispatchUserDeskPush(It.IsAny<string>(), It.IsAny<string>(), UserId, Code, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = Code });

			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = UserId, FirstName = "Dana", LastName = "Desk", MembershipEmail = "dana@example.com" });

			_provider = new DispatcherDeskPushProvider(_novu.Object, _departments.Object, _profiles.Object);
		}

		[Test]
		public async Task Registering_an_iPhone_creates_the_desk_subscriber_then_writes_the_apns_token()
		{
			var sequence = new MockSequence();
			_novu.InSequence(sequence).Setup(n => n.CreateDispatchUserSubscriber(UserId, Code, DepartmentId, "dana@example.com", "Dana", "Desk")).ReturnsAsync(true);
			_novu.InSequence(sequence).Setup(n => n.UpdateDispatchUserSubscriberApns(UserId, Code, Token)).ReturnsAsync(true);

			var registered = await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.iOS, Token);

			registered.Should().BeTrue();
			_novu.Verify(n => n.CreateDispatchUserSubscriber(UserId, Code, DepartmentId, "dana@example.com", "Dana", "Desk"), Times.Once);
			_novu.Verify(n => n.UpdateDispatchUserSubscriberApns(UserId, Code, Token), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Registering_an_android_device_writes_the_fcm_token()
		{
			(await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Android, Token)).Should().BeTrue();

			_novu.Verify(n => n.UpdateDispatchUserSubscriberFcm(UserId, Code, Token), Times.Once);
			_novu.Verify(n => n.UpdateDispatchUserSubscriberApns(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Registering_a_browser_adds_the_web_token()
		{
			(await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Web, Token)).Should().BeTrue();

			_novu.Verify(n => n.AddDispatchUserSubscriberWebPushToken(UserId, Code, Token), Times.Once);
		}

		[Test]
		public async Task Registration_never_touches_the_shared_user_or_IC_subscribers()
		{
			await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Android, Token);
			await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.iOS, Token);

			_novu.Verify(n => n.CreateUserSubscriber(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novu.Verify(n => n.UpdateUserSubscriberFcm(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novu.Verify(n => n.UpdateUserSubscriberApns(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novu.Verify(n => n.UpdateICUserSubscriberFcm(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task A_rejected_credential_write_is_reported()
		{
			_novu.Setup(n => n.UpdateDispatchUserSubscriberFcm(UserId, Code, Token)).ReturnsAsync(false);

			(await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Android, Token)).Should().BeFalse();
		}

		[TestCase(0)]
		[TestCase(4)]
		[TestCase(99)]
		public async Task An_unsupported_platform_registers_nothing(int platform)
		{
			(await _provider.RegisterDeviceAsync(DepartmentId, UserId, platform, Token)).Should().BeFalse();
			(await _provider.UnregisterDeviceAsync(DepartmentId, UserId, platform, Token)).Should().BeFalse();

			_novu.VerifyNoOtherCalls();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("  ")]
		public async Task A_missing_token_or_user_does_nothing(string value)
		{
			(await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Android, value)).Should().BeFalse();
			(await _provider.RegisterDeviceAsync(DepartmentId, value, (int)Platforms.Android, Token)).Should().BeFalse();
			(await _provider.UnregisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Android, value)).Should().BeFalse();
			(await _provider.SendAsync(DepartmentId, value, "t", "b", "DQ:item", PushSoundTypes.ModernDeskOffer)).Should().BeFalse();

			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_department_without_a_code_gets_no_desk_subscriber()
		{
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = " " });

			(await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Android, Token)).Should().BeFalse();
			(await _provider.SendAsync(DepartmentId, UserId, "t", "b", "DQ:item", PushSoundTypes.ModernDeskOffer)).Should().BeFalse();

			_novu.VerifyNoOtherCalls();
		}

		[TestCase(Platforms.iOS)]
		[TestCase(Platforms.Android)]
		public async Task Unregistering_a_phone_removes_only_its_native_token(Platforms platform)
		{
			(await _provider.UnregisterDeviceAsync(DepartmentId, UserId, (int)platform, Token)).Should().BeTrue();

			_novu.Verify(n => n.RemoveDispatchUserSubscriberNativeToken(UserId, Code, Token), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Unregistering_a_browser_removes_its_web_token()
		{
			(await _provider.UnregisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Web, Token)).Should().BeTrue();

			_novu.Verify(n => n.RemoveDispatchUserSubscriberWebPushToken(UserId, Code, Token), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		[TestCase(PushSoundTypes.ModernDeskOffer, "28")]
		[TestCase(PushSoundTypes.ModernDeskQueueAlert, "29")]
		[TestCase(PushSoundTypes.ModernDeskMessage, "30")]
		[TestCase(PushSoundTypes.ModernBreakReminder, "31")]
		[TestCase(PushSoundTypes.ModernBreakOver, "32")]
		public async Task A_desk_push_goes_to_the_desk_subscriber_with_its_event_code_and_sound(PushSoundTypes sound, string type)
		{
			var sent = await _provider.SendAsync(DepartmentId, UserId, "New text from 555-0100", "Tap to take it", "DQ:item-1", sound);

			sent.Should().BeTrue();
			_novu.Verify(n => n.SendDispatchUserDeskPush("New text from 555-0100", "Tap to take it", UserId, Code, "DQ:item-1", type), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_failing_provider_answers_false_and_never_throws()
		{
			_novu.Setup(n => n.SendDispatchUserDeskPush(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.ThrowsAsync(new TimeoutException());
			_departments.Setup(d => d.GetDepartmentByIdAsync(13, It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException("db down"));

			(await _provider.SendAsync(DepartmentId, UserId, "t", "b", "DB:break-1", PushSoundTypes.ModernBreakOver)).Should().BeFalse();
			(await _provider.RegisterDeviceAsync(13, UserId, (int)Platforms.Android, Token)).Should().BeFalse();
			(await _provider.UnregisterDeviceAsync(13, UserId, (int)Platforms.Android, Token)).Should().BeFalse();
		}

		[Test]
		public async Task A_cancelled_request_does_nothing()
		{
			using var cancelled = new CancellationTokenSource();
			cancelled.Cancel();

			(await _provider.SendAsync(DepartmentId, UserId, "t", "b", "DQ:item", PushSoundTypes.ModernDeskOffer, cancelled.Token)).Should().BeFalse();
			(await _provider.RegisterDeviceAsync(DepartmentId, UserId, (int)Platforms.Android, Token, cancelled.Token)).Should().BeFalse();

			_novu.VerifyNoOtherCalls();
		}

		[TestCase("28", "moderndeskoffer")]
		[TestCase("29", "moderndeskqueuealert")]
		[TestCase("30", "moderndeskmessage")]
		[TestCase("31", "modernbreakreminder")]
		[TestCase("32", "modernbreakover")]
		public void Desk_sound_types_map_to_the_dispatch_app_sound_files(string type, string stem)
		{
			PushSoundFile.GetModernStem(type).Should().Be(stem);
			NovuProvider.GetSoundFileNameFromType(type).Should().Be($"{stem}.wav");
		}

		[Test]
		public void Existing_sound_files_are_unchanged()
		{
			NovuProvider.GetSoundFileNameFromType("0").Should().Be("callemergency.wav");
			NovuProvider.GetSoundFileNameFromType("5").Should().Be("message.wav");
			NovuProvider.GetSoundFileNameFromType("27").Should().Be("moderncallrenotify.wav");
			NovuProvider.GetSoundFileNameFromType("c5").Should().Be("c5.wav");
		}

		[TestCase(PushSoundTypes.ModernDeskOffer, "calls")]
		[TestCase(PushSoundTypes.ModernDeskQueueAlert, "calls")]
		[TestCase(PushSoundTypes.ModernDeskMessage, "messages")]
		[TestCase(PushSoundTypes.ModernBreakReminder, "notifications")]
		[TestCase(PushSoundTypes.ModernBreakOver, "notifications")]
		public void Desk_pushes_are_urgent_only_for_work_to_take(PushSoundTypes sound, string channel)
		{
			NovuProvider.GetDeskChannelName(DispatcherDeskPushProvider.SoundType(sound)).Should().Be(channel);
		}

		[Test]
		public void The_desk_workflow_falls_back_to_the_user_dispatch_workflow()
		{
			var saved = ChatConfig.NovuDispatcherDeskWorkflowId;
			try
			{
				ChatConfig.NovuDispatcherDeskWorkflowId = "";
				ChatConfig.EffectiveDispatcherDeskWorkflowId().Should().Be(ChatConfig.NovuDispatchUserWorkflowId);

				ChatConfig.NovuDispatcherDeskWorkflowId = "dispatcher-desk";
				ChatConfig.EffectiveDispatcherDeskWorkflowId().Should().Be("dispatcher-desk");
			}
			finally
			{
				ChatConfig.NovuDispatcherDeskWorkflowId = saved;
			}
		}
	}
}
