using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// DepartmentDefault print layout (RMS plan section 4.10.1). Every save bumps the version so the provenance
	/// footer on a print names the exact letterhead it rendered with; record content never depends on it.
	/// </summary>
	public class RecordsPrintLayoutService : IRecordsPrintLayoutService
	{
		private readonly IRmsRecordPrintLayoutsRepository _layouts;

		public RecordsPrintLayoutService(IRmsRecordPrintLayoutsRepository layouts)
		{
			_layouts = layouts;
		}

		public async Task<RmsRecordPrintLayout> GetDepartmentDefaultAsync(int departmentId)
		{
			var row = await _layouts.GetAsync(departmentId, (int)RmsRecordPrintLayoutScope.DepartmentDefault, string.Empty);
			if (row == null)
			{
				return new RmsRecordPrintLayout
				{
					DepartmentId = departmentId,
					Scope = (int)RmsRecordPrintLayoutScope.DepartmentDefault,
					DefinitionKey = string.Empty,
					Version = 0,
					Config = RecordsPrintLayoutConfig.Default()
				};
			}

			row.Config = Parse(row.ConfigJson);
			return row;
		}

		public async Task<RmsRecordPrintLayout> SaveDepartmentDefaultAsync(int departmentId, string userId, RecordsPrintLayoutConfig config, CancellationToken cancellationToken = default)
		{
			config = Normalize(config ?? RecordsPrintLayoutConfig.Default());
			var now = DateTime.UtcNow;
			var row = await _layouts.GetAsync(departmentId, (int)RmsRecordPrintLayoutScope.DepartmentDefault, string.Empty);

			if (row == null)
			{
				row = new RmsRecordPrintLayout
				{
					RmsRecordPrintLayoutId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					ProtectionId = Guid.NewGuid().ToString(),
					Scope = (int)RmsRecordPrintLayoutScope.DepartmentDefault,
					DefinitionKey = string.Empty,
					Version = 1,
					CreatedOn = now,
					RowVersion = 1
				};
			}
			else
			{
				row.Version += 1;
				row.RowVersion += 1;
			}

			row.ConfigJson = JsonConvert.SerializeObject(config);
			row.ModifiedByUserId = userId;
			row.ModifiedOn = now;
			row = await _layouts.SaveOrUpdateAsync(row, cancellationToken, true);
			row.Config = config;
			return row;
		}

		public async Task<RmsRecordPrintLayout> GetDefinitionLayoutAsync(int departmentId, string definitionKey)
		{
			var key = (definitionKey ?? string.Empty).Trim().ToLowerInvariant();
			var row = string.IsNullOrEmpty(key) ? null : await _layouts.GetAsync(departmentId, (int)RmsRecordPrintLayoutScope.Definition, key);
			if (row == null)
				return new RmsRecordPrintLayout { DepartmentId = departmentId, Scope = (int)RmsRecordPrintLayoutScope.Definition, DefinitionKey = key, Version = 0, DefinitionConfig = RecordsDefinitionLayoutConfig.Default() };
			row.DefinitionConfig = ParseDefinition(row.ConfigJson);
			return row;
		}

		public async Task<RmsRecordPrintLayout> SaveDefinitionLayoutAsync(int departmentId, string userId, string definitionKey, RecordsDefinitionLayoutConfig config, CancellationToken cancellationToken = default)
		{
			var key = (definitionKey ?? string.Empty).Trim().ToLowerInvariant();
			if (string.IsNullOrEmpty(key)) throw new ArgumentException("A definition key is required.", nameof(definitionKey));
			config = NormalizeDefinition(config ?? RecordsDefinitionLayoutConfig.Default());
			var now = DateTime.UtcNow;
			var row = await _layouts.GetAsync(departmentId, (int)RmsRecordPrintLayoutScope.Definition, key);
			if (row == null)
			{
				row = new RmsRecordPrintLayout
				{
					RmsRecordPrintLayoutId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
					Scope = (int)RmsRecordPrintLayoutScope.Definition, DefinitionKey = key, Version = 1, CreatedOn = now, RowVersion = 1
				};
			}
			else
			{
				row.Version += 1;
				row.RowVersion += 1;
			}
			row.ConfigJson = JsonConvert.SerializeObject(config);
			row.ModifiedByUserId = userId;
			row.ModifiedOn = now;
			row = await _layouts.SaveOrUpdateAsync(row, cancellationToken, true);
			row.DefinitionConfig = config;
			return row;
		}

		public async Task<RecordsResolvedPrintLayout> ResolveForDefinitionAsync(int departmentId, string definitionKey, int definitionVersion)
		{
			var department = await GetDepartmentDefaultAsync(departmentId);
			var resolved = new RecordsResolvedPrintLayout { Branding = department.Config ?? RecordsPrintLayoutConfig.Default(), BrandingLayoutVersion = department.LayoutVersion };
			if (string.IsNullOrWhiteSpace(definitionKey) || RmsDefinitionKeys.LockedTypes.ContainsKey(definitionKey))
				return resolved;
			var definition = await GetDefinitionLayoutAsync(departmentId, definitionKey);
			if (definition.Version <= 0 || definition.DefinitionConfig == null || !definition.DefinitionConfig.AppliesTo(definitionVersion))
				return resolved;
			resolved.Definition = definition.DefinitionConfig;
			resolved.DefinitionLayoutVersion = definition.LayoutVersion;
			if (definition.DefinitionConfig.BrandingOverrides != null)
			{
				// Overrides replace the whole branding block, page size included; the provenance footer names both versions.
				resolved.Branding = Normalize(definition.DefinitionConfig.BrandingOverrides);
				resolved.BrandingLayoutVersion = definition.LayoutVersion + "/branding";
			}
			return resolved;
		}

		public static RecordsDefinitionLayoutConfig ParseDefinition(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return RecordsDefinitionLayoutConfig.Default();
			try
			{
				return NormalizeDefinition(JsonConvert.DeserializeObject<RecordsDefinitionLayoutConfig>(json) ?? RecordsDefinitionLayoutConfig.Default());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Definition print layout could not be parsed; using the generated default.");
				return RecordsDefinitionLayoutConfig.Default();
			}
		}

		public static RecordsDefinitionLayoutConfig NormalizeDefinition(RecordsDefinitionLayoutConfig config)
		{
			static List<string> Keys(IEnumerable<string> keys) => (keys ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
			config.SectionOrder = Keys(config.SectionOrder);
			config.HiddenSectionKeys = Keys(config.HiddenSectionKeys);
			config.HiddenFieldKeys = Keys(config.HiddenFieldKeys);
			config.PageBreakBeforeSectionKeys = Keys(config.PageBreakBeforeSectionKeys);
			// "Order", "order" and "order " all normalize to the same key, and ToDictionary would throw on the second
			// one — turning a client-supplied layout into an unhandled failure on save. Keep the first and move on.
			config.SectionHeadings = (config.SectionHeadings ?? new Dictionary<string, string>()).Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
				.GroupBy(p => p.Key.Trim().ToLowerInvariant(), StringComparer.Ordinal)
				.ToDictionary(g => g.Key, g => Trim(g.First().Value, 120), StringComparer.OrdinalIgnoreCase);
			config.SignatureBlockPlacement = RecordsDefinitionLayoutConfig.SignaturePlacements.Contains((config.SignatureBlockPlacement ?? string.Empty).Trim().ToLowerInvariant())
				? config.SignatureBlockPlacement.Trim().ToLowerInvariant() : RecordsDefinitionLayoutConfig.SignatureAtEnd;
			config.AttachmentListStyle = RecordsDefinitionLayoutConfig.AttachmentStyles.Contains((config.AttachmentListStyle ?? string.Empty).Trim().ToLowerInvariant())
				? config.AttachmentListStyle.Trim().ToLowerInvariant() : RecordsDefinitionLayoutConfig.AttachmentsTable;
			if (config.AppliesToVersion.HasValue && config.AppliesToVersion.Value <= 0) config.AppliesToVersion = null;
			if (config.BrandingOverrides != null) config.BrandingOverrides = Normalize(config.BrandingOverrides);
			return config;
		}

		public static RecordsPrintLayoutConfig Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return RecordsPrintLayoutConfig.Default();

			try
			{
				return Normalize(JsonConvert.DeserializeObject<RecordsPrintLayoutConfig>(json) ?? RecordsPrintLayoutConfig.Default());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Print layout config could not be parsed; using the generated default.");
				return RecordsPrintLayoutConfig.Default();
			}
		}

		public static RecordsPrintLayoutConfig Normalize(RecordsPrintLayoutConfig config)
		{
			config.PageSize = RecordsPrintLayoutConfig.NormalizePageSize(config.PageSize);
			config.LetterheadLine1 = Trim(config.LetterheadLine1, 200);
			config.LetterheadLine2 = Trim(config.LetterheadLine2, 200);
			config.FooterText = Trim(config.FooterText, 500);
			config.WatermarkLabel = Trim(config.WatermarkLabel, 40);
			config.DateTimeFormat = Trim(config.DateTimeFormat, 40);
			return config;
		}

		private static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}
	}
}
