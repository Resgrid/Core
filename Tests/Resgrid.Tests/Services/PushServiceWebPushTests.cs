using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Browser and desktop (Electron) registrations arrive as Platform 3 with an FCM web token. They must
	/// land on the web channel (appending), never on the native FCM/APNs channel (which replaces), or a
	/// browser registering would sign the person's phone out of push.
	/// </summary>
	[TestFixture]
	public class PushServiceWebPushTests
	{
		private const string UserId = "user-1";
		private const int UnitId = 9;
		private const int DepartmentId = 7;
		private const string Code = "DEPT";
		private const string Token = "fcm-web-token";

		private Mock<INovuProvider> _novuProvider;
		private Mock<IUnitsService> _unitsService;
		private PushService _pushService;

		[SetUp]
		public void SetUp()
		{
			_novuProvider = new Mock<INovuProvider>();
			_unitsService = new Mock<IUnitsService>();

			var userProfileService = new Mock<IUserProfileService>();
			userProfileService.Setup(x => x.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = UserId, FirstName = "First", LastName = "Last", MembershipEmail = "user@example.com" });

			_pushService = new PushService(
				Mock.Of<IPushLogsService>(),
				Mock.Of<INotificationProvider>(),
				userProfileService.Object,
				Mock.Of<IUnitNotificationProvider>(),
				_novuProvider.Object,
				Mock.Of<IDepartmentSettingsService>(),
				_unitsService.Object);
		}

		[Test]
		public async Task Register_web_should_add_the_token_to_the_user_web_channel_only()
		{
			_novuProvider.Setup(x => x.AddUserSubscriberWebPushToken(UserId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.Register(UserPushUri(source: null));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.CreateUserSubscriber(UserId, Code, DepartmentId, "user@example.com", "First", "Last"), Times.Once);
			_novuProvider.Verify(x => x.AddUserSubscriberWebPushToken(UserId, Code, Token), Times.Once);
			VerifyNoNativeCredentialWrite();
			_novuProvider.Verify(x => x.AddICUserSubscriberWebPushToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Register_web_from_ic_should_add_the_token_to_the_ic_subscriber()
		{
			_novuProvider.Setup(x => x.AddICUserSubscriberWebPushToken(UserId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.Register(UserPushUri(source: "IC"));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.CreateICUserSubscriber(UserId, Code, DepartmentId, "user@example.com", "First", "Last"), Times.Once);
			_novuProvider.Verify(x => x.AddICUserSubscriberWebPushToken(UserId, Code, Token), Times.Once);
			_novuProvider.Verify(x => x.AddUserSubscriberWebPushToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			VerifyNoNativeCredentialWrite();
		}

		[Test]
		public async Task Register_web_should_report_a_rejected_token_write()
		{
			_novuProvider.Setup(x => x.AddUserSubscriberWebPushToken(UserId, Code, Token)).ReturnsAsync(false);

			var result = await _pushService.Register(UserPushUri(source: null));

			result.Should().BeFalse();
		}

		[Test]
		public async Task RegisterUnit_web_should_add_the_token_to_the_unit_web_channel_only()
		{
			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId, Name = "Engine 9" });
			_novuProvider.Setup(x => x.CreateUnitSubscriber(UnitId, Code, DepartmentId, "Engine 9", Token)).ReturnsAsync(true);
			_novuProvider.Setup(x => x.AddUnitSubscriberWebPushToken(UnitId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.RegisterUnit(UnitPushUri());

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.AddUnitSubscriberWebPushToken(UnitId, Code, Token), Times.Once);
			_novuProvider.Verify(x => x.UpdateUnitSubscriberFcm(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novuProvider.Verify(x => x.UpdateUnitSubscriberApns(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task UnRegisterWebPush_should_remove_the_token_from_the_user_web_channel()
		{
			_novuProvider.Setup(x => x.RemoveUserSubscriberWebPushToken(UserId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.UnRegisterWebPush(UserPushUri(source: null));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.RemoveUserSubscriberWebPushToken(UserId, Code, Token), Times.Once);
			_novuProvider.Verify(x => x.RemoveICUserSubscriberWebPushToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task UnRegisterWebPush_from_ic_should_remove_the_token_from_the_ic_subscriber()
		{
			_novuProvider.Setup(x => x.RemoveICUserSubscriberWebPushToken(UserId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.UnRegisterWebPush(UserPushUri(source: "ic"));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.RemoveICUserSubscriberWebPushToken(UserId, Code, Token), Times.Once);
			_novuProvider.Verify(x => x.RemoveUserSubscriberWebPushToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[TestCase(null, Code, Token)]
		[TestCase(UserId, null, Token)]
		[TestCase(UserId, Code, " ")]
		public async Task UnRegisterWebPush_should_refuse_an_incomplete_request(string userId, string code, string token)
		{
			var result = await _pushService.UnRegisterWebPush(new PushUri { UserId = userId, PushLocation = code, DeviceId = token, PlatformType = (int)Platforms.Web });

			result.Should().BeFalse();
			_novuProvider.VerifyNoOtherCalls();
		}

		[Test]
		public async Task UnRegisterUnitWebPush_should_remove_the_token_from_the_unit_web_channel()
		{
			_novuProvider.Setup(x => x.RemoveUnitSubscriberWebPushToken(UnitId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.UnRegisterUnitWebPush(UnitPushUri());

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.RemoveUnitSubscriberWebPushToken(UnitId, Code, Token), Times.Once);
		}

		[Test]
		public async Task UnRegisterUnitWebPush_should_refuse_a_request_without_a_unit()
		{
			var push = UnitPushUri();
			push.UnitId = null;

			var result = await _pushService.UnRegisterUnitWebPush(push);

			result.Should().BeFalse();
			_novuProvider.VerifyNoOtherCalls();
		}

		private void VerifyNoNativeCredentialWrite()
		{
			_novuProvider.Verify(x => x.UpdateUserSubscriberFcm(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novuProvider.Verify(x => x.UpdateUserSubscriberApns(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novuProvider.Verify(x => x.UpdateICUserSubscriberFcm(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novuProvider.Verify(x => x.UpdateICUserSubscriberApns(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		private static PushUri UserPushUri(string source)
		{
			return new PushUri
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				PlatformType = (int)Platforms.Web,
				PushLocation = Code,
				DeviceId = Token,
				Source = source
			};
		}

		private static PushUri UnitPushUri()
		{
			return new PushUri
			{
				UserId = UserId,
				UnitId = UnitId,
				DepartmentId = DepartmentId,
				PlatformType = (int)Platforms.Web,
				PushLocation = Code,
				DeviceId = Token
			};
		}
	}
}
