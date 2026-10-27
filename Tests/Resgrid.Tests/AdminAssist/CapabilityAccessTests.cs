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

		[Test]
		public async Task Definitely_unmet_requirement_is_not_hidden_by_unknown_subscription_status()
		{
			// Maintenance off by flag while add-on status cannot be read (no Billing API): the capability is known to be
			// unavailable, so its Critical safety-hold check becomes not applicable instead of a permanent unknown.
			var actor = new AdminAssistActor(7, "admin");
			var membership = new Mock<IRecordsAuthorizationService>();
			membership.Setup(m => m.IsActiveMemberAsync(actor.UserId, actor.DepartmentId)).ReturnsAsync(true);
			membership.Setup(m => m.IsDepartmentAdminAsync(actor.UserId, actor.DepartmentId)).ReturnsAsync(true);
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.EvaluateFreshAsync(It.IsAny<string>(), actor.DepartmentId)).ReturnsAsync(new Resgrid.Model.FeatureFlagEvaluation { IsEnabled = false });
			var catalog = new ConfigurationCatalog();
			var service = new AdminAssistAccessService(membership.Object, flags.Object, Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IReadinessAccessService>(),
				Mock.Of<IBusinessOperationsAccessService>(), catalog, TimeProvider.System, Mock.Of<IAuthorizationService>());
			var workOrders = await service.GetCapabilityAsync(actor, catalog.Capabilities.First(c => c.Location.Controller == "WorkOrders").Id);
			Assert.That(workOrders.State, Is.EqualTo(EvidenceState.Unavailable));
			Assert.That(workOrders.ReasonCodes, Does.Contain("FeatureNotEnabled"));
			Assert.That(workOrders.CanConfigure, Is.False);
			Assert.That(workOrders.CommercialState, Is.EqualTo(EvidenceState.Unknown), "Subscription uncertainty is still reported separately.");
		}

		[Test]
		public async Task Each_owning_gate_is_read_once_per_pass()
		{
			var actor = new AdminAssistActor(7, "admin");
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.EvaluateFreshAsync(It.IsAny<string>(), actor.DepartmentId)).ReturnsAsync(new Resgrid.Model.FeatureFlagEvaluation { IsEnabled = true });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(actor.DepartmentId, true)).ReturnsAsync(new Resgrid.Model.DepartmentModuleSettings());
			var readiness = new Mock<IReadinessAccessService>();
			readiness.Setup(r => r.CanUseChecklistsAsync(actor.DepartmentId)).ReturnsAsync(true);
			var service = new AdminAssistAccessService(Admin(actor).Object, flags.Object, settings.Object, Mock.Of<ISubscriptionsService>(),
				Mock.Of<IDepartmentDataProtectionService>(), readiness.Object, Mock.Of<IBusinessOperationsAccessService>(), new ConfigurationCatalog(),
				TimeProvider.System, Mock.Of<IAuthorizationService>());

			var features = await service.GetCapabilitiesAsync(actor);

			Assert.That(features.Single(f => f.CapabilityId == "checklist-reports").State, Is.EqualTo(EvidenceState.Known));
			readiness.Verify(r => r.CanUseChecklistsAsync(actor.DepartmentId), Times.Once, "Five checklist capabilities share one gate.");
		}

		[TestCase(false, false, false, "Known", "Known", "Known")]
		[TestCase(true, false, false, "ModuleDisabled", "ModuleDisabled", "ModuleDisabled")]
		[TestCase(false, true, false, "Known", "ModuleDisabled", "Known")]
		[TestCase(false, false, true, "Known", "Known", "ModuleDisabled")]
		public async Task Incident_Back_Office_switches_gate_their_desks(bool backOfficeOff, bool businessOff, bool coordinationOff,
			string activation, string finance, string boards)
		{
			// The finance and board switches only apply while the back office itself is on, so turning the master off closes every desk.
			var actor = new AdminAssistActor(7, "admin");
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.EvaluateFreshAsync(It.IsAny<string>(), actor.DepartmentId)).ReturnsAsync(new Resgrid.Model.FeatureFlagEvaluation { IsEnabled = true });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(actor.DepartmentId, true)).ReturnsAsync(new Resgrid.Model.DepartmentModuleSettings
				{ IncidentBackOfficeDisabled = backOfficeOff, IncidentBusinessDisabled = businessOff, IncidentCoordinationDisabled = coordinationOff });
			var service = new AdminAssistAccessService(Admin(actor).Object, flags.Object, settings.Object, Mock.Of<ISubscriptionsService>(),
				Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IReadinessAccessService>(), Mock.Of<IBusinessOperationsAccessService>(), new ConfigurationCatalog(),
				TimeProvider.System, Mock.Of<IAuthorizationService>());

			var features = await service.GetCapabilitiesAsync(actor);

			// The add-on itself cannot be read without the Billing API, so a desk whose switches are on is Unknown, never Unavailable.
			string Outcome(string id)
			{
				var feature = features.Single(f => f.CapabilityId == id);
				return feature.ReasonCodes.Contains("ModuleDisabled") ? "ModuleDisabled" : feature.State == EvidenceState.Unavailable ? "Unavailable" : "Known";
			}
			Assert.That(Outcome("incident-activation"), Is.EqualTo(activation));
			Assert.That(Outcome("incident-finance"), Is.EqualTo(finance));
			Assert.That(Outcome("coordination-boards"), Is.EqualTo(boards));
		}

		[Test, NonParallelizable]
		public async Task Unresponsive_billing_spends_one_bounded_budget_and_reports_unknown_entitlement()
		{
			var (url, key, budget) = (Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl, Resgrid.Config.ApiConfig.BackendInternalApikey, Resgrid.Config.AdminAssistConfig.BillingEvidenceTimeoutSeconds);
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			Resgrid.Config.ApiConfig.BackendInternalApikey = "unit-test-only";
			Resgrid.Config.AdminAssistConfig.BillingEvidenceTimeoutSeconds = 1;
			try
			{
				var actor = new AdminAssistActor(7, "admin");
				var subscriptions = new Mock<ISubscriptionsService>();
				// Every add-on lookup hangs, as when the Billing API accepts connections but never answers.
				subscriptions.Setup(s => s.GetAllAddonPlansByTypeAsync(It.IsAny<Resgrid.Model.PlanAddonTypes>(), It.IsAny<bool>()))
					.Returns(() => new TaskCompletionSource<System.Collections.Generic.List<Resgrid.Model.PlanAddon>>().Task);
				var service = new AdminAssistAccessService(Admin(actor).Object, Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentSettingsService>(),
					subscriptions.Object, Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IReadinessAccessService>(), Mock.Of<IBusinessOperationsAccessService>(),
					new ConfigurationCatalog(), TimeProvider.System, Mock.Of<IAuthorizationService>());

				var watch = System.Diagnostics.Stopwatch.StartNew();
				var features = await service.GetCapabilitiesAsync(actor);

				Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "Several add-on types must not each wait for billing.");
				var addon = features.Single(f => f.CapabilityId == "addon-readiness");
				Assert.That(addon.State, Is.EqualTo(EvidenceState.Unknown));
				Assert.That(addon.ReasonCodes, Does.Contain("SubscriptionStatusUnavailable"));
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = url;
				Resgrid.Config.ApiConfig.BackendInternalApikey = key;
				Resgrid.Config.AdminAssistConfig.BillingEvidenceTimeoutSeconds = budget;
			}
		}

		private static Mock<IRecordsAuthorizationService> Admin(AdminAssistActor actor)
		{
			var membership = new Mock<IRecordsAuthorizationService>();
			membership.Setup(m => m.IsActiveMemberAsync(actor.UserId, actor.DepartmentId)).ReturnsAsync(true);
			membership.Setup(m => m.IsDepartmentAdminAsync(actor.UserId, actor.DepartmentId)).ReturnsAsync(true);
			return membership;
		}
	}
}
