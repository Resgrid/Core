using System;
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
	[TestFixture]
	public class PushServiceUserRegistrationTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 7;
		private const string Code = "DEPT";
		private const string DeviceId = "device-token";
		private const string Email = "user@example.com";
		private const string FirstName = "First";
		private const string LastName = "Last";

		private Mock<INovuProvider> _novuProvider;
		private Mock<IUserProfileService> _userProfileService;
		private PushService _pushService;

		[SetUp]
		public void SetUp()
		{
			_novuProvider = new Mock<INovuProvider>();
			_userProfileService = new Mock<IUserProfileService>();
			_userProfileService.Setup(x => x.GetProfileByUserIdAsync(UserId, It.IsAny<bool>())).ReturnsAsync(new UserProfile
			{
				UserId = UserId,
				FirstName = FirstName,
				LastName = LastName,
				MembershipEmail = Email
			});

			_pushService = new PushService(
				Mock.Of<IPushLogsService>(),
				Mock.Of<INotificationProvider>(),
				_userProfileService.Object,
				Mock.Of<IUnitNotificationProvider>(),
				_novuProvider.Object,
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IUnitsService>());
		}

		[Test]
		public async Task Register_responder_should_create_subscriber_before_credential_write()
		{
			_novuProvider.Setup(x => x.UpdateUserSubscriberFcm(UserId, Code, DeviceId)).ReturnsAsync(true);

			var result = await _pushService.Register(CreatePushUri(source: null));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.CreateUserSubscriber(UserId, Code, DepartmentId, Email, FirstName, LastName), Times.Once);
			_novuProvider.Verify(x => x.CreateICUserSubscriber(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novuProvider.Verify(x => x.UpdateUserSubscriberFcm(UserId, Code, DeviceId), Times.Once);
		}

		[Test]
		public async Task Register_ic_should_create_ic_subscriber_before_credential_write()
		{
			_novuProvider.Setup(x => x.UpdateICUserSubscriberFcm(UserId, Code, DeviceId)).ReturnsAsync(true);

			var result = await _pushService.Register(CreatePushUri(source: "IC"));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.CreateICUserSubscriber(UserId, Code, DepartmentId, Email, FirstName, LastName), Times.Once);
			_novuProvider.Verify(x => x.CreateUserSubscriber(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novuProvider.Verify(x => x.UpdateICUserSubscriberFcm(UserId, Code, DeviceId), Times.Once);
		}

		[Test]
		public async Task Register_should_still_write_credentials_when_subscriber_create_fails()
		{
			_novuProvider.Setup(x => x.CreateUserSubscriber(UserId, Code, DepartmentId, Email, FirstName, LastName))
				.ReturnsAsync(false);
			_novuProvider.Setup(x => x.UpdateUserSubscriberFcm(UserId, Code, DeviceId)).ReturnsAsync(true);

			var result = await _pushService.Register(CreatePushUri(source: null));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.UpdateUserSubscriberFcm(UserId, Code, DeviceId), Times.Once);
		}

		[Test]
		public async Task Register_should_still_write_credentials_when_subscriber_create_throws()
		{
			_novuProvider.Setup(x => x.CreateUserSubscriber(UserId, Code, DepartmentId, Email, FirstName, LastName))
				.ThrowsAsync(new InvalidOperationException("Novu unavailable"));
			_novuProvider.Setup(x => x.UpdateUserSubscriberFcm(UserId, Code, DeviceId)).ReturnsAsync(true);

			var result = await _pushService.Register(CreatePushUri(source: null));

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.UpdateUserSubscriberFcm(UserId, Code, DeviceId), Times.Once);
		}

		private static PushUri CreatePushUri(string source)
		{
			return new PushUri
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				PlatformType = (int)Platforms.Android,
				PushLocation = Code,
				DeviceId = DeviceId,
				Source = source
			};
		}
	}
}
