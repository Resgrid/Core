using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using CsvHelper;
using CsvHelper.Configuration;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Resgrid.Model.Onboarding;

namespace Resgrid.Providers.Import.Readers
{
	/// <summary>
	/// CSV/TSV, XLSX and JSON/JSONL/GeoJSON readers for Department Import (wizard plan sections 8.3 and 14.1). Signature
	/// wins over extension; macro-enabled, binary and encrypted workbooks are refused; formulas are never evaluated (cached
	/// values only); XLSX dates honour the workbook's 1900/1904 system. Errors carry issue codes and row numbers only:
	/// no cell value ever reaches an exception message or a log.
	/// </summary>
	public sealed class ImportFileReader : IImportFileReader
	{
		private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };
		private static readonly byte[] OleSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
		private static readonly char[] CandidateDelimiters = { ',', ';', '\t', '|' };
		private const int HeadLength = 64 * 1024;

		static ImportFileReader()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}

		public ImportFileInspection Inspect(Stream content, string fileName)
		{
			var extension = (Path.GetExtension(fileName ?? string.Empty) ?? string.Empty).ToLowerInvariant();
			var head = ReadHead(content, HeadLength);

			// Workbook formats that are never accepted, whatever their content: macros, binary and legacy workbooks.
			if (extension == ".xlsm" || extension == ".xltm" || extension == ".xlam")
				return Reject("IMP-FILE-MACRO");
			if (extension == ".xls" || extension == ".xlsb")
				return Reject("IMP-FILE-SIGNATURE");

			if (StartsWith(head, OleSignature))
				// An OLE container is a legacy .xls or a password-protected (encrypted) OOXML workbook.
				return Reject("IMP-FILE-ENCRYPTED");

			if (StartsWith(head, ZipSignature))
				return InspectZip(content, extension);

			if (head.Any(b => b == 0))
				return Reject("IMP-FILE-SIGNATURE");

			var text = DecodeHead(head, out var encoding);
			if (text == null)
				return new ImportFileInspection { Format = GuessTextFormat(extension), RejectionCode = "IMP-FILE-ENCODING", Encoding = "unknown" };

			var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
			if (trimmed.StartsWith("<", StringComparison.Ordinal))
			{
				var lower = trimmed.Length > 2048 ? trimmed.Substring(0, 2048).ToLowerInvariant() : trimmed.ToLowerInvariant();
				if (lower.Contains("<kml"))
					return new ImportFileInspection { Format = ImportFileFormat.Kml, Encoding = encoding };
				if (lower.Contains("<gpx"))
					return new ImportFileInspection { Format = ImportFileFormat.Gpx, Encoding = encoding };
				return Reject("IMP-FILE-SIGNATURE");
			}

			if (trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal))
			{
				if (trimmed.StartsWith("{", StringComparison.Ordinal))
				{
					var firstLineEnd = trimmed.IndexOf('\n');
					var firstLine = firstLineEnd > 0 ? trimmed.Substring(0, firstLineEnd).Trim() : trimmed.Trim();
					if (firstLine.EndsWith("}", StringComparison.Ordinal) && firstLineEnd > 0 && trimmed.Substring(firstLineEnd).TrimStart().StartsWith("{", StringComparison.Ordinal))
						return new ImportFileInspection { Format = ImportFileFormat.JsonLines, Encoding = encoding };

					if (trimmed.Replace(" ", string.Empty).Contains("\"type\":\"FeatureCollection\""))
						return new ImportFileInspection { Format = ImportFileFormat.GeoJson, Encoding = encoding };
				}

				return new ImportFileInspection { Format = extension == ".geojson" ? ImportFileFormat.GeoJson : ImportFileFormat.Json, Encoding = encoding };
			}

			var delimiter = SniffDelimiter(trimmed, extension);
			return new ImportFileInspection
			{
				Format = delimiter == "\t" ? ImportFileFormat.Tsv : ImportFileFormat.Csv,
				Delimiter = delimiter,
				Encoding = encoding
			};
		}

		public IReadOnlyList<ImportSheetInfo> ListSheets(Stream content, ImportFileFormat format)
		{
			if (format != ImportFileFormat.Xlsx)
				return new[] { new ImportSheetInfo { Index = 0, Name = null } };

			content.Position = 0;
			using var document = SpreadsheetDocument.Open(content, false, new OpenSettings { AutoSave = false });
			var sheets = document.WorkbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? new List<Sheet>();
			return sheets.Select((sheet, i) => new ImportSheetInfo
			{
				Index = i,
				Name = sheet.Name?.Value,
				Hidden = sheet.State != null && sheet.State.Value != SheetStateValues.Visible
			}).ToList();
		}

		public ImportSheetReadResult ReadSheet(Stream content, ImportFileFormat format, int sheetIndex, ImportReadOptions options,
			Action<int, string[]> onRow, CancellationToken cancellationToken = default)
		{
			options ??= new ImportReadOptions();
			var clock = Stopwatch.StartNew();
			var result = new ImportSheetReadResult();
			content.Position = 0;

			try
			{
				switch (format)
				{
					case ImportFileFormat.Csv:
					case ImportFileFormat.Tsv:
						ReadDelimited(content, format, options, onRow, result, clock, cancellationToken);
						break;
					case ImportFileFormat.Xlsx:
						ReadWorkbook(content, sheetIndex, options, onRow, result, clock, cancellationToken);
						break;
					case ImportFileFormat.Json:
					case ImportFileFormat.JsonLines:
					case ImportFileFormat.GeoJson:
						ReadJson(content, format, options, onRow, result, clock, cancellationToken);
						break;
					default:
						result.ErrorCode = "IMP-FILE-UNSUPPORTED";
						break;
				}
			}
			catch (DecoderFallbackException)
			{
				result.ErrorCode = "IMP-FILE-ENCODING";
			}
			catch (JsonException ex)
			{
				result.ErrorCode = "IMP-PARSE-JSON";
				result.ErrorRow = ex.LineNumber.HasValue ? (int)ex.LineNumber.Value + 1 : (int?)null;
			}
			catch (MaxFieldSizeException ex)
			{
				result.ErrorCode = "IMP-FILE-FIELD-TOO-LONG";
				result.ErrorRow = ex.Context?.Parser?.Row;
			}
			catch (CsvHelperException ex)
			{
				result.ErrorCode = "IMP-PARSE-CSV";
				result.ErrorRow = ex.Context?.Parser?.Row;
			}
			catch (OpenXmlPackageException)
			{
				result.ErrorCode = "IMP-PARSE-XLSX";
			}
			catch (InvalidDataException)
			{
				result.ErrorCode = "IMP-PARSE-XLSX";
			}

			return result;
		}

		// ---- CSV / TSV -------------------------------------------------------------------------------------------

		private static void ReadDelimited(Stream content, ImportFileFormat format, ImportReadOptions options, Action<int, string[]> onRow,
			ImportSheetReadResult result, Stopwatch clock, CancellationToken cancellationToken)
		{
			var encoding = options.AllowWindows1252
				? Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
				: new UTF8Encoding(false, true);

			// A BOM selects its own encoding (UTF-8/UTF-16); otherwise strict UTF-8 unless the administrator confirmed 1252.
			using var reader = new StreamReader(content, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
			var delimiter = string.IsNullOrEmpty(options.Delimiter) ? (format == ImportFileFormat.Tsv ? "\t" : ",") : options.Delimiter;
			var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
			{
				Delimiter = delimiter,
				HasHeaderRecord = false,
				BadDataFound = null,
				MissingFieldFound = null,
				DetectColumnCountChanges = false,
				IgnoreBlankLines = false,
				TrimOptions = TrimOptions.None,
				// An outer guard against a runaway field (an unterminated quote); the exact cap is checked per cell in Accept.
				MaxFieldSize = Math.Max(options.MaxFieldLength * 4, 1024 * 1024)
			};

			using var parser = new CsvParser(reader, configuration);
			var row = 0;
			while (parser.Read())
			{
				cancellationToken.ThrowIfCancellationRequested();
				row++;
				var record = parser.Record ?? Array.Empty<string>();
				if (!Accept(row, record, options, onRow, result, clock))
					break;
			}
		}

		// ---- XLSX ------------------------------------------------------------------------------------------------

		private static void ReadWorkbook(Stream content, int sheetIndex, ImportReadOptions options, Action<int, string[]> onRow,
			ImportSheetReadResult result, Stopwatch clock, CancellationToken cancellationToken)
		{
			using var document = SpreadsheetDocument.Open(content, false, new OpenSettings { AutoSave = false });
			var workbookPart = document.WorkbookPart;
			var sheet = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ElementAtOrDefault(sheetIndex);
			if (sheet?.Id?.Value == null)
			{
				result.ErrorCode = "IMP-PARSE-XLSX";
				return;
			}

			var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
			var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>()
				.Select(item => item.InnerText ?? string.Empty).ToArray() ?? Array.Empty<string>();
			var dateStyles = DateStyleIndexes(workbookPart.WorkbookStylesPart?.Stylesheet, out var timeStyles);
			var date1904 = workbookPart.Workbook.WorkbookProperties?.Date1904?.Value == true;

			using var reader = OpenXmlReader.Create(worksheetPart);
			var expectedRow = 0;
			while (reader.Read())
			{
				if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
					continue;

				cancellationToken.ThrowIfCancellationRequested();
				var rowElement = (Row)reader.LoadCurrentElement();
				var rowNumber = rowElement.RowIndex?.Value != null ? (int)rowElement.RowIndex.Value : expectedRow + 1;

				// Emit blank rows the sheet skipped so row numbers stay the ones the administrator sees in Excel.
				while (expectedRow + 1 < rowNumber)
				{
					expectedRow++;
					if (!Accept(expectedRow, Array.Empty<string>(), options, onRow, result, clock))
						return;
				}

				var cells = new List<string>();
				foreach (var cell in rowElement.Elements<Cell>())
				{
					var column = ColumnIndex(cell.CellReference?.Value, cells.Count);
					if (column >= options.MaxColumns)
					{
						result.ErrorCode = "IMP-FILE-TOO-WIDE";
						result.ErrorRow = rowNumber;
						return;
					}

					while (cells.Count < column)
						cells.Add(string.Empty);

					cells.Add(CellText(cell, sharedStrings, dateStyles, timeStyles, date1904));
				}

				expectedRow = rowNumber;
				if (!Accept(rowNumber, cells.ToArray(), options, onRow, result, clock))
					return;
			}
		}

		private static string CellText(Cell cell, string[] sharedStrings, HashSet<uint> dateStyles, HashSet<uint> timeStyles, bool date1904)
		{
			// Formulas are never evaluated: the cached <v> value is what Excel last showed.
			var type = cell.DataType?.Value;
			var raw = cell.CellValue?.Text;

			if (type == CellValues.InlineString)
				return cell.InlineString?.InnerText ?? string.Empty;

			if (raw == null)
				return string.Empty;

			if (type == CellValues.SharedString)
				return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Length
					? sharedStrings[index]
					: string.Empty;

			if (type == CellValues.Boolean)
				return raw == "1" ? "TRUE" : "FALSE";

			if (type == CellValues.Error)
				return string.Empty;

			if (type == CellValues.String || type == CellValues.Date)
				return raw;

			if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
				return raw;

			if (cell.StyleIndex?.Value != null && timeStyles.Contains(cell.StyleIndex.Value) && number >= 0 && number < 1)
				return TimeSpan.FromDays(number).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

			if (cell.StyleIndex?.Value != null && dateStyles.Contains(cell.StyleIndex.Value))
			{
				var date = FromSerial(number, date1904);
				if (date.HasValue)
					return date.Value.TimeOfDay == TimeSpan.Zero
						? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
						: date.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
			}

			// Plain digits (phone numbers, ids) must not become 1.5551234567E+10.
			if (Math.Abs(number) < 1e17 && Math.Abs(number % 1) < double.Epsilon)
				return ((long)number).ToString(CultureInfo.InvariantCulture);

			return ((decimal)number).ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>Excel serial date to DateTime: the 1900 system counts the phantom 1900-02-29, the 1904 system does not.</summary>
		public static DateTime? FromSerial(double serial, bool date1904)
		{
			if (serial < 0 || serial > 2958465)
				return null;

			if (date1904)
				return new DateTime(1904, 1, 1).AddDays(serial);

			if (serial < 1)
				return new DateTime(1899, 12, 30).AddDays(serial);

			// Serial 60 is Excel's nonexistent 1900-02-29; days after it are one ahead of the real calendar.
			var days = serial >= 61 ? serial - 1 : serial;
			return new DateTime(1899, 12, 31).AddDays(days);
		}

		private static HashSet<uint> DateStyleIndexes(Stylesheet stylesheet, out HashSet<uint> timeStyles)
		{
			var result = new HashSet<uint>();
			timeStyles = new HashSet<uint>();
			var formats = stylesheet?.CellFormats?.Elements<CellFormat>().ToList();
			if (formats == null)
				return result;

			var custom = stylesheet.NumberingFormats?.Elements<NumberingFormat>()
				.Where(f => f.NumberFormatId?.Value != null)
				.ToDictionary(f => f.NumberFormatId.Value, f => f.FormatCode?.Value ?? string.Empty) ?? new Dictionary<uint, string>();

			for (var i = 0; i < formats.Count; i++)
			{
				var id = formats[i].NumberFormatId?.Value ?? 0;
				custom.TryGetValue(id, out var code);
				if ((id >= 18 && id <= 21) || (id >= 45 && id <= 47) || (code != null && LooksLikeTimeFormat(code)))
					timeStyles.Add((uint)i);
				else if ((id >= 14 && id <= 17) || id == 22 || (id >= 27 && id <= 36) || (id >= 50 && id <= 58) || (code != null && LooksLikeDateFormat(code)))
					result.Add((uint)i);
			}

			return result;
		}

		/// <summary>True for a custom number format that renders a calendar date (day, year or month name).</summary>
		public static bool LooksLikeDateFormat(string code)
		{
			var text = FormatTokens(code);
			return text.IndexOfAny(new[] { 'd', 'y' }) >= 0 || text.Contains("mmm");
		}

		/// <summary>True for a time-only custom format (hours or seconds, no day or year).</summary>
		public static bool LooksLikeTimeFormat(string code)
		{
			var text = FormatTokens(code);
			return !LooksLikeDateFormat(code) && (text.Contains('h') || text.Contains('s'));
		}

		/// <summary>The format's letters with quoted literals, escaped characters and bracketed sections ([Red], [$-409]) removed.</summary>
		private static string FormatTokens(string code)
		{
			if (string.IsNullOrEmpty(code))
				return string.Empty;

			var cleaned = new StringBuilder();
			var inQuote = false;
			var inBracket = false;
			for (var i = 0; i < code.Length; i++)
			{
				var c = code[i];
				if (c == '"') { inQuote = !inQuote; continue; }
				if (inQuote) continue;
				if (c == '[') { inBracket = true; continue; }
				if (c == ']') { inBracket = false; continue; }
				if (inBracket) continue;
				if (c == '\\') { i++; continue; }
				cleaned.Append(char.ToLowerInvariant(c));
			}

			return cleaned.ToString();
		}

		private static int ColumnIndex(string reference, int fallback)
		{
			if (string.IsNullOrEmpty(reference))
				return fallback;

			var index = 0;
			foreach (var c in reference)
			{
				if (c < 'A' || c > 'Z')
					break;
				index = index * 26 + (c - 'A' + 1);
			}

			return index > 0 ? index - 1 : fallback;
		}

		// ---- JSON / JSONL / GeoJSON ----------------------------------------------------------------------------

		private static void ReadJson(Stream content, ImportFileFormat format, ImportReadOptions options, Action<int, string[]> onRow,
			ImportSheetReadResult result, Stopwatch clock, CancellationToken cancellationToken)
		{
			var documentOptions = new JsonDocumentOptions { MaxDepth = 64, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };
			var records = new List<Dictionary<string, string>>();
			var headers = new List<string>();
			var headerSet = new HashSet<string>(StringComparer.Ordinal);

			void Add(Dictionary<string, string> record)
			{
				foreach (var key in record.Keys)
					if (headerSet.Add(key))
						headers.Add(key);
				records.Add(record);
			}

			if (format == ImportFileFormat.JsonLines)
			{
				using var reader = new StreamReader(content, new UTF8Encoding(false, true), true, 64 * 1024, leaveOpen: true);
				string line;
				var lineNumber = 0;
				while ((line = reader.ReadLine()) != null)
				{
					cancellationToken.ThrowIfCancellationRequested();
					lineNumber++;
					if (string.IsNullOrWhiteSpace(line))
						continue;

					using var lineDocument = ParseLine(line, documentOptions, lineNumber);
					if (lineDocument.RootElement.ValueKind != JsonValueKind.Object)
					{
						result.ErrorCode = "IMP-PARSE-JSON-SHAPE";
						result.ErrorRow = lineNumber;
						return;
					}

					Add(Flatten(lineDocument.RootElement));
					if (records.Count > options.MaxRows)
						break;
				}
			}
			else
			{
				using var document = JsonDocument.Parse(content, documentOptions);
				var root = document.RootElement;
				IEnumerable<JsonElement> items;

				if (format == ImportFileFormat.GeoJson || (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("features", out _)))
				{
					if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
					{
						result.ErrorCode = "IMP-PARSE-JSON-SHAPE";
						return;
					}

					foreach (var feature in features.EnumerateArray())
					{
						cancellationToken.ThrowIfCancellationRequested();
						Add(FlattenFeature(feature));
						if (records.Count > options.MaxRows)
							break;
					}
					items = null;
				}
				else if (root.ValueKind == JsonValueKind.Array)
				{
					items = root.EnumerateArray();
				}
				else if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Array) == 1)
				{
					// A single wrapped array ({"data": [...]}, the shape most API exports use).
					items = root.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value.EnumerateArray();
				}
				else
				{
					result.ErrorCode = "IMP-PARSE-JSON-SHAPE";
					return;
				}

				if (items != null)
				{
					foreach (var item in items)
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (item.ValueKind != JsonValueKind.Object)
						{
							result.ErrorCode = "IMP-PARSE-JSON-SHAPE";
							result.ErrorRow = records.Count + 2;
							return;
						}

						Add(Flatten(item));
						if (records.Count > options.MaxRows)
							break;
					}
				}
			}

			if (headers.Count > options.MaxColumns)
			{
				result.ErrorCode = "IMP-FILE-TOO-WIDE";
				return;
			}

			// The header row is synthesized from the union of keys in first-seen order, then one row per record.
			if (!Accept(1, headers.ToArray(), options, onRow, result, clock))
				return;

			var row = 1;
			foreach (var record in records)
			{
				row++;
				var cells = headers.Select(h => record.TryGetValue(h, out var v) ? v : string.Empty).ToArray();
				if (!Accept(row, cells, options, onRow, result, clock))
					return;
			}
		}

		private static JsonDocument ParseLine(string line, JsonDocumentOptions options, int lineNumber)
		{
			try
			{
				return JsonDocument.Parse(line, options);
			}
			catch (JsonException)
			{
				// Re-thrown with the line number only: the exception message could quote the content.
				throw new JsonException("invalid", null, lineNumber - 1, null);
			}
		}

		private static Dictionary<string, string> Flatten(JsonElement element)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			FlattenInto(element, null, result, 0);
			return result;
		}

		private static Dictionary<string, string> FlattenFeature(JsonElement feature)
		{
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			if (feature.TryGetProperty("id", out var id))
				result["id"] = Scalar(id);

			if (feature.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
				FlattenInto(properties, null, result, 0);

			if (feature.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Object)
			{
				var type = geometry.TryGetProperty("type", out var t) ? t.GetString() : null;
				if (type == "Point" && geometry.TryGetProperty("coordinates", out var coordinates) && coordinates.ValueKind == JsonValueKind.Array &&
					coordinates.GetArrayLength() >= 2)
				{
					// GeoJSON order is longitude, latitude.
					result["longitude"] = coordinates[0].GetRawText();
					result["latitude"] = coordinates[1].GetRawText();
				}
				else
				{
					result["geometry"] = geometry.GetRawText();
				}
			}

			return result;
		}

		private static void FlattenInto(JsonElement element, string prefix, Dictionary<string, string> into, int depth)
		{
			if (element.ValueKind == JsonValueKind.Object && depth < 4)
			{
				foreach (var property in element.EnumerateObject())
				{
					var key = prefix == null ? property.Name : prefix + "." + property.Name;
					if (property.Value.ValueKind == JsonValueKind.Object)
						FlattenInto(property.Value, key, into, depth + 1);
					else
						into[key] = Scalar(property.Value);
				}
				return;
			}

			into[prefix ?? "value"] = Scalar(element);
		}

		private static string Scalar(JsonElement value)
		{
			switch (value.ValueKind)
			{
				case JsonValueKind.String:
					return value.GetString() ?? string.Empty;
				case JsonValueKind.Number:
					return value.GetRawText();
				case JsonValueKind.True:
					return "TRUE";
				case JsonValueKind.False:
					return "FALSE";
				case JsonValueKind.Null:
				case JsonValueKind.Undefined:
					return string.Empty;
				case JsonValueKind.Array:
					// Arrays of scalars become a "; " list (multi-value fields); anything deeper stays as JSON text.
					var items = value.EnumerateArray().ToList();
					return items.All(i => i.ValueKind != JsonValueKind.Object && i.ValueKind != JsonValueKind.Array)
						? string.Join("; ", items.Select(Scalar))
						: value.GetRawText();
				default:
					return value.GetRawText();
			}
		}

		// ---- shared --------------------------------------------------------------------------------------------

		private static bool Accept(int rowNumber, string[] cells, ImportReadOptions options, Action<int, string[]> onRow, ImportSheetReadResult result, Stopwatch clock)
		{
			if (clock.Elapsed > options.TimeBudget)
			{
				result.ErrorCode = "IMP-PARSE-TIMEOUT";
				result.ErrorRow = rowNumber;
				return false;
			}

			if (cells.Length > options.MaxColumns)
			{
				result.ErrorCode = "IMP-FILE-TOO-WIDE";
				result.ErrorRow = rowNumber;
				return false;
			}

			foreach (var cell in cells)
			{
				if (cell != null && cell.Length > options.MaxFieldLength)
				{
					result.ErrorCode = "IMP-FILE-FIELD-TOO-LONG";
					result.ErrorRow = rowNumber;
					return false;
				}
			}

			// The header row does not count toward the data-row limit.
			if (result.RowsRead > options.MaxRows)
			{
				result.Truncated = true;
				return false;
			}

			result.RowsRead++;
			onRow(rowNumber, cells);
			return true;
		}

		private static ImportFileInspection InspectZip(Stream content, string extension)
		{
			try
			{
				content.Position = 0;
				using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
				var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/').ToLowerInvariant()).ToList();

				// ZIP guards (section 14.1): entry count, total expansion ratio, traversal and absolute paths.
				if (archive.Entries.Count > Config.ImportConfig.MaxZipEntries)
					return Reject("IMP-ZIP-ENTRIES");
				if (names.Any(n => n.StartsWith("/", StringComparison.Ordinal) || n.Contains("../") || n.Contains(":")))
					return Reject("IMP-ZIP-PATH");

				long compressed = 0, expanded = 0;
				foreach (var entry in archive.Entries)
				{
					compressed += entry.CompressedLength;
					expanded += entry.Length;
				}
				if (compressed > 0 && expanded / Math.Max(1, compressed) > Config.ImportConfig.MaxZipCompressionRatio)
					return Reject("IMP-ZIP-RATIO");
				if (expanded > Config.ImportConfig.PaidPlanMaxPackageBytes)
					return Reject("IMP-ZIP-TOO-LARGE");

				if (names.Contains("[content_types].xml") && names.Contains("xl/workbook.xml"))
				{
					if (names.Any(n => n.EndsWith("vbaproject.bin", StringComparison.Ordinal)))
						return Reject("IMP-FILE-MACRO");
					if (names.Any(n => n.StartsWith("xl/externallinks/", StringComparison.Ordinal)))
						return Reject("IMP-FILE-EXTERNAL-LINKS");
					if (names.Any(n => n.StartsWith("xl/embeddings/", StringComparison.Ordinal) || n.StartsWith("xl/activex/", StringComparison.Ordinal)))
						return Reject("IMP-FILE-EMBEDDED-OBJECTS");

					return new ImportFileInspection { Format = ImportFileFormat.Xlsx, RequiresScanning = true };
				}

				if (names.Contains("xl/workbook.bin"))
					return Reject("IMP-FILE-SIGNATURE");

				if (names.Any(n => n.EndsWith(".kml", StringComparison.Ordinal)) && extension == ".kmz")
					return new ImportFileInspection { Format = ImportFileFormat.Kmz, RequiresScanning = true };

				// A package may hold files, never further archives (nesting depth 1).
				if (names.Any(n => n.EndsWith(".zip", StringComparison.Ordinal) || n.EndsWith(".kmz", StringComparison.Ordinal) ||
					n.EndsWith(".7z", StringComparison.Ordinal) || n.EndsWith(".rar", StringComparison.Ordinal)))
					return Reject("IMP-ZIP-NESTED");

				return new ImportFileInspection { Format = ImportFileFormat.Zip, RequiresScanning = true };
			}
			catch (InvalidDataException)
			{
				return Reject("IMP-FILE-SIGNATURE");
			}
			finally
			{
				content.Position = 0;
			}
		}

		private static string SniffDelimiter(string text, string extension)
		{
			if (extension == ".tsv" || extension == ".tab")
				return "\t";

			var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(20).ToList();
			if (lines.Count == 0)
				return ",";

			// The delimiter whose per-line count (outside quotes) is the highest and most consistent wins.
			var best = ',';
			var bestScore = -1.0;
			foreach (var candidate in CandidateDelimiters)
			{
				var counts = lines.Select(l => CountOutsideQuotes(l, candidate)).ToList();
				if (counts[0] == 0)
					continue;

				var consistent = counts.Count(c => c == counts[0]);
				var score = counts[0] * (consistent / (double)counts.Count);
				if (score > bestScore)
				{
					bestScore = score;
					best = candidate;
				}
			}

			return best.ToString();
		}

		private static int CountOutsideQuotes(string line, char delimiter)
		{
			var count = 0;
			var inQuotes = false;
			foreach (var c in line)
			{
				if (c == '"') inQuotes = !inQuotes;
				else if (c == delimiter && !inQuotes) count++;
			}
			return count;
		}

		private static string DecodeHead(byte[] head, out string encoding)
		{
			if (head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
			{
				encoding = "utf-16";
				return (head[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(head, 2, head.Length - 2);
			}

			var start = head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
			var end = head.Length;
			if (head.Length == HeadLength)
			{
				// The head is a prefix of a longer file and may end inside a multi-byte character: cut back to the last
				// complete sequence before validating.
				var i = end - 1;
				var back = 0;
				while (i >= start && back < 3 && (head[i] & 0xC0) == 0x80) { i--; back++; }
				if (i >= start && head[i] >= 0xC0)
				{
					var need = head[i] >= 0xF0 ? 4 : head[i] >= 0xE0 ? 3 : 2;
					if (end - i < need)
						end = i;
				}
			}
			var length = end - start;

			try
			{
				encoding = "utf-8";
				return new UTF8Encoding(false, true).GetString(head, start, length);
			}
			catch (DecoderFallbackException)
			{
				encoding = null;
				return null;
			}
		}

		private static ImportFileFormat GuessTextFormat(string extension) =>
			extension == ".tsv" || extension == ".tab" ? ImportFileFormat.Tsv
			: extension == ".json" ? ImportFileFormat.Json
			: extension == ".jsonl" || extension == ".ndjson" ? ImportFileFormat.JsonLines
			: ImportFileFormat.Csv;

		private static byte[] ReadHead(Stream content, int max)
		{
			content.Position = 0;
			var buffer = new byte[max];
			var read = 0;
			int n;
			while (read < max && (n = content.Read(buffer, read, max - read)) > 0)
				read += n;
			content.Position = 0;
			return read == max ? buffer : buffer.Take(read).ToArray();
		}

		private static bool StartsWith(byte[] data, byte[] prefix) => data.Length >= prefix.Length && !prefix.Where((b, i) => data[i] != b).Any();

		private static ImportFileInspection Reject(string code) => new ImportFileInspection { Format = ImportFileFormat.Unknown, RejectionCode = code };
	}
}
