using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Resgrid.Framework;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	/// <summary>
	/// Reads and writes the inventory import file. Columns are matched by name in any order (spaces, underscores and case
	/// ignored, with common aliases such as "Qty" or "SKU"); only <c>Item</c> is required. Unknown columns are reported and
	/// ignored. A semicolon-delimited file (what Excel writes in comma-decimal locales) reads "1.234,5" as 1234.5; a comma or
	/// tab file reads "1,234.5". A number that fits neither pattern is refused rather than guessed.
	/// </summary>
	public static class InventoryImportCsv
	{
		public static class Columns
		{
			public const string Item = "Item", Code = "Code", Barcode = "Barcode", Category = "Category", Description = "Description", UnitOfMeasure = "UnitOfMeasure",
				Tracking = "Tracking", RequiresLot = "RequiresLot", RequiresExpiration = "RequiresExpiration", UnitCost = "UnitCost", Currency = "Currency",
				MinLevel = "MinLevel", MaxLevel = "MaxLevel", ReorderPoint = "ReorderPoint", ReorderQuantity = "ReorderQuantity", Location = "Location",
				Station = "Station", Unit = "Unit", Quantity = "Quantity", ParLevel = "ParLevel", Lot = "Lot", Expires = "Expires", SerialNumber = "SerialNumber",
				AssetTag = "AssetTag", Note = "Note";
		}

		/// <summary>Stable interchange keys, in template order. Labels and help are localized by the UI.</summary>
		public static readonly IReadOnlyList<string> Header = new[]
		{
			Columns.Item, Columns.Code, Columns.Barcode, Columns.Category, Columns.Description, Columns.UnitOfMeasure, Columns.Tracking, Columns.RequiresLot,
			Columns.RequiresExpiration, Columns.UnitCost, Columns.Currency, Columns.MinLevel, Columns.MaxLevel, Columns.ReorderPoint, Columns.ReorderQuantity,
			Columns.Location, Columns.Station, Columns.Unit, Columns.Quantity, Columns.ParLevel, Columns.Lot, Columns.Expires, Columns.SerialNumber, Columns.AssetTag, Columns.Note
		};

		private static readonly Dictionary<string, string> Aliases = BuildAliases();

		private static Dictionary<string, string> BuildAliases()
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			void Add(string column, params string[] names) { map[Normalize(column)] = column; foreach (var name in names) map[Normalize(name)] = column; }
			Add(Columns.Item, "ItemName", "Name", "ItemType", "InventoryType", "Type");
			Add(Columns.Code, "ItemCode", "SKU", "PartNumber", "Part");
			Add(Columns.Barcode, "UPC", "EAN");
			Add(Columns.Category, "CategoryPath");
			Add(Columns.Description, "Desc");
			Add(Columns.UnitOfMeasure, "UOM", "Measure");
			Add(Columns.Tracking, "TrackingMode");
			Add(Columns.RequiresLot, "RequiresLotTracking", "LotTracking", "LotTracked");
			Add(Columns.RequiresExpiration, "RequiresExpiry", "TracksExpiration", "Expiring");
			Add(Columns.UnitCost, "Cost", "Price", "UnitPrice");
			Add(Columns.Currency, "CurrencyCode");
			Add(Columns.MinLevel, "Minimum", "Min");
			Add(Columns.MaxLevel, "Maximum", "Max");
			Add(Columns.ReorderPoint, "ReorderAt");
			Add(Columns.ReorderQuantity, "ReorderQty");
			Add(Columns.Location, "LocationName", "StorageLocation", "Storage");
			Add(Columns.Station, "StationName");
			Add(Columns.Unit, "UnitName", "Apparatus", "Vehicle");
			Add(Columns.Quantity, "Qty", "OnHand", "QuantityOnHand", "Count", "Amount");
			Add(Columns.ParLevel, "Par", "LocationMinimum");
			Add(Columns.Lot, "LotNumber", "Batch", "BatchNumber");
			Add(Columns.Expires, "Expiration", "ExpirationDate", "Expiry", "ExpiryDate", "ExpiresOn");
			Add(Columns.SerialNumber, "Serial", "SN");
			Add(Columns.AssetTag, "Tag");
			Add(Columns.Note, "Notes");
			return map;
		}

		private static string Normalize(string header)
			=> new string((header ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

		/// <summary>The canonical column a file header maps to, or null when the header is not an import column.</summary>
		public static string ColumnFor(string header) => Aliases.TryGetValue(Normalize(header), out var column) ? column : null;

		public static string Template() => CsvCell.Line(Header) + "\r\n";

		/// <summary>
		/// The uploaded bytes as text: a byte order mark decides (Excel's "Unicode text" is UTF-16), else strict UTF-8, else
		/// Latin-1, which is what a spreadsheet saved as plain "CSV" on Windows mostly is.
		/// </summary>
		public static string Decode(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0) return null;
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
			var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
			try { return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
			catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
		}

		/// <summary>UTF-8 with a byte order mark, so a spreadsheet opens accented names correctly.</summary>
		public static byte[] Encode(string text) => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text ?? "")).ToArray();

		public static InventoryImportInput Parse(string text, string requestId, CultureInfo culture = null)
		{
			if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > InventoryImportLimits.MaxFileBytes) throw new InventoryException(400, "ImportFileInvalid");
			text = text.TrimStart('﻿');
			var delimiter = Delimiter(text);
			var numbers = delimiter == ';' ? (Decimal: ',', Group: '.') : (Decimal: '.', Group: ',');
			using var records = Records(text, delimiter).GetEnumerator();
			if (!records.MoveNext() || records.Current.Malformed) throw new InventoryException(400, "ImportFileInvalid");
			var headers = records.Current.Fields;
			var result = new InventoryImportInput { RequestId = requestId };
			var columns = new string[headers.Count];
			var seen = new HashSet<string>(StringComparer.Ordinal);
			for (var i = 0; i < headers.Count; i++)
			{
				var header = Clean(headers[i]);
				if (string.IsNullOrEmpty(header)) continue;
				var column = ColumnFor(header);
				if (column == null) { result.IgnoredColumns.Add(header.Length > 80 ? header[..80] : header); continue; }
				if (!seen.Add(column)) throw new InventoryException(400, "ImportDuplicateColumn");
				columns[i] = column;
			}
			if (!seen.Contains(Columns.Item)) throw new InventoryException(400, "ImportItemColumnRequired");
			// Rows are numbered as a spreadsheet shows them: the header is row 1 and blank rows still count.
			var rowNumber = 1;
			while (records.MoveNext())
			{
				rowNumber++;
				var (fields, malformed) = records.Current;
				if (malformed)
				{
					Add(result, new InventoryImportRow { RowNumber = rowNumber, ParseError = "ImportRowUnreadable" });
					continue;
				}
				if (fields.All(f => string.IsNullOrWhiteSpace(f))) continue;
				var row = new InventoryImportRow { RowNumber = rowNumber };
				Add(result, row);
				if (fields.Count > columns.Length && fields.Skip(columns.Length).Any(f => !string.IsNullOrWhiteSpace(f))) { row.ParseError = "ImportRowColumns"; continue; }
				for (var i = 0; i < columns.Length && i < fields.Count && row.ParseError == null; i++)
				{
					if (columns[i] == null) continue;
					var value = Clean(fields[i]);
					if (value == null) continue;
					if (value.Length > InventoryImportLimits.MaxTextLength) { row.ParseError = "ImportValueTooLong"; row.ParseColumn = columns[i]; continue; }
					if (!Assign(row, columns[i], value, numbers, culture ?? CultureInfo.InvariantCulture)) row.ParseColumn = columns[i];
				}
			}
			if (result.Rows.Count == 0) throw new InventoryException(400, "ImportFileEmpty");
			return result;
		}

		private static void Add(InventoryImportInput input, InventoryImportRow row)
		{
			input.Rows.Add(row);
			if (input.Rows.Count > InventoryImportLimits.MaxRows) throw new InventoryException(400, "ImportTooManyRows");
		}

		/// <summary>The delimiter of the header line: the most frequent of comma, semicolon and tab outside quotes (comma on a tie).</summary>
		private static char Delimiter(string text)
		{
			int commas = 0, semicolons = 0, tabs = 0; var quoted = false;
			foreach (var c in text)
			{
				if (c == '"') quoted = !quoted;
				else if (!quoted && (c == '\n' || c == '\r')) break;
				else if (!quoted && c == ',') commas++;
				else if (!quoted && c == ';') semicolons++;
				else if (!quoted && c == '\t') tabs++;
			}
			return semicolons > commas && semicolons >= tabs ? ';' : tabs > commas ? '\t' : ',';
		}

		/// <summary>
		/// RFC 4180 records, one per spreadsheet row (a quoted value may span lines; a blank line is an empty record). A quote
		/// that does not open a value is kept as text, so an unquoted inch mark (Gauze 4"x4") reads as written. A quote left
		/// open at the end of the file makes that last record unreadable, and so does text after a quoted value closes
		/// (<c>"12"34</c> is not 1234): only spaces may follow the closing quote before the delimiter or the line end.
		/// </summary>
		private static IEnumerable<(List<string> Fields, bool Malformed)> Records(string text, char delimiter)
		{
			var fields = new List<string>(); var field = new StringBuilder(); var quoted = false; var opened = false; var closed = false; var malformed = false;
			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];
				if (quoted)
				{
					if (c != '"') { field.Append(c); continue; }
					if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; continue; }
					quoted = false; closed = true; continue;
				}
				if (closed && c != delimiter && c != '\r' && c != '\n')
				{
					if (!char.IsWhiteSpace(c)) malformed = true;
					continue;
				}
				if (c == '"' && !opened && field.ToString().Trim().Length == 0) { field.Clear(); quoted = opened = true; continue; }
				if (c == delimiter) { fields.Add(field.ToString()); field.Clear(); opened = closed = false; continue; }
				if (c == '\r' || c == '\n')
				{
					if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
					fields.Add(field.ToString()); field.Clear(); opened = closed = false;
					yield return (fields, malformed); fields = new List<string>(); malformed = false;
					continue;
				}
				field.Append(c);
			}
			if (quoted || field.Length > 0 || fields.Count > 0 || opened)
			{
				fields.Add(field.ToString());
				yield return (fields, quoted || malformed);
			}
		}

		/// <summary>Trims, blanks to null, and undoes the export's spreadsheet formula guard (a leading apostrophe).</summary>
		private static string Clean(string value)
		{
			value = value?.Trim();
			if (string.IsNullOrEmpty(value)) return null;
			if (value.Length > 1 && value[0] == '\'' && "=+-@\t\r".IndexOf(value[1]) >= 0) value = value[1..];
			return value;
		}

		private static bool Assign(InventoryImportRow row, string column, string value, (char Decimal, char Group) numbers, CultureInfo culture)
		{
			bool Fail(string code) { row.ParseError = code; return false; }
			bool Number(Action<decimal> set) { if (!TryNumber(value, numbers.Decimal, numbers.Group, out var n)) return Fail("ImportInvalidNumber"); set(n); return true; }
			bool Flag(Action<bool> set) { if (!TryFlag(value, out var b)) return Fail("ImportInvalidYesNo"); set(b); return true; }
			switch (column)
			{
				case Columns.Item: row.Item = value; return true;
				case Columns.Code: row.Code = value; return true;
				case Columns.Barcode: row.Barcode = value; return true;
				case Columns.Category: row.Category = value; return true;
				case Columns.Description: row.Description = value; return true;
				case Columns.UnitOfMeasure: row.UnitOfMeasure = value; return true;
				case Columns.Tracking:
					switch (Normalize(value))
					{
						case "bulk": case "0": row.Tracking = InventoryTrackingMode.Bulk; return true;
						case "serialized": case "serialised": case "serial": case "1": row.Tracking = InventoryTrackingMode.Serialized; return true;
						default: return Fail("ImportInvalidTracking");
					}
				case Columns.RequiresLot: return Flag(b => row.RequiresLot = b);
				case Columns.RequiresExpiration: return Flag(b => row.RequiresExpiration = b);
				case Columns.UnitCost: return Number(n => row.UnitCost = n);
				case Columns.Currency: row.Currency = value; return true;
				case Columns.MinLevel: return Number(n => row.MinLevel = n);
				case Columns.MaxLevel: return Number(n => row.MaxLevel = n);
				case Columns.ReorderPoint: return Number(n => row.ReorderPoint = n);
				case Columns.ReorderQuantity: return Number(n => row.ReorderQuantity = n);
				case Columns.Location: row.Location = value; return true;
				case Columns.Station: row.Station = value; return true;
				case Columns.Unit: row.Unit = value; return true;
				case Columns.Quantity: return Number(n => row.Quantity = n);
				case Columns.ParLevel: return Number(n => row.ParLevel = n);
				case Columns.Lot: row.Lot = value; return true;
				case Columns.Expires:
					if (!TryDate(value, culture, out var date)) return Fail("ImportInvalidDate");
					row.Expires = date; return true;
				case Columns.SerialNumber: row.SerialNumber = value; return true;
				case Columns.AssetTag: row.AssetTag = value; return true;
				case Columns.Note: row.Note = value; return true;
				default: return true;
			}
		}

		/// <summary>
		/// Parses a number written with the file's separators, falling back to the other convention only when the first does
		/// not fit ("1.5" in a semicolon file). Currency symbols around the number and space thousands separators are dropped.
		/// </summary>
		public static bool TryNumber(string value, char decimalSeparator, char groupSeparator, out decimal result)
		{
			result = 0;
			if (string.IsNullOrWhiteSpace(value)) return false;
			var s = new string(value.Where(c => !char.IsWhiteSpace(c) && c != ' ' && c != ' ').ToArray());
			var start = 0; var end = s.Length;
			while (start < end && !IsNumberChar(s[start])) start++;
			while (end > start && !IsNumberChar(s[end - 1])) end--;
			if (start >= end || s.Take(start).Any(char.IsDigit) || s.Skip(end).Any(char.IsDigit)) return false;
			// Accounting parentheses are a minus sign: "($12.50)" is -12.50, which the cost and quantity checks then refuse.
			var opens = s.Take(start).Count(c => c == '('); var closes = s.Skip(end).Count(c => c == ')');
			if (opens > 1 || closes > 1 || opens != closes) return false;
			var negative = opens == 1;
			s = s[start..end];
			if (negative && (s[0] == '-' || s[0] == '+')) return false;
			if (!TryPattern(s, decimalSeparator, groupSeparator, out result) && !TryPattern(s, groupSeparator, decimalSeparator, out result)) return false;
			if (negative) result = -result;
			return true;
		}

		private static bool IsNumberChar(char c) => char.IsDigit(c) || c is '-' or '+' or '.' or ',';

		private static bool TryPattern(string s, char decimalSeparator, char groupSeparator, out decimal result)
		{
			result = 0;
			var d = Regex.Escape(decimalSeparator.ToString()); var g = Regex.Escape(groupSeparator.ToString());
			if (!Regex.IsMatch(s, "^[+-]?(\\d{1,3}(" + g + "\\d{3})+|\\d+)?(" + d + "\\d+)?$") || !s.Any(char.IsDigit)) return false;
			var normalized = s.Replace(groupSeparator.ToString(), "").Replace(decimalSeparator, '.');
			return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
		}

		public static bool TryFlag(string value, out bool result)
		{
			switch (Normalize(value))
			{
				case "yes": case "y": case "true": case "1": case "x": result = true; return true;
				case "no": case "n": case "false": case "0": result = false; return true;
				default: result = false; return false;
			}
		}

		private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss'Z'", "yyyy-MM-dd HH:mm:ss" };

		/// <summary>
		/// ISO dates first, then the uploader's own date format. The date is kept as midnight UTC, like the lot form. The cell must
		/// carry the whole date: the culture parser would fill a missing date or year from today ("12:00 PM", "5/12"), so the same
		/// file would read differently on another day.
		/// </summary>
		public static bool TryDate(string value, CultureInfo culture, out DateTime result)
		{
			if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
				|| culture != null && DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out date) && NamesYear(value, date.Year))
			{
				result = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
				return date.Year is >= 1900 and <= 2200;
			}
			result = default; return false;
		}

		/// <summary>Whether the text holds the year as a number of its own (four digits, or the last two).</summary>
		private static bool NamesYear(string value, int year)
		{
			var number = 0; var digits = 0;
			bool Matches() => digits == 4 && number == year || digits == 2 && number == year % 100;
			foreach (var c in value + " ")
			{
				if (char.IsDigit(c)) { number = number * 10 + (int)char.GetNumericValue(c); digits++; continue; }
				if (digits > 0 && Matches()) return true;
				number = 0; digits = 0;
			}
			return false;
		}

		public static string Number(decimal? value) => value?.ToString("0.######", CultureInfo.InvariantCulture);

		/// <summary>Stored dates are UTC; one read back from the database is Unspecified and must not be shifted as local time.</summary>
		public static string Date(DateTime? value) => value == null ? null
			: (value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		/// <summary>One file: the header, then each row's cells in <see cref="Header"/> order (formula-guarded).</summary>
		public static string Write(IEnumerable<IReadOnlyDictionary<string, string>> rows)
		{
			var builder = new StringBuilder(Template());
			foreach (var row in rows) builder.Append(CsvCell.Line(Header.Select(column => row.TryGetValue(column, out var value) ? value : null))).Append("\r\n");
			return builder.ToString();
		}
	}
}
