using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Resgrid.Framework;
using Resgrid.Model.Onboarding;

namespace Resgrid.Providers.Import.Templates
{
	/// <summary>
	/// Writes the Resgrid starter workbook and per-entity CSV templates (wizard plan sections 8.3 and 14.7). Values are
	/// written as inline strings with explicit types; the only formulas are Excel's own data-validation list references.
	/// Each data sheet carries a trailing "resgrid:example" marker column: rows with an "x" there are examples the
	/// importer skips.
	/// </summary>
	public sealed class ImportTemplateWriter : IImportTemplateWriter
	{
		public const string ExampleMarkerHeader = ImportTemplateMarkers.ExampleColumn;
		public const string InstructionsSheetName = ImportTemplateMarkers.InstructionsSheet;
		public const string ListsSheetName = ImportTemplateMarkers.ListsSheet;
		private const int ValidatedRows = 2000;

		public byte[] WriteWorkbook(ImportTemplateWorkbook workbook)
		{
			using var stream = new MemoryStream();
			using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
			{
				var workbookPart = document.AddWorkbookPart();
				workbookPart.Workbook = new Workbook();
				AddStyles(workbookPart);
				var sheets = workbookPart.Workbook.AppendChild(new Sheets());
				uint sheetId = 1;

				// Lists first so every data sheet's validations can name their columns.
				var listColumns = new Dictionary<string, (string Column, int Count)>(StringComparer.OrdinalIgnoreCase);
				var listIndex = 0;
				foreach (var list in workbook.Lists)
					listColumns[list.Key] = (ColumnName(listIndex++), list.Value?.Count ?? 0);

				AddSheet(workbookPart, sheets, sheetId++, InstructionsSheetName, false, sheetData =>
				{
					uint row = 1;
					sheetData.AppendChild(Row(row++, new[] { workbook.Title ?? "Resgrid starter workbook" }, styleIndex: 1));
					foreach (var line in workbook.Instructions)
						sheetData.AppendChild(Row(row++, new[] { line }));
				}, columns: new Columns(new Column { Min = 1, Max = 1, Width = 120, CustomWidth = true }));

				foreach (var sheet in workbook.Sheets)
				{
					var headers = sheet.Columns.Select(c => c.Header).Concat(new[] { ExampleMarkerHeader }).ToArray();
					AddSheet(workbookPart, sheets, sheetId++, SafeSheetName(sheet.Name), false, sheetData =>
					{
						uint row = 1;
						sheetData.AppendChild(Row(row++, headers, styleIndex: 1));
						foreach (var example in sheet.ExampleRows)
						{
							var cells = example.Concat(Enumerable.Repeat(string.Empty, Math.Max(0, sheet.Columns.Count - example.Length)))
								.Take(sheet.Columns.Count).Concat(new[] { "x" }).ToArray();
							sheetData.AppendChild(Row(row++, cells, styleIndex: 2));
						}
						foreach (var data in sheet.DataRows)
						{
							var cells = data.Concat(Enumerable.Repeat(string.Empty, Math.Max(0, sheet.Columns.Count - data.Length)))
								.Take(sheet.Columns.Count).Concat(new[] { string.Empty }).ToArray();
							sheetData.AppendChild(Row(row++, cells));
						}
					},
					columns: new Columns(sheet.Columns.Select((c, i) => new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = Math.Max(14, Math.Min(40, (c.Header?.Length ?? 10) + 4)), CustomWidth = true })),
					afterData: worksheet =>
					{
						// Freeze the header row.
						worksheet.InsertAt(new SheetViews(new SheetView(new Pane
						{
							VerticalSplit = 1,
							TopLeftCell = "A2",
							ActivePane = PaneValues.BottomLeft,
							State = PaneStateValues.Frozen
						})
						{ WorkbookViewId = 0 }), 0);

						var validations = new DataValidations();
						for (var i = 0; i < sheet.Columns.Count; i++)
						{
							var column = sheet.Columns[i];
							var cellColumn = ColumnName(i);
							var range = $"{cellColumn}2:{cellColumn}{ValidatedRows + 1}";
							if (!string.IsNullOrEmpty(column.ListName) && listColumns.TryGetValue(column.ListName, out var list) && list.Count > 0)
							{
								validations.AppendChild(new DataValidation
								{
									Type = DataValidationValues.List,
									AllowBlank = true,
									ShowErrorMessage = true,
									ErrorStyle = DataValidationErrorStyleValues.Warning,
									SequenceOfReferences = new ListValue<StringValue> { InnerText = range },
									Formula1 = new Formula1($"{ListsSheetName}!${list.Column}$2:${list.Column}${list.Count + 1}")
								});
							}
							else if (column.CellType == "date")
							{
								validations.AppendChild(new DataValidation
								{
									Type = DataValidationValues.Date,
									Operator = DataValidationOperatorValues.GreaterThan,
									AllowBlank = true,
									ShowErrorMessage = true,
									ErrorStyle = DataValidationErrorStyleValues.Warning,
									SequenceOfReferences = new ListValue<StringValue> { InnerText = range },
									Formula1 = new Formula1("1")
								});
							}
						}

						if (validations.HasChildren)
						{
							validations.Count = (uint)validations.ChildElements.Count;
							worksheet.AppendChild(validations);
						}
					});
				}

				AddSheet(workbookPart, sheets, sheetId, ListsSheetName, true, sheetData =>
				{
					var lists = workbook.Lists.ToList();
					var depth = lists.Count == 0 ? 0 : lists.Max(l => l.Value?.Count ?? 0);
					sheetData.AppendChild(Row(1, lists.Select(l => l.Key).ToArray(), styleIndex: 1));
					for (var r = 0; r < depth; r++)
						sheetData.AppendChild(Row((uint)r + 2, lists.Select(l => l.Value != null && r < l.Value.Count ? l.Value[r] : string.Empty).ToArray()));
				});

				workbookPart.Workbook.Save();
			}

			return stream.ToArray();
		}

		public byte[] WriteCsv(ImportTemplateSheet sheet)
		{
			var builder = new StringBuilder();
			builder.Append(CsvCell.Line(sheet.Columns.Select(c => c.Header).Concat(new[] { ExampleMarkerHeader }))).Append("\r\n");
			foreach (var example in sheet.ExampleRows)
			{
				var cells = example.Concat(Enumerable.Repeat(string.Empty, Math.Max(0, sheet.Columns.Count - example.Length)))
					.Take(sheet.Columns.Count).Concat(new[] { "x" });
				builder.Append(CsvCell.Line(cells)).Append("\r\n");
			}

			foreach (var data in sheet.DataRows)
			{
				var cells = data.Concat(Enumerable.Repeat(string.Empty, Math.Max(0, sheet.Columns.Count - data.Length)))
					.Take(sheet.Columns.Count).Concat(new[] { string.Empty });
				builder.Append(CsvCell.Line(cells)).Append("\r\n");
			}

			// UTF-8 with a BOM so Excel opens non-ASCII text correctly.
			return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
		}

		private static void AddSheet(WorkbookPart workbookPart, Sheets sheets, uint sheetId, string name, bool hidden, Action<SheetData> fill,
			Columns columns = null, Action<Worksheet> afterData = null)
		{
			var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
			var sheetData = new SheetData();
			fill(sheetData);

			var worksheet = new Worksheet();
			if (columns != null && columns.HasChildren)
				worksheet.AppendChild(columns);
			worksheet.AppendChild(sheetData);
			afterData?.Invoke(worksheet);
			worksheetPart.Worksheet = worksheet;

			var sheet = new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = name };
			if (hidden)
				sheet.State = SheetStateValues.Hidden;
			sheets.AppendChild(sheet);
		}

		private static Row Row(uint rowIndex, IReadOnlyList<string> values, uint styleIndex = 0)
		{
			var row = new Row { RowIndex = rowIndex };
			for (var i = 0; i < values.Count; i++)
			{
				var cell = new Cell
				{
					CellReference = ColumnName(i) + rowIndex,
					DataType = CellValues.InlineString,
					InlineString = new InlineString(new Text(Sanitize(values[i])) { Space = SpaceProcessingModeValues.Preserve })
				};
				if (styleIndex > 0)
					cell.StyleIndex = styleIndex;
				row.AppendChild(cell);
			}

			return row;
		}

		/// <summary>Strips characters XML cannot carry; template text is generated, so this only guards department names.</summary>
		private static string Sanitize(string value) =>
			string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(c => c == '\t' || c == '\n' || c == '\r' || c >= 0x20).ToArray());

		private static void AddStyles(WorkbookPart workbookPart)
		{
			var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
			stylesPart.Stylesheet = new Stylesheet(
				new Fonts(
					new Font(),
					new Font(new Bold()),
					new Font(new Italic(), new Color { Rgb = "FF6B6B6B" })),
				new Fills(
					new Fill(new PatternFill { PatternType = PatternValues.None }),
					new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
					new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE9E8E4" }) { PatternType = PatternValues.Solid })),
				new Borders(new Border()),
				new CellFormats(
					new CellFormat(),
					new CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true },
					new CellFormat { FontId = 2, ApplyFont = true }));
			stylesPart.Stylesheet.Save();
		}

		internal static string ColumnName(int index)
		{
			var name = string.Empty;
			index++;
			while (index > 0)
			{
				var remainder = (index - 1) % 26;
				name = (char)('A' + remainder) + name;
				index = (index - 1) / 26;
			}
			return name;
		}

		private static string SafeSheetName(string name)
		{
			var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
			var cleaned = new string((name ?? "Sheet").Where(c => !invalid.Contains(c)).ToArray()).Trim('\'');
			return cleaned.Length > 31 ? cleaned.Substring(0, 31) : (cleaned.Length == 0 ? "Sheet" : cleaned);
		}
	}
}
