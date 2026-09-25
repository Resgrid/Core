using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Search;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class CatalogTests
	{
		private static readonly ConfigurationCatalog Catalog = new();
		[Test]
		public void Every_department_setting_has_an_inventory_entry()
		{
			Assert.That(Catalog.Settings.Where(s => s.Binding.StartsWith("DepartmentSettingTypes.")).Select(s => s.Binding.Substring("DepartmentSettingTypes.".Length)), Is.EquivalentTo(Enum.GetNames<DepartmentSettingTypes>()));
			Assert.That(Catalog.Rules.Count, Is.GreaterThanOrEqualTo(25));
			Assert.That(Catalog.Packs.Select(p => p.Id), Is.EquivalentTo(DepartmentOperatingProfile.ArchetypeCodes));
		}
		[Test]
		public void Every_permission_has_a_reviewed_entry_or_an_owned_unexpired_inventory_gap()
		{
			var permissions = Catalog.Settings.Where(s => s.Binding.StartsWith("PermissionTypes.")).ToArray();
			Assert.That(permissions.Select(s => s.Binding.Substring("PermissionTypes.".Length)), Is.EquivalentTo(Enum.GetNames<PermissionTypes>()));
			foreach (var entry in permissions.Where(s => s.Availability == "review-required"))
			{
				Assert.That(entry.Owner, Is.Not.Null.And.Not.Empty, entry.Id);
				Assert.That(entry.ReviewGap, Is.Not.Null.And.Not.Empty, entry.Id);
				Assert.That(entry.ReviewGapExpiresOn, Is.Not.Null, entry.Id);
				Assert.That(entry.ReviewGapExpiresOn.Value.Date, Is.GreaterThanOrEqualTo(DateTime.UtcNow.Date), "Expired permission consumer-review gap: " + entry.Id);
			}
		}

		[Test]
		public void Public_search_actions_addons_and_explicit_requirement_ids_have_catalog_coverage()
		{
			var ids = Catalog.Capabilities.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
			foreach (var action in Resgrid.Services.Search.SystemActionCatalog.All)
				Assert.That(ids.Contains(action.Key), Is.True, action.Key);
			var addons = Catalog.Capabilities.Where(c => c.Id.StartsWith("addon-")).SelectMany(c => c.Requirements).Where(r => r.Kind == "addon").Select(r => r.Id).ToHashSet();
			foreach (var addon in Enum.GetNames<PlanAddonTypes>()) Assert.That(addons.Contains(addon), Is.True, addon);
			foreach (var requirement in Catalog.Capabilities.SelectMany(c => c.Requirements).Where(r => r.Kind == "permission"))
				Assert.That(Enum.TryParse<PermissionTypes>(requirement.Id, out var permission) && Enum.IsDefined(permission), Is.True, requirement.Id);
		}
		[Test]
		public void Serialized_configuration_fields_have_independent_catalog_entries_including_nested_list_elements()
		{
			foreach (var type in new[] { typeof(DepartmentModuleSettings), typeof(DepartmentOperatingProfile), typeof(PersonnelListStatusOrderSetting),
				typeof(PersonnelListStatusOrder), typeof(DepartmentSuppressStaffingInfo), typeof(UnitTypeCallStatusOverrideSetting), typeof(UnitTypeCallStatusOverride),
				typeof(UnitStatusThresholds), typeof(UnitStatusThreshold), typeof(NewCallFieldPolicy), typeof(NewCallFieldRule), typeof(GroupDispatchScopeConfig),
				typeof(DispatchRecommendationConfig), typeof(RecordsNumberingConfig), typeof(RecordsSearchConfig), typeof(RecordsRetentionPolicy),
				typeof(RecordsRetentionOverride), typeof(RecordsRetentionPolicyVersion), typeof(RecordsDisclosureConfig) })
			{
				var fields = type.GetProperties().Where(p => p.GetCustomAttribute<ProtoBuf.ProtoMemberAttribute>() != null).Select(p => type.Name + "." + p.Name);
				Assert.That(Catalog.Settings.Where(s => s.Binding.StartsWith(type.Name + ".")).Select(s => s.Binding), Is.EquivalentTo(fields));
			}
		}
		[Test]
		public void Dedicated_configuration_tables_have_field_inventory_without_secret_values()
		{
			var metadata = new[] { "DepartmentSecurityPolicyId", "DepartmentSsoConfigId", "DepartmentCallEmailId", "WeatherAlertZoneId", "Id", "DepartmentNotificationId", "DepartmentId", "CreatedOn", "UpdatedOn", "CreatedAt", "UpdatedAt", "CreatedByUserId", "UpdatedByUserId", "ReferringDepartmentId", "AffiliateCode" }.ToHashSet();
			foreach (var type in new[] { typeof(Department), typeof(DepartmentSecurityPolicy), typeof(DepartmentSsoConfig), typeof(DepartmentCallEmail), typeof(WeatherAlertZone), typeof(ChatbotDepartmentConfig), typeof(DepartmentNotification) })
			{
				var fields = type.GetProperties().Where(p => p.CanWrite && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null &&
					!metadata.Contains(p.Name) && (p.PropertyType.IsValueType || p.PropertyType == typeof(string))).Select(p => type.Name + "." + p.Name);
				Assert.That(Catalog.Settings.Where(s => s.Id.StartsWith("table.") && s.Binding.StartsWith(type.Name + ".")).Select(s => s.Binding), Is.EquivalentTo(fields), type.Name);
			}
			foreach (var field in new[] { "Department.ApiKey", "Department.SharedSecret", "DepartmentSsoConfig.EncryptedClientSecret", "DepartmentSsoConfig.EncryptedSigningCertificate", "DepartmentSsoConfig.EncryptedScimBearerToken", "DepartmentCallEmail.Password", "ChatbotDepartmentConfig.LlmApiKey" })
				Assert.That(Catalog.Settings.Single(s => s.Binding == field).Secret, Is.True, field);
		}

		[Test]
		public void Source_destinations_are_existing_GET_actions()
		{
			var assembly = typeof(Resgrid.Web.Areas.User.Controllers.DepartmentController).Assembly;
			foreach (var location in Catalog.Settings.Select(s => s.Location).Concat(Catalog.Capabilities.Select(c => c.Location)).Concat(Catalog.Rules.Select(r => r.Location)).Distinct())
			{
				var type = assembly.GetType("Resgrid.Web.Areas.User.Controllers." + location.Controller + "Controller");
				Assert.That(type, Is.Not.Null, location.Url);
				Assert.That(type.GetMethods().Any(m => m.Name == location.Action && m.GetCustomAttribute<HttpPostAttribute>() == null && m.GetCustomAttribute<NonActionAttribute>() == null), Is.True, location.Url);
			}
		}
		[TestCase("ar")][TestCase("de")][TestCase("el")][TestCase("es")][TestCase("fr")][TestCase("it")][TestCase("pl")][TestCase("sv")][TestCase("uk")]
		public void Core_navigation_and_guidance_boundary_have_real_locale_resources(string locale)
		{
			var resources = new ResourceManager(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
			var culture = CultureInfo.GetCultureInfo(locale);
			foreach (var name in new[] { "overview", "wizard", "report", "explore", "health", "worklist", "reference", "history", "ReportBoundary", "Unknown", "Redacted" })
			{
				var localized = resources.GetString("Ui." + name, culture);
				Assert.That(localized, Is.Not.Null.And.Not.Empty, locale + ": " + name);
				Assert.That(localized, Is.Not.EqualTo(resources.GetString("Ui." + name, CultureInfo.GetCultureInfo("en"))), locale + ": " + name);
			}
			Assert.That(culture.TextInfo.IsRightToLeft, Is.EqualTo(locale == "ar"));
		}

		[Test]
		public void English_catalog_resource_references_resolve_without_key_fallback()
		{
			var resources = new ResourceManager(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
			var objects = Catalog.Settings.Cast<object>().Concat(Catalog.Capabilities).Concat(Catalog.Areas).Concat(Catalog.Packs).Concat(Catalog.Rules).Concat(Catalog.Settings.Select(s => s.Impact));
			foreach (var item in objects)
				foreach (var property in item.GetType().GetProperties().Where(p => p.Name.EndsWith("Key") && p.PropertyType == typeof(string)))
				{
					var key = (string)property.GetValue(item);
					Assert.That(resources.GetString(key, CultureInfo.GetCultureInfo("en")), Is.Not.Null.And.Not.Empty, key);
				}
		}
		[Test]
		public void Reference_search_is_literal_bounded_and_cites_the_release_pack()
		{
			using var search = new AdminAssistReferenceSearch(Catalog);
			var results = search.Search("Mapbox token", "en", 100);
			Assert.That(results, Is.Not.Empty); Assert.That(results.Count, Is.LessThanOrEqualTo(25));
			Assert.That(results.All(r => r.PackVersion == Catalog.Version && r.SourcePath.StartsWith("docs/admin-assist/") && !string.IsNullOrEmpty(r.Anchor)), Is.True);
			Assert.DoesNotThrow(() => search.Search("( token : * ) OR", "en"));
			Assert.Throws<ArgumentException>(() => search.Search(new string('x', 257), "en"));
		}
		[Test]
		public void Embedded_documentation_anchors_resolve_and_onboarding_is_searchable()
		{
			var root = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			Assert.That(root, Is.Not.Null);
			foreach (var article in Catalog.Articles)
			{
				var path = System.IO.Path.Combine(root.FullName, article.SourcePath);
				Assert.That(System.IO.File.ReadAllText(path), Does.Contain("<a id=\"" + article.Anchor + "\"></a>"), article.Id);
			}
			using var search = new AdminAssistReferenceSearch(Catalog);
			Assert.That(search.Search("resume setup", "en").Any(hit => hit.Id.StartsWith("guide.")), Is.True);
		}
	}
}
