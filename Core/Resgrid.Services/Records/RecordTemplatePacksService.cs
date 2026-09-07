using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Product template packs and jurisdiction profiles (RMS plan section 4.1; RMS-1B launch templates, RMS-1C
	/// packs). The content is <see cref="RecordTemplateCatalog"/>; this service renders a template for a profile and
	/// locale (labels, units, currency, provenance statement), mirrors the catalog into the product-scope tables,
	/// and diffs a department clone against the current product template for the deliberate-incorporate flow.
	/// </summary>
	public class RecordTemplatePacksService : IRecordTemplatePacksService
	{
		private readonly IRmsTemplatePackVersionsRepository _packs;
		private readonly IRmsJurisdictionProfileVersionsRepository _profiles;
		private static int _catalogEnsured;

		public RecordTemplatePacksService(IRmsTemplatePackVersionsRepository packs, IRmsJurisdictionProfileVersionsRepository profiles)
		{
			_packs = packs;
			_profiles = profiles;
		}

		public async Task<List<RecordTemplatePackSummary>> GetCatalogAsync()
		{
			await EnsureCatalogAsync();
			return RecordTemplateCatalog.Packs.Select(p => new RecordTemplatePackSummary
			{
				PackKey = p.Key, Version = p.Version, Name = p.Name, Category = p.Category, Description = p.Description, IsPreview = p.IsPreview, ArtifactStatus = p.ArtifactStatus.ToString(),
				SupportedProfiles = p.SupportedProfiles.ToList(), SupportedLocales = p.SupportedLocales.ToList(), ReviewedOn = p.ReviewedOn, Sources = p.Sources.ToList(),
				Definitions = p.Definitions.Select(d => new RecordTemplateSummary
				{
					Key = d.Key, Name = d.Name, Category = d.Category, Description = d.Description, LifecyclePreset = d.LifecyclePreset.ToString(),
					SectionCount = d.Schema.Sections.Count, FieldCount = d.Schema.AllFields().Count(), MinimumClientCapability = RecordsClientCapabilities.Derive(d.Schema),
					ArtifactStatus = p.ArtifactStatus.ToString(), IsPreview = p.IsPreview
				}).ToList()
			}).ToList();
		}

		public async Task<List<RmsJurisdictionProfileVersion>> GetProfilesAsync()
		{
			await EnsureCatalogAsync();
			return RecordTemplateCatalog.Profiles.ToList();
		}

		public Task<RmsJurisdictionProfileVersion> GetProfileAsync(string profileKey) => Task.FromResult(RecordTemplateCatalog.FindProfile(string.IsNullOrWhiteSpace(profileKey) ? "generic" : profileKey));

		public RecordTemplateDefinition GetTemplate(string templateKey) => RecordTemplateCatalog.Find(templateKey);

		public Task<RecordTemplateRendering> RenderAsync(string templateKey, string profileKey, string locale)
		{
			var template = RecordTemplateCatalog.Find(templateKey);
			if (template == null) return Task.FromResult<RecordTemplateRendering>(null);
			profileKey = string.IsNullOrWhiteSpace(profileKey) ? "generic" : profileKey.Trim().ToLowerInvariant();
			var profile = RecordTemplateCatalog.FindProfile(profileKey) ?? throw new ArgumentException($"'{profileKey}' is not a jurisdiction profile.", nameof(profileKey));
			var pack = RecordTemplateCatalog.PackOf(template.Key);
			if (pack != null && !pack.SupportedProfiles.Contains(profileKey, StringComparer.OrdinalIgnoreCase))
				throw new ArgumentException($"Pack '{pack.Key}' does not support profile '{profileKey}'.", nameof(profileKey));
			locale = string.IsNullOrWhiteSpace(locale) ? profile.DefaultLocale : locale.Trim();
			return Task.FromResult(Render(template, profile, profileKey, locale));
		}

		/// <summary>Applies the overlay: a deep copy of the base schema with profile labels, units and currency; the base is never mutated.</summary>
		public static RecordTemplateRendering Render(RecordTemplateDefinition template, RmsJurisdictionProfileVersion profile, string profileKey, string locale)
		{
			var schema = JsonConvert.DeserializeObject<RecordDefinitionSchema>(RecordDefinitionSchema.Serialize(template.Schema));
			template.Overlays.TryGetValue(profileKey, out var overlay);
			var terminology = profile.Terminology.TryGetValue(locale, out var terms) ? terms : new Dictionary<string, string>();
			var labels = overlay != null && overlay.Labels.TryGetValue(locale, out var l) ? l : new Dictionary<string, string>();

			foreach (var section in schema.Sections)
			{
				if (labels.TryGetValue(section.Key, out var sectionLabel)) section.Label = sectionLabel;
				foreach (var field in section.Fields)
				{
					if (labels.TryGetValue(field.Key, out var fieldLabel)) field.Label = fieldLabel;
					else if (terminology.TryGetValue(field.Key, out var term)) field.Label = term;
					if (field.Type == RmsFieldType.Quantity)
					{
						if (overlay != null && overlay.DefaultUnits.TryGetValue(field.Key, out var unit) && RmsUnits.Find(unit)?.Family == field.UnitFamily) field.DefaultUnit = unit;
						else field.DefaultUnit = RmsUnits.PreferredUnit(field.UnitFamily, profile.MeasurementSystem) ?? field.DefaultUnit;
					}
					if (field.Type == RmsFieldType.Decimal && !string.IsNullOrWhiteSpace(field.FixedUnitLabel))
					{
						// Delivery Run's mileage ships as a decimal with a fixed unit label (plan 4.1); the label follows the profile.
						if (field.FixedUnitLabel == "mi" && string.Equals(profile.MeasurementSystem, "metric", StringComparison.OrdinalIgnoreCase)) field.FixedUnitLabel = "km";
						if (field.FixedUnitLabel == "km" && string.Equals(profile.MeasurementSystem, "customary", StringComparison.OrdinalIgnoreCase)) field.FixedUnitLabel = "mi";
					}
					if (field.Type == RmsFieldType.Currency)
						field.DefaultCurrency = overlay?.CurrencyCode ?? profile.CurrencyCode ?? field.DefaultCurrency ?? "USD";
					foreach (var option in field.Options)
						if (option.Labels != null && option.Labels.TryGetValue(locale, out var optionLabel)) option.Label = optionLabel;
					// Pack-locked classification and the pack's protected-data policies can only tighten in a clone; the
					// rendering carries the floor, and a department definition may raise it but never lower it.
					var floor = template.FloorFor(field.Key);
					if (floor.HasValue && field.Classification < floor.Value)
					{
						// The floor also fixes the safe projection: a non-Standard value is never indexed, grouped,
						// summed or handed to Workflow (plan 4.1), whatever the base field declared.
						field.Classification = floor.Value;
						field.Searchable = false;
						field.Groupable = false;
						field.Aggregatable = false;
						field.WorkflowExposed = false;
					}
				}
			}

			var status = overlay?.ArtifactStatus ?? (profileKey == "generic" ? RmsArtifactStatus.DepartmentLocal : RmsArtifactStatus.Compatible);
			var sources = overlay?.Sources?.Count > 0 ? overlay.Sources : RecordTemplateCatalog.PackOf(template.Key)?.Sources ?? new List<RmsSourceProvenance>();
			var pack = RecordTemplateCatalog.PackOf(template.Key);
			var statement = status == RmsArtifactStatus.Exact
				? $"Exact form output per {string.Join(", ", sources.Select(s => s.Identifier))}."
				: status == RmsArtifactStatus.Compatible
					? $"Compatible with {string.Join(", ", sources.Select(s => s.Identifier).DefaultIfEmpty("the generic template"))} ({profile.Name}, reviewed {(sources.FirstOrDefault()?.ReviewedOn ?? pack?.ReviewedOn)?.ToString("yyyy-MM-dd") ?? "n/a"}); not an exact named form." + (pack?.IsPreview == true ? " Preview: no claim of agency acceptance." : string.Empty)
					: "Department-local template; carries no jurisdiction or agency provenance.";
			return new RecordTemplateRendering
			{
				Template = template, ProfileKey = profileKey, Locale = locale, MeasurementSystem = profile.MeasurementSystem, CurrencyCode = overlay?.CurrencyCode ?? profile.CurrencyCode,
				Schema = schema, ArtifactStatus = status, Sources = sources.ToList(), ProvenanceStatement = statement, Policies = template.ProtectedDataPolicies.ToList()
			};
		}

		public async Task<int> EnsureCatalogAsync(CancellationToken cancellationToken = default)
		{
			if (Interlocked.CompareExchange(ref _catalogEnsured, 1, 0) != 0) return 0;
			var written = 0;
			try
			{
				var existingPacks = (await _packs.GetCatalogAsync())?.ToList() ?? new List<RmsTemplatePackVersion>();
				foreach (var pack in RecordTemplateCatalog.Packs)
				{
					var checksum = RecordSnapshotSerializer.Checksum(string.Join("|", pack.Definitions.Select(d => d.Key + ":" + RecordSnapshotSerializer.Checksum(d.Schema.Canonical()))));
					var row = existingPacks.FirstOrDefault(p => p.PackKey == pack.Key && p.Version == pack.Version);
					if (row != null && row.ContentChecksum == checksum) continue;
					var now = DateTime.UtcNow;
					row ??= new RmsTemplatePackVersion { RmsTemplatePackVersionId = Guid.NewGuid().ToString(), DepartmentId = RmsTemplatePackVersion.ProductDepartmentId, ProtectionId = Guid.NewGuid().ToString(), PackKey = pack.Key, Version = pack.Version, CreatedOn = now, RowVersion = 0 };
					row.Name = pack.Name; row.Category = pack.Category; row.Description = pack.Description; row.IsPreview = pack.IsPreview;
					row.DefinitionKeys = string.Join(",", pack.Definitions.Select(d => d.Key)); row.SupportedProfiles = string.Join(",", pack.SupportedProfiles); row.SupportedLocales = string.Join(",", pack.SupportedLocales);
					row.ReleaseNotes = pack.ReleaseNotes; row.SourceProvenanceJson = JsonConvert.SerializeObject(pack.Sources); row.ReviewedOn = pack.ReviewedOn; row.ArtifactStatus = (int)pack.ArtifactStatus;
					row.ContentChecksum = checksum; row.ModifiedOn = now; row.RowVersion += 1;
					await _packs.SaveOrUpdateAsync(row, cancellationToken, true);
					written++;
				}
				var existingProfiles = (await _profiles.GetCatalogAsync())?.ToList() ?? new List<RmsJurisdictionProfileVersion>();
				foreach (var profile in RecordTemplateCatalog.Profiles)
				{
					var row = existingProfiles.FirstOrDefault(p => p.ProfileKey == profile.ProfileKey && p.Version == profile.Version);
					if (row != null && row.TerminologyJson == profile.TerminologyJson && row.StandardsJson == profile.StandardsJson && row.Name == profile.Name) continue;
					var copy = JsonConvert.DeserializeObject<RmsJurisdictionProfileVersion>(JsonConvert.SerializeObject(profile));
					copy.RmsJurisdictionProfileVersionId = row?.RmsJurisdictionProfileVersionId ?? Guid.NewGuid().ToString();
					copy.ProtectionId = row?.ProtectionId ?? Guid.NewGuid().ToString();
					copy.ModifiedOn = DateTime.UtcNow; copy.RowVersion = (row?.RowVersion ?? 0) + 1;
					await _profiles.SaveOrUpdateAsync(copy, cancellationToken, true);
					written++;
				}
			}
			catch (Exception ex)
			{
				// The code catalog is authoritative; a mirroring failure (a repository not yet migrated) must not block browsing.
				Interlocked.Exchange(ref _catalogEnsured, 0);
				Resgrid.Framework.Logging.LogException(ex, "Template pack catalog mirror failed; browsing continues from code.");
			}
			return written;
		}

		public RecordDefinitionDiff DiffAgainstTemplate(string templateKey, string profileKey, string locale, RecordDefinitionSchema departmentSchema, string definitionKey, int version)
		{
			var template = RecordTemplateCatalog.Find(templateKey);
			if (template == null) return null;
			var profile = RecordTemplateCatalog.FindProfile(string.IsNullOrWhiteSpace(profileKey) ? "generic" : profileKey) ?? RecordTemplateCatalog.FindProfile("generic");
			var rendering = Render(template, profile, profile.ProfileKey, locale ?? profile.DefaultLocale);
			var diff = new RecordDefinitionDiff { DefinitionKey = definitionKey, FromVersion = version, ToVersion = version };
			RecordDefinitionsService.DiffSchemas(diff, departmentSchema, rendering.Schema);
			return diff;
		}
	}
}
