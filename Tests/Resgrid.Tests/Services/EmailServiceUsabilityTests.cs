using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class EmailServiceUsabilityTests
	{
		private bool _originalDoNotBroadcast;

		[SetUp]
		public void SetUp()
		{
			_originalDoNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _originalDoNotBroadcast;
		}

		[Test]
		public async Task SendTroubleAlert_WithPersonnelAndTimestamp_ForwardsCompleteDetailsToEmailProvider()
		{
			// Arrange
			var emailProvider = new Mock<IEmailProvider>();
			emailProvider
				.Setup(provider => provider.SendTroubleAlertMail(It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(true);

			var departmentsService = new Mock<IDepartmentsService>();
			departmentsService.Setup(service => service.GetDepartmentByIdAsync(42, It.IsAny<bool>()))
				.ReturnsAsync((Department)null);

			var service = new EmailService(
				new Mock<IUserProfileService>().Object,
				new Mock<IUsersService>().Object,
				new Mock<IGeoLocationProvider>().Object,
				emailProvider.Object,
				departmentsService.Object,
				new Mock<ICallEmailProvider>().Object,
				new Mock<IEmailSender>().Object,
				new Mock<IAmazonEmailSender>().Object);

			var timestamp = new DateTime(2026, 9, 2, 14, 12, 0, DateTimeKind.Utc);
			var alert = new TroubleAlertEvent
			{
				TimeStamp = timestamp,
				Latitude = "38.9399",
				Longitude = "-119.9772"
			};
			var unit = new Unit { DepartmentId = 42, Name = "Engine 1" };
			var profile = new UserProfile
			{
				SendEmail = true,
				User = new IdentityUser { Email = "member@example.com" }
			};

			// Act
			await service.SendTroubleAlert(alert, unit, null, "100 Main St", "101 Main St",
				"Alex Smith, Jamie Lee", profile);

			// Assert
			emailProvider.Verify(provider => provider.SendTroubleAlertMail(
				"member@example.com",
				"Engine 1",
				"38.9399,-119.9772",
				"Alex Smith, Jamie Lee",
				"100 Main St",
				"101 Main St",
				timestamp.ToString("G") + " UTC",
				"No Active Call"), Times.Once);
		}
	}
}
