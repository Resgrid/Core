using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-1B launch templates and RMS-1C operational packs: every rendering validates, overlays apply, provenance is stated, Preview is labeled.</summary>
	[TestFixture]
	public class RecordTemplateCatalogTests
	{
		private RmsDefinitionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsDefinitionHarness();

		[Test]
		public async Task Every_template_validates_under_every_supported_profile_and_locale()
		{
			var catalog = await _h.Templates.GetCatalogAsync();
			catalog.Select(p => p.PackKey).Should().BeEquivalentTo(new[] { "template.launch", "pack.cert", "pack.sar", "pack.disaster-assessment", "pack.eoc", "pack.hazmat", "pack.industrial", "pack.exercise", "pack.mutual-aid", "pack.incident-support" });
			catalog.Single(p => p.PackKey == "template.launch").Definitions.Select(d => d.Key).Should().BeEquivalentTo(new[]
			{
				"template.security-patrol", "template.security-incident", "template.delivery-run", "template.bus-route-eod", "template.shift-summary", "template.job-completion"
			});
			foreach (var pack in catalog)
			{
				if (pack.PackKey != "template.launch") pack.Sources.Should().NotBeEmpty($"{pack.PackKey} declares its sources");
				pack.ReviewedOn.Should().NotBeNull();
				foreach (var template in pack.Definitions)
					foreach (var profile in pack.SupportedProfiles)
						foreach (var locale in pack.SupportedLocales)
						{
							var rendering = await _h.Templates.RenderAsync(template.Key, profile, locale);
							rendering.Should().NotBeNull();
							var draft = new RecordDefinitionDraftInput
							{
								Name = rendering.Template.Name, Schema = rendering.Schema, LifecyclePreset = rendering.Template.LifecyclePreset,
								Numbering = new RecordDefinitionNumbering { Prefix = rendering.Template.NumberPrefix }, PermittedSubjectTypes = rendering.Template.PermittedSubjectTypes,
								Classification = rendering.Template.Classification, RetentionYears = rendering.Template.RetentionYears, ClientSurface = rendering.Template.ClientSurface
							};
							var validation = await _h.Definitions.ValidateAsync(Dept, draft);
							validation.Issues.Where(i => i.Severity == "error").Should().BeEmpty($"{template.Key} under {profile}/{locale}: {string.Join("; ", validation.Issues.Where(i => i.Severity == "error").Select(i => i.Path + " " + i.Code + " " + i.Message))}");
							rendering.ProvenanceStatement.Should().NotBeNullOrWhiteSpace();
							rendering.Schema.AllFields().Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Label));
						}
			}
		}

		[Test]
		public async Task Overlays_relabel_convert_units_and_set_currency_without_touching_the_base_template()
		{
			var generic = await _h.Templates.RenderAsync("template.security-patrol", "generic", null);
			var ca = await _h.Templates.RenderAsync("template.security-patrol", "ca", "fr-CA");
			ca.Schema.FindField("officer").Label.Should().Be("Agent");
			generic.Schema.FindField("officer").Label.Should().Be("Officer");
			RecordTemplateCatalog.Find("template.security-patrol").Schema.FindField("officer").Label.Should().Be("Officer", "rendering deep-copies the schema");
			ca.Locale.Should().Be("fr-CA"); ca.MeasurementSystem.Should().Be("metric"); ca.CurrencyCode.Should().Be("CAD");
			ca.ArtifactStatus.Should().Be(RmsArtifactStatus.Compatible);
			generic.ArtifactStatus.Should().Be(RmsArtifactStatus.DepartmentLocal);
			generic.ProvenanceStatement.Should().StartWith("Department-local template");

			var usDebrief = await _h.Templates.RenderAsync("pack.sar.segment-debrief", "us", null);
			var caDebrief = await _h.Templates.RenderAsync("pack.sar.segment-debrief", "ca", null);
			usDebrief.Schema.FindField("track_spacing").DefaultUnit.Should().Be("ft");
			caDebrief.Schema.FindField("track_spacing").DefaultUnit.Should().Be("m");
			usDebrief.ProvenanceStatement.Should().StartWith("Compatible with");
			usDebrief.Sources.Should().NotBeEmpty();

			var usDelivery = await _h.Templates.RenderAsync("template.delivery-run", "us", null);
			var caDelivery = await _h.Templates.RenderAsync("template.delivery-run", "ca", null);
			usDelivery.Schema.FindField("mileage").FixedUnitLabel.Should().Be("mi");
			caDelivery.Schema.FindField("mileage").FixedUnitLabel.Should().Be("km");
		}

		[Test]
		public async Task Preview_packs_are_labeled_and_locked_classification_floors_apply()
		{
			var catalog = await _h.Templates.GetCatalogAsync();
			catalog.Where(p => p.IsPreview).Select(p => p.PackKey).Should().BeEquivalentTo(new[] { "pack.cert", "pack.mutual-aid", "pack.incident-support" });
			catalog.Single(p => p.PackKey == "pack.hazmat").IsPreview.Should().BeFalse();
			var incident = await _h.Templates.RenderAsync("template.security-incident", "generic", null);
			incident.Schema.FindField("name").Classification.Should().Be(RmsFieldClassification.Restricted, "involved-person names carry the pack's restricted floor");
			incident.Schema.FindField("contact").Classification.Should().Be(RmsFieldClassification.Restricted);
			var deployment = await _h.Templates.RenderAsync("pack.mutual-aid.deployment", "us-ca", null);
			deployment.Schema.FindSection("roster").Repeating.Should().BeTrue();
			deployment.Schema.FindField("profile").Options.Select(o => o.Key).Should().Contain("us-ca-cross-border");
			RecordsClientCapabilities.Derive(deployment.Schema).Should().Be(RecordsClientCapabilities.Packs);
		}

		[Test]
		public async Task Pack_protected_data_policies_set_classification_floors_in_every_rendering()
		{
			var mission = await _h.Templates.RenderAsync("pack.sar.mission-summary", "generic", null);
			mission.Policies.Should().NotBeEmpty();
			mission.Policies.Select(p => p.Category).Should().Contain("subject-clue-recovery").And.Contain("treatment-casualty");
			mission.Schema.FindField("subject_name").Classification.Should().Be(RmsFieldClassification.Restricted);
			mission.Schema.FindField("medical_concerns").Classification.Should().Be(RmsFieldClassification.Protected, "medical concerns are health information");
			mission.Schema.FindField("mission_number").Classification.Should().Be(RmsFieldClassification.Standard, "policies touch only the fields they name");

			var release = await _h.Templates.RenderAsync("pack.hazmat.release-response", "generic", null);
			release.Schema.FindField("exposure_details").Classification.Should().Be(RmsFieldClassification.Protected);
			release.Schema.FindField("entrant").Classification.Should().Be(RmsFieldClassification.Restricted, "entrants are identifiable persons");
			release.Schema.FindField("persons_deconned").Classification.Should().Be(RmsFieldClassification.Standard, "counts stay Workflow-exposed");

			var deployment = await _h.Templates.RenderAsync("pack.mutual-aid.deployment", "generic", null);
			deployment.Schema.FindField("travel_instructions").Classification.Should().Be(RmsFieldClassification.Restricted);
			deployment.Schema.FindField("receipts").Classification.Should().Be(RmsFieldClassification.Restricted);

			foreach (var pack in await _h.Templates.GetCatalogAsync())
				foreach (var template in pack.Definitions)
				{
					var definition = _h.Templates.GetTemplate(template.Key);
					foreach (var policy in definition.ProtectedDataPolicies)
					{
						policy.FieldKeys.Should().NotBeEmpty();
						policy.Rationale.Should().NotBeNullOrWhiteSpace();
						foreach (var key in policy.FieldKeys)
							definition.Schema.FindField(key).Should().NotBeNull($"policy '{policy.Category}' of {template.Key} names '{key}'");
					}
				}
		}

		[Test]
		public async Task Unsupported_profiles_and_unknown_templates_fail_closed()
		{
			(await _h.Templates.RenderAsync("template.missing", "generic", null)).Should().BeNull();
			Func<Task> bad = () => _h.Templates.RenderAsync("template.security-patrol", "mars", null);
			await bad.Should().ThrowAsync<ArgumentException>();
			Func<Task> unsupported = () => _h.Templates.RenderAsync("template.security-patrol", "us-ca", null);
			await unsupported.Should().ThrowAsync<ArgumentException>().WithMessage("*does not support profile*");
		}

		[Test]
		public async Task Catalog_mirrors_to_product_scope_rows_once()
		{
			var written = await _h.Templates.EnsureCatalogAsync();
			written.Should().BeGreaterThan(0);
			_h.Defs.Packs.Should().HaveCount(RecordTemplateCatalog.Packs.Count).And.OnlyContain(p => p.DepartmentId == RmsTemplatePackVersion.ProductDepartmentId && p.ContentChecksum != null);
			_h.Defs.Profiles.Select(p => p.ProfileKey).Should().BeEquivalentTo(new[] { "generic", "us", "ca", "us-ca", "us-nwcg", "us-nims", "us-calif" });
			(await _h.Templates.EnsureCatalogAsync()).Should().Be(0, "a second call is a no-op within the process");
			var profiles = await _h.Templates.GetProfilesAsync();
			profiles.Single(p => p.ProfileKey == "us").CurrencyCode.Should().Be("USD");
			profiles.Single(p => p.ProfileKey == "ca").MeasurementSystem.Should().Be("metric");
			profiles.Single(p => p.ProfileKey == "ca").SupportedLocales.Should().Contain("fr-CA");
		}
	}
}
