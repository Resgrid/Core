using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Builds the starter workbook, single-entity CSV templates and the "current data" workbook (wizard plan section
	/// 6.3 step 4). Headers are "{label} [field_key]", the form <see cref="ImportColumnMatcher"/> detects as a Resgrid
	/// template, so a filled-in workbook needs no column matching. Choice columns offer the contract's codes; reference
	/// columns offer the department's existing names. Personal data is never exported.
	/// </summary>
	public class ImportTemplateService : IImportTemplateService
	{
		private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

		private readonly IImportCatalog _catalog;
		private readonly IImportTemplateWriter _writer;
		private readonly IEnumerable<IImportTargetWriter> _writers;

		public ImportTemplateService(IImportCatalog catalog, IImportTemplateWriter writer, IEnumerable<IImportTargetWriter> writers)
		{
			_catalog = catalog;
			_writer = writer;
			_writers = writers;
		}

		public IReadOnlyList<string> ExportableEntities => _writers.OfType<IImportExporter>().Select(e => e.EntityKey).ToList();

		public async Task<ImportTemplateFile> BuildStarterWorkbookAsync(int departmentId, IReadOnlyCollection<string> entityKeys, Func<string, string> localize,
			CancellationToken cancellationToken = default)
		{
			var workbook = await WorkbookAsync(departmentId, Contracts(entityKeys), localize, false, cancellationToken);
			return new ImportTemplateFile { FileName = "resgrid-starter-workbook.xlsx", ContentType = XlsxContentType, Content = _writer.WriteWorkbook(workbook) };
		}

		public async Task<ImportTemplateFile> BuildCsvTemplateAsync(int departmentId, string entityKey, Func<string, string> localize, CancellationToken cancellationToken = default)
		{
			var contract = _catalog.GetContract(entityKey) ?? throw new DepartmentImportException("import_unknown_destination", 404);
			var sheet = Sheet(contract, localize, null);
			return new ImportTemplateFile { FileName = $"resgrid-{contract.Key}.csv", ContentType = "text/csv", Content = _writer.WriteCsv(sheet) };
		}

		public async Task<ImportTemplateFile> ExportCurrentDataAsync(int departmentId, IReadOnlyCollection<string> entityKeys, Func<string, string> localize,
			CancellationToken cancellationToken = default)
		{
			var exportable = new HashSet<string>(ExportableEntities, StringComparer.OrdinalIgnoreCase);
			var contracts = Contracts(entityKeys).Where(c => exportable.Contains(c.Key)).ToList();
			if (contracts.Count == 0)
				throw new DepartmentImportException("import_unknown_destination", 404);

			var workbook = await WorkbookAsync(departmentId, contracts, localize, true, cancellationToken);
			return new ImportTemplateFile { FileName = "resgrid-current-data.xlsx", ContentType = XlsxContentType, Content = _writer.WriteWorkbook(workbook) };
		}

		private List<ImportEntityContract> Contracts(IReadOnlyCollection<string> entityKeys) =>
			_catalog.InDependencyOrder().Where(c => entityKeys == null || entityKeys.Count == 0 || entityKeys.Contains(c.Key, StringComparer.OrdinalIgnoreCase)).ToList();

		private async Task<ImportTemplateWorkbook> WorkbookAsync(int departmentId, IReadOnlyList<ImportEntityContract> contracts, Func<string, string> localize,
			bool withData, CancellationToken cancellationToken)
		{
			var workbook = new ImportTemplateWorkbook
			{
				Title = localize("TemplateTitle"),
				Instructions = Enumerable.Range(1, 5).Select(i => localize("TemplateInstructions" + i)).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
			};

			// The department's names for every entity a sheet refers to, and each choice field's codes.
			var exporters = _writers.OfType<IImportExporter>().ToDictionary(e => e.EntityKey, StringComparer.OrdinalIgnoreCase);
			var exported = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
			async Task<List<Dictionary<string, string>>> RecordsAsync(string entityKey)
			{
				if (!exported.TryGetValue(entityKey, out var rows))
					exported[entityKey] = rows = exporters.TryGetValue(entityKey, out var exporter) ? await exporter.ExportAsync(departmentId, cancellationToken) : new List<Dictionary<string, string>>();
				return rows;
			}

			foreach (var contract in contracts)
			{
				foreach (var field in contract.Fields)
				{
					if (field.Type == ImportFieldTypes.Choice && field.Choices.Count > 0)
						workbook.Lists[ListName(contract.Key, field.Key)] = field.Choices.Select(c => c.Code).ToList();
					else if (field.References != null && field.References != contract.Key && exporters.ContainsKey(field.References))
						workbook.Lists[ListName(field.References, "name")] = (await RecordsAsync(field.References))
							.Select(r => r.TryGetValue(NameField(field.References), out var name) ? name : null)
							.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
				}

				workbook.Sheets.Add(Sheet(contract, localize, withData ? await RecordsAsync(contract.Key) : null));
			}

			return workbook;
		}

		private static ImportTemplateSheet Sheet(ImportEntityContract contract, Func<string, string> localize, List<Dictionary<string, string>> data)
		{
			var sheet = new ImportTemplateSheet { Name = localize(contract.LabelKey), EntityKey = contract.Key };
			foreach (var field in contract.Fields)
			{
				sheet.Columns.Add(new ImportTemplateColumn
				{
					Header = $"{localize(field.LabelKey)} [{field.Key}]",
					Note = localize(field.DescriptionKey),
					Required = field.Required,
					ListName = field.Type == ImportFieldTypes.Choice && field.Choices.Count > 0 ? ListName(contract.Key, field.Key)
						: field.References != null && field.References != contract.Key ? ListName(field.References, "name") : null,
					CellType = field.Type == ImportFieldTypes.Date ? "date" : field.Type == ImportFieldTypes.Integer || field.Type == ImportFieldTypes.Decimal ? "number" : "text"
				});
			}

			sheet.ExampleRows.Add(contract.Fields.Select(f => f.Example ?? string.Empty).ToArray());
			if (data != null)
				foreach (var record in data)
					sheet.DataRows.Add(contract.Fields.Select(f => record.TryGetValue(f.Key, out var value) ? value ?? string.Empty : string.Empty).ToArray());

			return sheet;
		}

		private static string ListName(string entityKey, string fieldKey) => $"{entityKey}.{fieldKey}";

		private static string NameField(string entityKey) => entityKey == "custom-statuses" ? "set_name" : "name";
	}
}
