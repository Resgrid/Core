using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class CommunicationEvidenceTests
	{
		[TestCase(false, 1)] [TestCase(true, 0)]
		public async Task Uses_owning_channel_selection_without_claiming_delivery(bool push, int expected)
		{
			var store = new Mock<INotificationImpactStore>(); var authorization = new Mock<IAuthorizationService>();
			store.Setup(s => s.ReadNotificationMembersAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
				new NotificationMemberEvidence { DepartmentId = 7, UserId = "member", ProfileId = 1, Sms = false, Email = false, Push = push } });
			authorization.Setup(a => a.CanUserViewPersonAsync("admin", "member", 7)).ReturnsAsync(true);
			var source = new CommunicationEvidenceSource(store.Object, authorization.Object);
			var evidence = await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None);
			Assert.That(evidence.Single(e => e.Id == "notificationMembersWithoutChannel").Number, Is.EqualTo(expected));
		}
		[Test]
		public async Task Missing_profile_keeps_eligibility_unknown_and_hidden_members_are_not_counted()
		{
			var store = new Mock<INotificationImpactStore>(); var authorization = new Mock<IAuthorizationService>();
			store.Setup(s => s.ReadNotificationMembersAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
				new NotificationMemberEvidence { DepartmentId = 7, UserId = "member" } });
			authorization.Setup(a => a.CanUserViewPersonAsync("admin", "member", 7)).ReturnsAsync(true);
			var source = new CommunicationEvidenceSource(store.Object, authorization.Object);
			var evidence = await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None);
			Assert.That(evidence.Single(e => e.Id == "notificationMembersWithoutChannel").State, Is.EqualTo(EvidenceState.Unknown));
			authorization.Setup(a => a.CanUserViewPersonAsync("admin", "member", 7)).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None));
		}
	}
}
