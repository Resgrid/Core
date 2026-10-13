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
	/// Signing out of the native app has to take the device's FCM / APNs token off the Novu subscriber it was
	/// registered on, or the phone keeps receiving that person's (or unit's) calls after they signed out.
	/// </summary>
	[TestFixture]
	public class PushServiceNativePushTests
	{
		private const string UserId = "user-1";
		private const int UnitId = 9;
		private const int DepartmentId = 7;
		private const string Code = "DEPT";
		private const string Token = "native-device-token";

		private Mock<INovuProvider> _novuProvider;
		private PushService _pushService;

		[SetUp]
		public void SetUp()
		{
			_novuProvider = new Mock<INovuProvider>();

			_pushService = new PushService(
				Mock.Of<IPushLogsService>(),
				Mock.Of<IUserProfileService>(),
				_novuProvider.Object,
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IUnitsService>());
		}

		[Test]
		public async Task UnRegisterNativePush_should_remove_the_token_from_the_user_subscriber()
		{
			_novuProvider.Setup(x => x.RemoveUserSubscriberNativeToken(UserId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.UnRegisterNativePush(UserPushUri(source: null));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.RemoveUserSubscriberNativeToken(UserId, Code, Token), Times.Once);
			_novuProvider.VerifyNoOtherCalls();
		}

		[Test]
		public async Task UnRegisterNativePush_from_ic_should_remove_the_token_from_the_ic_subscriber()
		{
			_novuProvider.Setup(x => x.RemoveICUserSubscriberNativeToken(UserId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.UnRegisterNativePush(UserPushUri(source: "ic"));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.RemoveICUserSubscriberNativeToken(UserId, Code, Token), Times.Once);
			_novuProvider.VerifyNoOtherCalls();
		}

		[Test]
		public async Task UnRegisterNativePush_should_report_a_token_that_could_not_be_removed()
		{
			_novuProvider.Setup(x => x.RemoveUserSubscriberNativeToken(UserId, Code, Token)).ReturnsAsync(false);

			var result = await _pushService.UnRegisterNativePush(UserPushUri(source: null));

			result.Should().BeFalse();
		}

		[TestCase(null, Code, Token)]
		[TestCase(UserId, null, Token)]
		[TestCase(UserId, Code, " ")]
		public async Task UnRegisterNativePush_should_refuse_an_incomplete_request(string userId, string code, string token)
		{
			var result = await _pushService.UnRegisterNativePush(new PushUri { UserId = userId, PushLocation = code, DeviceId = token });

			result.Should().BeFalse();
			_novuProvider.VerifyNoOtherCalls();
		}

		[Test]
		public async Task UnRegisterUnitNativePush_should_remove_the_token_from_the_unit_subscriber()
		{
			_novuProvider.Setup(x => x.RemoveUnitSubscriberNativeToken(UnitId, Code, Token)).ReturnsAsync(true);

			var result = await _pushService.UnRegisterUnitNativePush(UnitPushUri());

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.RemoveUnitSubscriberNativeToken(UnitId, Code, Token), Times.Once);
			_novuProvider.VerifyNoOtherCalls();
		}

		[Test]
		public async Task UnRegisterUnitNativePush_should_refuse_a_request_without_a_unit()
		{
			var push = UnitPushUri();
			push.UnitId = null;

			var result = await _pushService.UnRegisterUnitNativePush(push);

			result.Should().BeFalse();
			_novuProvider.VerifyNoOtherCalls();
		}

		private static PushUri UserPushUri(string source)
		{
			return new PushUri
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				PlatformType = (int)Platforms.Android,
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
				PlatformType = (int)Platforms.iOS,
				PushLocation = Code,
				DeviceId = Token
			};
		}
	}
}
