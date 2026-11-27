using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Web.ServicesCore.Helpers;
using Labels = Resgrid.Localization.Areas.User.AdminAssist.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>
	/// An add-on built dark (every feature planned, e.g. the LMS before wave 3) stays in the catalog for inventory and coverage,
	/// but setup, Explore, the printed report and Ask neither list nor offer it, and it needs no key feature until one is released.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class UnreleasedAddonVisibilityTests
	{
		private static readonly ConfigurationCatalog Catalog = new();

		[Test]
		public void The_LMS_add_on_is_inventoried_but_not_listed_until_a_feature_is_released()
		{
			var lms = Catalog.Areas.Single(a => a.Addon == nameof(PlanAddonTypes.Lms));
			var features = Catalog.Capabilities.Where(c => c.AreaId == lms.Id).ToArray();

			Assert.That(lms.Tier, Is.EqualTo(ProductArea.AddOn));
			Assert.That(features.Select(c => c.Id), Does.Contain("addon-lms"));
			Assert.That(features.All(c => c.ReleaseStatus == "planned" && !c.IsKey), Is.True, "Nothing about the LMS is offered or taught before wave 3.");
			Assert.That(features.All(c => c.Requirements.Any(r => r.Kind == "flag" && r.Id == FeatureFlagKeys.TrainingLms)), Is.True);
			Assert.That(Catalog.IsListed(lms), Is.False);
			Assert.That(Catalog.ListedAreas().Select(a => a.Id), Does.Not.Contain(lms.Id));
			Assert.That(Catalog.ListedCapabilities().Select(c => c.AreaId), Does.Not.Contain(lms.Id));
			// Every other module, released add-ons included, is still listed (Workforce Pro is dark too until the owner releases it).
			var workforcePro = Catalog.Areas.Single(a => a.Addon == nameof(PlanAddonTypes.WorkforcePro));
			Assert.That(Catalog.ListedAreas().Select(a => a.Id), Is.EquivalentTo(Catalog.Areas.Where(a => a.Id != lms.Id && a.Id != workforcePro.Id).Select(a => a.Id)));
		}

		[Test]
		public void The_Workforce_Pro_add_on_holds_member_wellness_and_is_inventoried_but_not_listed_until_released()
		{
			var area = Catalog.Areas.Single(a => a.Addon == nameof(PlanAddonTypes.WorkforcePro));
			var features = Catalog.Capabilities.Where(c => c.AreaId == area.Id).ToArray();

			Assert.That(area.Tier, Is.EqualTo(ProductArea.AddOn));
			Assert.That(features.Select(c => c.Id), Is.SupersetOf(new[] { "addon-workforce-pro", "workforce-pro-seats", "member-wellness" }));
			Assert.That(features.All(c => c.ReleaseStatus == "planned" && !c.IsKey), Is.True, "Nothing about Workforce Pro is offered or taught before it is released.");
			Assert.That(features.All(c => c.Requirements.Any(r => r.Kind == "flag" && r.Id == FeatureFlagKeys.WorkforcePro) &&
				c.Requirements.Any(r => r.Kind == "addon" && r.Id == nameof(PlanAddonTypes.WorkforcePro))), Is.True);
			// The wellness settings are Workforce Pro settings now, not Personnel ones.
			Assert.That(Catalog.Settings.Where(s => s.Binding.StartsWith("WellnessSettings.") || s.Id == "setting.WellnessSettings").Select(s => s.AreaId).Distinct(), Is.EqualTo(new[] { area.Id }));
			Assert.That(Catalog.IsListed(area), Is.False);
			Assert.That(Catalog.ListedCapabilities().Select(c => c.Id), Does.Not.Contain("member-wellness"));
		}

		[Test]
		public void An_add_on_is_listed_once_one_feature_is_available_or_in_preview()
		{
			ProductCapability Feature(string id, string area, string status) => new(id, area, "L", "P", "V", "E", "A", status, new CatalogLocation("Home", "Index"),
				Array.Empty<CapabilityRequirement>(), Array.Empty<string>(), Array.Empty<string>());
			var catalog = new Mock<IAdminAssistCatalog>();
			catalog.SetupGet(c => c.Areas).Returns(new[]
			{
				new ProductArea("dark", "L", "P", 1, Array.Empty<string>(), ProductArea.AddOn, "Lms"),
				new ProductArea("preview", "L", "P", 2, Array.Empty<string>(), ProductArea.AddOn, "PTT"),
				new ProductArea("core", "L", "P", 3, Array.Empty<string>(), ProductArea.Core)
			});
			catalog.SetupGet(c => c.Capabilities).Returns(new[]
			{
				Feature("dark-1", "dark", "planned"), Feature("dark-2", "dark", "retired"),
				Feature("preview-1", "preview", "planned"), Feature("preview-2", "preview", "preview"),
				Feature("core-1", "core", "planned")
			});

			Assert.That(catalog.Object.ListedAreas().Select(a => a.Id), Is.EqualTo(new[] { "preview", "core" }));
			Assert.That(catalog.Object.ListedCapabilities().Select(c => c.Id), Is.EqualTo(new[] { "preview-1", "preview-2", "core-1" }));
		}

		[Test]
		public async Task The_setup_catalog_endpoint_does_not_list_or_offer_an_unreleased_add_on()
		{
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{ new Claim(ClaimTypes.PrimarySid, "admin"), new Claim(ClaimTypes.PrimaryGroupSid, "7") }, "Test")) };
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			try
			{
				var access = new Mock<IAdminAssistAccessService>();
				access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				var labels = new Mock<IStringLocalizer<Labels>>();
				labels.Setup(l => l.GetAllStrings(It.IsAny<bool>())).Returns(Array.Empty<LocalizedString>());
				var controller = new Resgrid.Web.Services.Controllers.v4.AdminAssistController(Mock.Of<IAdminAssistService>(), access.Object, Catalog, labels.Object,
					Mock.Of<IAdminAssistReferenceSearch>(), Mock.Of<IAdminAssistAskService>(), Mock.Of<IAdminAssistDiagnostics>(), Mock.Of<IAdminAssistPlans>(),
					Mock.Of<IAdminAssistPlanQueries>(), Mock.Of<IAdminAssistWorklistService>(), Mock.Of<IAdminAssistMaintenanceStore>(), Mock.Of<IConfigurationImpactService>(),
					Mock.Of<IDispatchImpactService>(), Mock.Of<IPermissionImpactService>(), Mock.Of<IModuleImpactService>(), Mock.Of<ITextImportImpactService>(),
					Mock.Of<IRetentionImpactService>(), Mock.Of<INotificationImpactService>(), Mock.Of<ISecurityImpactService>())
				{
					ControllerContext = new ControllerContext { HttpContext = http }
				};

				var result = await controller.Catalog(true, CancellationToken.None);

				Assert.That(result, Is.InstanceOf<ContentResult>());
				using var json = JsonDocument.Parse(((ContentResult)result).Content);
				var areas = json.RootElement.GetProperty("areas").EnumerateArray().Select(a => a.GetProperty("id").GetString()).ToArray();
				var features = json.RootElement.GetProperty("capabilities").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToArray();
				Assert.That(areas, Does.Not.Contain("lms").And.Not.Contain("workforce-pro").And.Contain("training").And.Contain("ai"));
				Assert.That(features, Does.Not.Contain("addon-lms").And.Not.Contain("member-wellness").And.Contain("addon-ai").And.Contain("training-transcript"));
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}
