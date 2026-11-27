using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// Reads the training history import file (R-C1). Columns are matched by name in any order (spaces, punctuation and case
	/// ignored, with common aliases such as "Employee ID" or "Completion Date"); unknown columns are reported and ignored.
	/// Course, a completion date, hours and one member column are required. A semicolon-delimited file (what Excel writes in
	/// comma-decimal locales) reads "1,5" as 1.5; a comma or tab file reads "1.5". Rows are numbered as a spreadsheet shows them
	/// (the header is row 1). Problems are resource keys: file-level ones in <see cref="TrainingHistoryImportParse.FileError"/>,
	/// row-level ones in the row's Errors and Warnings.
	/// </summary>
	public static class TrainingHistoryImportCsv
	{
		public const int MaxFileBytes = 5 * 1024 * 1024;
		public const int MaxRows = 10000;
		public const decimal MaxHours = 999m;
		public const int MaxTextLength = 4000;

		public static class Columns
		{
			public const string Email = "Email", MemberId = "MemberId", MemberExternalId = "MemberExternalId", MemberName = "MemberName", Course = "Course",
				CourseCode = "CourseCode", CompletedOn = "CompletedOn", Hours = "Hours", Category = "Category", CeProvider = "CeProvider",
				CeCourseNumber = "CeCourseNumber", CeHours = "CeHours", Instructor = "Instructor", Method = "Method", RecordId = "RecordId", Notes = "Notes",
				CertificationType = "CertificationType", CertificationNumber = "CertificationNumber", CertificationIssuedOn = "CertificationIssuedOn",
				CertificationExpiresOn = "CertificationExpiresOn";
		}

		/// <summary>Resource keys (Trainings resx) of the problems the parser and the import report.</summary>
		public static class Keys
		{
			public const string FileInvalid = "Import.File.Invalid", FileTooLarge = "Import.File.TooLarge", FileCourseColumnRequired = "Import.File.CourseColumnRequired",
				FileDateColumnRequired = "Import.File.DateColumnRequired", FileHoursColumnRequired = "Import.File.HoursColumnRequired",
				FileMemberColumnRequired = "Import.File.MemberColumnRequired", FileTooManyRows = "Import.File.TooManyRows", FileDuplicateColumn = "Import.File.DuplicateColumn",
				FileEmpty = "Import.File.Empty";

			public const string CourseRequired = "Import.Err.CourseRequired", DateRequired = "Import.Err.DateRequired", DateInvalid = "Import.Err.DateInvalid",
				HoursInvalid = "Import.Err.HoursInvalid", MemberNotFound = "Import.Err.MemberNotFound", Unreadable = "Import.Err.Unreadable",
				FutureDate = "Import.Err.FutureDate";

			public const string NameMatch = "Import.Warn.NameMatch", UnknownCategory = "Import.Warn.UnknownCategory", UnknownCertification = "Import.Warn.UnknownCertification",
				CeHoursInvalid = "Import.Warn.CeHoursInvalid", DuplicateRow = "Import.Warn.DuplicateRow", UnknownMethod = "Import.Warn.UnknownMethod",
				CertificationDateInvalid = "Import.Warn.CertificationDateInvalid";
		}

		/// <summary>Stable interchange keys, in template order.</summary>
		public static readonly IReadOnlyList<string> Header = new[]
		{
			Columns.Email, Columns.MemberId, Columns.MemberExternalId, Columns.MemberName, Columns.Course, Columns.CourseCode, Columns.CompletedOn, Columns.Hours,
			Columns.Category, Columns.CeProvider, Columns.CeCourseNumber, Columns.CeHours, Columns.Instructor, Columns.Method, Columns.RecordId, Columns.Notes,
			Columns.CertificationType, Columns.CertificationNumber, Columns.CertificationIssuedOn, Columns.CertificationExpiresOn
		};

		private static readonly Dictionary<string, string> Aliases = BuildAliases();

		private static Dictionary<string, string> BuildAliases()
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			void Add(string column, params string[] names) { map[Normalize(column)] = column; foreach (var name in names) map[Normalize(name)] = column; }
			Add(Columns.Email, "E-mail", "EmailAddress");
			Add(Columns.MemberId, "UserId", "ResgridId");
			Add(Columns.MemberExternalId, "EmployeeId", "EmployeeNumber", "PersonnelId", "IdNumber", "BadgeNumber", "ExternalId");
			Add(Columns.MemberName, "Name", "Member", "FullName");
			Add(Columns.Course, "CourseName", "Training", "Title", "Class");
			Add(Columns.CourseCode, "Code", "CourseNumber");
			Add(Columns.CompletedOn, "Date", "CompletionDate", "Completed");
			Add(Columns.Hours, "Duration", "CreditHours");
			Add(Columns.Category, "CategoryCode", "TrainingCategory");
			Add(Columns.CeProvider, "Provider");
			Add(Columns.CeCourseNumber, "CeNumber", "CapceNumber");
			Add(Columns.CeHours, "CeuHours", "ContinuingEducationHours");
			Add(Columns.Instructor, "Instructors", "Trainer");
			Add(Columns.Method, "MethodOfTraining", "Delivery");
			Add(Columns.RecordId, "ExternalRecordId", "CompletionId");
			Add(Columns.Notes);
			Add(Columns.CertificationType, "Certification", "CertificationCode");
			Add(Columns.CertificationNumber);
			Add(Columns.CertificationIssuedOn, "IssuedOn", "CertifiedOn");
			Add(Columns.CertificationExpiresOn, "ExpiresOn", "Expiration");
			return map;
		}

		private static string Normalize(string header)
			=> new string((header ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

		/// <summary>The canonical column a file header maps to, or null when the header is not an import column.</summary>
		public static string ColumnFor(string header) => Aliases.TryGetValue(Normalize(header), out var column) ? column : null;

		/// <summary>The template: the header in column order and one example row, UTF-8 with a byte order mark.</summary>
		public static byte[] Template()
		{
			var text = new StringBuilder();
			text.Append(CsvCell.Line(Header)).Append("\r\n");
			text.Append(CsvCell.Line(new[]
			{
				"jane.doe@example.com", "", "E1234", "Jane Doe", "Vehicle extrication", "FF-EXT-1", "2025-03-14", "3.5", "ISO-CO", "State Fire Academy", "CE-2025-117",
				"3.5", "Capt. Lee", "Drill/Field", "LMS-88412", "Annual refresher", "EMT", "E-556677", "2024-06-01", "2026-06-01"
			})).Append("\r\n");
			return InventoryImportCsv.Encode(text.ToString());
		}

		/// <summary>Parses the uploaded bytes; <paramref name="culture"/> reads non-ISO dates (the uploader's own format).</summary>
		public static TrainingHistoryImportParse Parse(byte[] bytes, CultureInfo culture = null)
		{
			if (bytes == null || bytes.Length == 0)
				return TrainingHistoryImportParse.Fail(Keys.FileInvalid);
			if (bytes.Length > MaxFileBytes)
				return TrainingHistoryImportParse.Fail(Keys.FileTooLarge);
			return Parse(InventoryImportCsv.Decode(bytes), culture);
		}

		public static TrainingHistoryImportParse Parse(string text, CultureInfo culture = null)
		{
			if (string.IsNullOrWhiteSpace(text))
				return TrainingHistoryImportParse.Fail(Keys.FileInvalid);
			if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
				return TrainingHistoryImportParse.Fail(Keys.FileTooLarge);

			text = text.TrimStart('﻿');
			var delimiter = Delimiter(text);
			var numbers = delimiter == ';' ? (Decimal: ',', Group: '.') : (Decimal: '.', Group: ',');
			culture ??= CultureInfo.InvariantCulture;

			using var records = Records(text, delimiter).GetEnumerator();
			if (!records.MoveNext() || records.Current.Malformed)
				return TrainingHistoryImportParse.Fail(Keys.FileInvalid);

			var headers = records.Current.Fields;
			var result = new TrainingHistoryImportParse();
			var columns = new string[headers.Count];
			var seen = new HashSet<string>(StringComparer.Ordinal);
			for (var i = 0; i < headers.Count; i++)
			{
				var header = Clean(headers[i]);
				if (string.IsNullOrEmpty(header))
					continue;
				var column = ColumnFor(header);
				if (column == null)
				{
					var ignored = header.Length > 80 ? header.Substring(0, 80) : header;
					if (!result.IgnoredColumns.Contains(ignored, StringComparer.OrdinalIgnoreCase))
						result.IgnoredColumns.Add(ignored);
					continue;
				}
				if (!seen.Add(column))
					return TrainingHistoryImportParse.Fail(Keys.FileDuplicateColumn, result.IgnoredColumns);
				columns[i] = column;
			}

			if (!seen.Contains(Columns.Course))
				return TrainingHistoryImportParse.Fail(Keys.FileCourseColumnRequired, result.IgnoredColumns);
			if (!seen.Contains(Columns.CompletedOn))
				return TrainingHistoryImportParse.Fail(Keys.FileDateColumnRequired, result.IgnoredColumns);
			if (!seen.Contains(Columns.Hours))
				return TrainingHistoryImportParse.Fail(Keys.FileHoursColumnRequired, result.IgnoredColumns);
			if (!seen.Contains(Columns.Email) && !seen.Contains(Columns.MemberId) && !seen.Contains(Columns.MemberExternalId) && !seen.Contains(Columns.MemberName))
				return TrainingHistoryImportParse.Fail(Keys.FileMemberColumnRequired, result.IgnoredColumns);

			// Rows are numbered as a spreadsheet shows them: the header is row 1 and blank rows still count.
			var rowNumber = 1;
			while (records.MoveNext())
			{
				rowNumber++;
				var (fields, malformed) = records.Current;
				if (!malformed && fields.All(string.IsNullOrWhiteSpace))
					continue;

				var row = new TrainingHistoryImportRow { RowNumber = rowNumber };
				result.Rows.Add(row);
				if (result.Rows.Count > MaxRows)
					return TrainingHistoryImportParse.Fail(Keys.FileTooManyRows, result.IgnoredColumns);

				if (malformed || fields.Count > columns.Length && fields.Skip(columns.Length).Any(f => !string.IsNullOrWhiteSpace(f)))
				{
					row.Errors.Add(Keys.Unreadable);
					continue;
				}

				for (var i = 0; i < columns.Length && i < fields.Count; i++)
				{
					if (columns[i] == null)
						continue;
					var value = Clean(fields[i]);
					if (value == null)
						continue;
					if (value.Length > MaxTextLength)
						value = value.Substring(0, MaxTextLength);
					Assign(row, columns[i], value, numbers, culture);
				}

				if (string.IsNullOrWhiteSpace(row.Course))
					AddOnce(row.Errors, Keys.CourseRequired);
				if (!row.CompletedOn.HasValue && !row.Errors.Contains(Keys.DateInvalid))
					AddOnce(row.Errors, Keys.DateRequired);
				if (!row.Hours.HasValue)
					AddOnce(row.Errors, Keys.HoursInvalid);
			}

			if (result.Rows.Count == 0)
				return TrainingHistoryImportParse.Fail(Keys.FileEmpty, result.IgnoredColumns);
			return result;
		}

		private static void Assign(TrainingHistoryImportRow row, string column, string value, (char Decimal, char Group) numbers, CultureInfo culture)
		{
			switch (column)
			{
				case Columns.Email: row.Email = value; break;
				case Columns.MemberId: row.MemberId = value; break;
				case Columns.MemberExternalId: row.MemberExternalId = value; break;
				case Columns.MemberName: row.MemberName = value; break;
				case Columns.Course: row.Course = value; break;
				case Columns.CourseCode: row.CourseCode = value; break;
				case Columns.CompletedOn:
					if (TryDateTime(value, culture, out var completed))
						row.CompletedOn = completed;
					else
						AddOnce(row.Errors, Keys.DateInvalid);
					break;
				case Columns.Hours:
					if (InventoryImportCsv.TryNumber(value, numbers.Decimal, numbers.Group, out var hours) && hours >= 0 && hours <= MaxHours)
						row.Hours = Math.Round(hours, 2, MidpointRounding.AwayFromZero);
					else
						AddOnce(row.Errors, Keys.HoursInvalid);
					break;
				case Columns.Category: row.Category = value; break;
				case Columns.CeProvider: row.CeProvider = value; break;
				case Columns.CeCourseNumber: row.CeCourseNumber = value; break;
				case Columns.CeHours:
					if (InventoryImportCsv.TryNumber(value, numbers.Decimal, numbers.Group, out var ce) && ce >= 0 && ce <= MaxHours)
						row.CeHours = Math.Round(ce, 2, MidpointRounding.AwayFromZero);
					else
						AddOnce(row.Warnings, Keys.CeHoursInvalid);
					break;
				case Columns.Instructor: row.Instructor = value; break;
				case Columns.Method: row.Method = value; break;
				case Columns.RecordId: row.RecordId = value; break;
				case Columns.Notes: row.Notes = value; break;
				case Columns.CertificationType: row.CertificationType = value; break;
				case Columns.CertificationNumber: row.CertificationNumber = value; break;
				case Columns.CertificationIssuedOn:
					if (TryDateTime(value, culture, out var issued))
						row.CertificationIssuedOn = issued.Date;
					else
						AddOnce(row.Warnings, Keys.CertificationDateInvalid);
					break;
				case Columns.CertificationExpiresOn:
					if (TryDateTime(value, culture, out var expires))
						row.CertificationExpiresOn = expires.Date;
					else
						AddOnce(row.Warnings, Keys.CertificationDateInvalid);
					break;
			}
		}

		private static void AddOnce(List<string> list, string key)
		{
			if (!list.Contains(key))
				list.Add(key);
		}

		/// <summary>
		/// The method column: a <see cref="MethodsOfTraining"/> name or number, or a common word for one ("classroom", "online",
		/// "drill", "field", "virtual", "self-study", "on the job"); null when it is none of them.
		/// </summary>
		public static MethodsOfTraining? MethodFor(string value)
		{
			var text = Normalize(value);
			if (text.Length == 0)
				return null;
			if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
				return Enum.IsDefined(typeof(MethodsOfTraining), number) ? (MethodsOfTraining)number : (MethodsOfTraining?)null;
			foreach (MethodsOfTraining method in Enum.GetValues(typeof(MethodsOfTraining)))
				if (Normalize(method.ToString()) == text)
					return method;
			switch (text)
			{
				case "class": case "classroom": case "lecture": case "inperson": return MethodsOfTraining.Classroom;
				case "online": case "elearning": case "web": case "webbased": return MethodsOfTraining.Online;
				case "drill": case "field": case "drillground": case "practical": case "handson": return MethodsOfTraining.DrillField;
				case "virtual": case "virtualclassroom": case "webinar": return MethodsOfTraining.Virtual;
				case "self": case "selfstudy": case "selfpaced": case "reading": return MethodsOfTraining.SelfStudy;
				case "onthejob": case "ojt": case "job": return MethodsOfTraining.OnTheJob;
				default: return null;
			}
		}

		private static readonly string[] DateOnlyFormats = { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyyMMdd" };

		private static readonly string[] DateTimeFormats =
		{
			"yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm'Z'",
			"yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd HH:mm:sszzz"
		};

		/// <summary>
		/// A completion date: an ISO date is that day at 12:00 UTC (it then reads as the same date in every department zone); an
		/// ISO date and time is that UTC instant (an offset is honoured); otherwise the uploader's own date format, which must
		/// name the whole date (the culture parser would fill a missing year from today). Years 1900–2200 only.
		/// </summary>
		public static bool TryDateTime(string value, CultureInfo culture, out DateTime result)
		{
			result = default;
			if (string.IsNullOrWhiteSpace(value))
				return false;
			value = value.Trim();

			if (DateTime.TryParseExact(value, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
				return Accept(Noon(date), out result);

			if (DateTime.TryParseExact(value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant))
				return Accept(DateTime.SpecifyKind(instant, DateTimeKind.Utc), out result);

			if (culture != null && DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out var local) && NamesYear(value, local.Year))
				return Accept(local.TimeOfDay == TimeSpan.Zero ? Noon(local) : DateTime.SpecifyKind(local, DateTimeKind.Utc), out result);

			return false;
		}

		private static DateTime Noon(DateTime date) => DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc);

		private static bool Accept(DateTime value, out DateTime result)
		{
			result = value;
			return value.Year is >= 1900 and <= 2200;
		}

		/// <summary>Whether the text holds the year as a number of its own (four digits, or the last two).</summary>
		private static bool NamesYear(string value, int year)
		{
			var number = 0; var digits = 0;
			bool Matches() => digits == 4 && number == year || digits == 2 && number == year % 100;
			foreach (var c in value + " ")
			{
				if (char.IsDigit(c)) { if (digits < 5) number = number * 10 + (int)char.GetNumericValue(c); digits++; continue; }
				if (digits > 0 && Matches()) return true;
				number = 0; digits = 0;
			}
			return false;
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
		/// that does not open a value is kept as text. A quote left open at the end of the file makes that last record
		/// unreadable, and so does text after a quoted value closes: only spaces may follow the closing quote.
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

		/// <summary>Trims, blanks to null, and undoes a spreadsheet formula guard (a leading apostrophe).</summary>
		private static string Clean(string value)
		{
			value = value?.Trim();
			if (string.IsNullOrEmpty(value)) return null;
			if (value.Length > 1 && value[0] == '\'' && "=+-@\t\r".IndexOf(value[1]) >= 0) value = value.Substring(1);
			return value;
		}
	}

	/// <summary>What <see cref="TrainingHistoryImportCsv.Parse(byte[], CultureInfo)"/> read: the rows, or a file-level problem.</summary>
	public sealed class TrainingHistoryImportParse
	{
		public string FileError { get; set; }
		public List<string> IgnoredColumns { get; set; } = new List<string>();
		public List<TrainingHistoryImportRow> Rows { get; set; } = new List<TrainingHistoryImportRow>();

		public static TrainingHistoryImportParse Fail(string key, List<string> ignoredColumns = null) =>
			new TrainingHistoryImportParse { FileError = key, IgnoredColumns = ignoredColumns ?? new List<string>() };
	}
}
