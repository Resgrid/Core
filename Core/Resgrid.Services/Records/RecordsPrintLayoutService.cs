using System;
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
