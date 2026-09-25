using System;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class CapabilityAccessTests
	{
		[TestCase(true)] [TestCase(false)]
		public async Task Addon_education_keeps_billing_handoff_separate_from_entitlement(bool managingMember)
		{
			var actor = new AdminAssistActor(7, "admin");
			var membership = new Mock<IRecordsAuthorizationService>();
			membership.Setup(m => m.IsActiveMemberAsync(actor.UserId, actor.DepartmentId)).ReturnsAsync(true);
			membership.Setup(m => m.IsDepartmentAdminAsync(actor.UserId, actor.DepartmentId)).ReturnsAsync(true);
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserManageSubscriptionAsync(actor.UserId, actor.DepartmentId)).ReturnsAsync(managingMember);
			var service = new AdminAssistAccessService(membership.Object, Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IReadinessAccessService>(),
				Mock.Of<IBusinessOperationsAccessService>(), new ConfigurationCatalog(), TimeProvider.System, authorization.Object);
			var features = await service.GetCapabilitiesAsync(actor);
			var addon = features.Single(f => f.CapabilityId == "addon-readiness");
			Assert.That(addon.CanConfigure, Is.False);
			Assert.That(addon.SubscriptionDestination, Is.EqualTo(managingMember ? "/User/Subscription/Index" : null));
			Assert.That(features.Single(f => f.CapabilityId == "addon-ai").SubscriptionDestination, Is.Null);
		}
	}
}
