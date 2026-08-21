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
	public class PushServiceUnitRegistrationTests
	{
		private const int UnitId = 9;
		private const int DepartmentId = 7;
		private const string Code = "DEPT";
		private const string DeviceId = "device-token";

		private Mock<INovuProvider> _novuProvider;
		private Mock<IUnitsService> _unitsService;
		private PushService _pushService;

		[SetUp]
		public void SetUp()
		{
			_novuProvider = new Mock<INovuProvider>();
			_unitsService = new Mock<IUnitsService>();
			_pushService = new PushService(
				Mock.Of<IPushLogsService>(),
				Mock.Of<INotificationProvider>(),
				Mock.Of<IUserProfileService>(),
				Mock.Of<IUnitNotificationProvider>(),
				_novuProvider.Object,
				Mock.Of<IDepartmentSettingsService>(),
				_unitsService.Object);
		}

		[Test]
		public async Task RegisterUnit_should_stop_before_credentials_when_unit_is_missing()
		{
			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync((Unit)null);

			var result = await _pushService.RegisterUnit(CreatePushUri());

			result.Should().BeFalse();
			VerifyNoCredentialWrite();
		}

		[Test]
		public async Task RegisterUnit_should_stop_before_credentials_when_subscriber_creation_is_rejected()
		{
			SetupUnit();
			_novuProvider.Setup(x => x.CreateUnitSubscriber(UnitId, Code, DepartmentId, "Engine 9", DeviceId))
				.ReturnsAsync(false);

			var result = await _pushService.RegisterUnit(CreatePushUri());

			result.Should().BeFalse();
			VerifyNoCredentialWrite();
		}

		[Test]
		public async Task RegisterUnit_should_stop_before_credentials_when_subscriber_creation_throws()
		{
			SetupUnit();
			_novuProvider.Setup(x => x.CreateUnitSubscriber(UnitId, Code, DepartmentId, "Engine 9", DeviceId))
				.ThrowsAsync(new InvalidOperationException("Novu unavailable"));

			var result = await _pushService.RegisterUnit(CreatePushUri());

			result.Should().BeFalse();
			VerifyNoCredentialWrite();
		}

		[Test]
		public async Task RegisterUnit_should_write_credentials_after_subscriber_creation_succeeds()
		{
			SetupUnit();
			_novuProvider.Setup(x => x.CreateUnitSubscriber(UnitId, Code, DepartmentId, "Engine 9", DeviceId))
				.ReturnsAsync(true);
			_novuProvider.Setup(x => x.UpdateUnitSubscriberFcm(UnitId, Code, DeviceId)).ReturnsAsync(true);

			var result = await _pushService.RegisterUnit(CreatePushUri());

			result.Should().BeTrue();
			_novuProvider.Verify(x => x.UpdateUnitSubscriberFcm(UnitId, Code, DeviceId), Times.Once);
		}

		private void SetupUnit()
		{
			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit
			{
				UnitId = UnitId,
				DepartmentId = DepartmentId,
				Name = "Engine 9"
			});
		}

		private static PushUri CreatePushUri()
		{
			return new PushUri
			{
				UnitId = UnitId,
				PlatformType = (int)Platforms.Android,
				PushLocation = Code,
				DeviceId = DeviceId
			};
		}

		private void VerifyNoCredentialWrite()
		{
			_novuProvider.Verify(x => x.UpdateUnitSubscriberApns(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_novuProvider.Verify(x => x.UpdateUnitSubscriberFcm(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}
	}
}
