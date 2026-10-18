using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;

namespace Resgrid.Services.Onboarding
{
	/// <summary>Inputs for converting one column (verification ladder L3, wizard plan section 9).</summary>
	public sealed class ImportConversionContext
	{
		/// <summary>The administrator's answer for this column's date order ("dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd").</summary>
		public string DateFormat { get; set; }
		/// <summary>ISO region the phone parser assumes for numbers without a country code (from the department's country).</summary>
		public string PhoneRegion { get; set; }
		public IPhoneNumberProcesserProvider Phones { get; set; }
		/// <summary>The department's time zone (Windows id) for date-times without an offset.</summary>
		public string TimeZoneId { get; set; }
		/// <summary>Value-map decisions for a choice field: normalized source value → choice code ("" = leave empty).</summary>
		public IReadOnlyDictionary<string, string> ChoiceMap { get; set; }
	}

	/// <summary>The result of converting one value: a canonical value, or an issue code (never the value itself).</summary>
	public sealed class ImportConversion
	{
		/// <summary>Canonical string, a list of canonical strings (multi), or null for empty.</summary>
		public object Value { get; set; }
		public string IssueCode { get; set; }
		/// <summary>Value-free parameters for the issue (expected format, limits).</summary>
		public Dictionary<string, string> Parameters { get; set; }
		/// <summary>The issue is about the value's form, so a required field is blocked; otherwise the value is left empty with a warning.</summary>
		public bool Invalid => IssueCode != null;

		public static ImportConversion Empty() => new ImportConversion();
		public static ImportConversion Ok(object value) => new ImportConversion { Value = value };
		public static ImportConversion Fail(string code, Dictionary<string, string> parameters = null) =>
			new ImportConversion { IssueCode = code, Parameters = parameters };
	}

	/// <summary>
	/// Converts raw cell text to the canonical value of a contract field type (plan sections 8.3 and 9, L3). Conversions
	/// are deterministic and culture-invariant, so the same file, map and catalog give the same issues — which is what makes
	/// the approval digest meaningful. Canonical forms: text trimmed; integer/decimal invariant; boolean "true"/"false";
	/// date "yyyy-MM-dd"; date-time ISO UTC; email lower-case; phone E.164; choice the code; colour "#rrggbb".
	/// </summary>
	public static class ImportFieldConverter
	{
		private static readonly Regex EmailPattern = new Regex(@"^[^@\s""<>()\[\],;:]+@[^@\s""<>()\[\],;:]+\.[^@\s""<>()\[\],;:]{2,}$", RegexOptions.CultureInvariant);
		private static readonly Regex HexColour = new Regex(@"^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.CultureInvariant);
		private static readonly string[] Languages = { "en", "es", "sv", "de", "fr", "it", "pl", "uk", "ar", "el" };

		private static readonly HashSet<string> TrueTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"true", "yes", "y", "1", "x", "on", "t", "active", "enabled", "ja", "oui", "si", "sí", "tak", "так", "ναι", "نعم", "sì"
		};

		private static readonly HashSet<string> FalseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"false", "no", "n", "0", "off", "f", "inactive", "disabled", "nein", "non", "nie", "nej", "ні", "όχι", "لا", "-"
		};

		public static readonly string[] DateFormats = { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd.MM.yyyy", "dd-MM-yyyy", "MM-dd-yyyy", "yyyy/MM/dd" };

		public static ImportConversion Convert(ImportFieldContract field, string raw, ImportConversionContext context)
		{
			var text = Clean(raw);
			if (string.IsNullOrEmpty(text))
				return ImportConversion.Empty();

			if (field.Type == ImportFieldTypes.Multi)
			{
				var separator = string.IsNullOrEmpty(field.Separator) ? ";" : field.Separator;
				var parts = text.Split(new[] { separator, "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
				// A source that used commas where the contract expects semicolons is common enough to accept when nothing else splits.
				if (parts.Count == 1 && separator == ";" && text.Contains(','))
					parts = text.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

				var item = new ImportFieldContract
				{
					Key = field.Key,
					Type = field.ItemType,
					MaxLength = field.MaxLength,
					Min = field.Min,
					Max = field.Max,
					Choices = field.Choices,
					References = field.References,
					ReferenceMatch = field.ReferenceMatch
				};

				var values = new List<string>();
				foreach (var part in parts)
				{
					var converted = ConvertScalar(item, part, context);
					if (converted.Invalid)
						return converted;
					if (converted.Value is string s && !values.Contains(s, StringComparer.OrdinalIgnoreCase))
						values.Add(s);
				}

				return values.Count == 0 ? ImportConversion.Empty() : ImportConversion.Ok(values);
			}

			return ConvertScalar(field, text, context);
		}

		private static ImportConversion ConvertScalar(ImportFieldContract field, string text, ImportConversionContext context)
		{
			switch (field.Type)
			{
				case ImportFieldTypes.Text:
				case ImportFieldTypes.Reference:
					if (field.MaxLength.HasValue && text.Length > field.MaxLength.Value)
						return ImportConversion.Fail("IMP-TYPE-TOO-LONG", P("max", field.MaxLength.Value));
					return ImportConversion.Ok(text);

				case ImportFieldTypes.Integer:
				{
					var normalized = text.Replace(" ", string.Empty);
					if (!long.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
					{
						// "12.0" from a spreadsheet's number cell is still the integer 12.
						if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Truncate(d))
							number = (long)d;
						else
							return ImportConversion.Fail("IMP-TYPE-INTEGER");
					}
					if ((field.Min.HasValue && number < field.Min.Value) || (field.Max.HasValue && number > field.Max.Value))
						return ImportConversion.Fail("IMP-TYPE-RANGE", Range(field));
					return ImportConversion.Ok(number.ToString(CultureInfo.InvariantCulture));
				}

				case ImportFieldTypes.Decimal:
				{
					var normalized = text.Replace(" ", string.Empty);
					if (normalized.Count(c => c == ',') == 1 && !normalized.Contains('.'))
						normalized = normalized.Replace(',', '.');
					if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
						return ImportConversion.Fail("IMP-TYPE-DECIMAL");
					if ((field.Min.HasValue && number < field.Min.Value) || (field.Max.HasValue && number > field.Max.Value))
						return ImportConversion.Fail("IMP-TYPE-RANGE", Range(field));
					return ImportConversion.Ok(number.ToString(CultureInfo.InvariantCulture));
				}

				case ImportFieldTypes.Boolean:
					if (TrueTokens.Contains(text))
						return ImportConversion.Ok("true");
					if (FalseTokens.Contains(text))
						return ImportConversion.Ok("false");
					return ImportConversion.Fail("IMP-TYPE-BOOLEAN");

				case ImportFieldTypes.Date:
					return ConvertDate(text, context?.DateFormat);

				case ImportFieldTypes.DateTime:
					return ConvertDateTime(text, context);

				case ImportFieldTypes.Time:
					if (TimeSpan.TryParseExact(text, new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1))
						return ImportConversion.Ok(time.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
					return ImportConversion.Fail("IMP-TYPE-TIME");

				case ImportFieldTypes.Email:
				{
					var email = text.Trim().Trim('<', '>').ToLowerInvariant();
					if (email.StartsWith("mailto:", StringComparison.Ordinal))
						email = email.Substring(7);
					if (email.Length > 256 || !EmailPattern.IsMatch(email) || email.Contains(".."))
						return ImportConversion.Fail("IMP-EMAIL-INVALID");
					return ImportConversion.Ok(email);
				}

				case ImportFieldTypes.Phone:
				{
					if (context?.Phones == null)
						return ImportConversion.Ok(text);
					var result = context.Phones.Process(text, context.PhoneRegion);
					if (result == null || !result.IsValid || string.IsNullOrWhiteSpace(result.InternationalNumber))
						return ImportConversion.Fail("IMP-PHONE-UNPARSEABLE", P("region", context.PhoneRegion ?? "us"));
					return ImportConversion.Ok(result.InternationalNumber);
				}

				case ImportFieldTypes.Url:
					if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
						return ImportConversion.Ok(uri.ToString());
					return ImportConversion.Fail("IMP-TYPE-URL");

				case ImportFieldTypes.Choice:
				{
					var key = Normalize(text);
					if (context?.ChoiceMap != null && context.ChoiceMap.TryGetValue(key, out var mapped))
						return string.IsNullOrEmpty(mapped) ? ImportConversion.Empty() : ImportConversion.Ok(mapped);

					foreach (var choice in field.Choices)
					{
						if (Normalize(choice.Code) == key || choice.Aliases.Any(a => Normalize(a) == key))
							return ImportConversion.Ok(choice.Code);
					}

					return ImportConversion.Fail("IMP-CHOICE-UNMAPPED");
				}

				case ImportFieldTypes.Latitude:
				case ImportFieldTypes.Longitude:
				{
					var limit = field.Type == ImportFieldTypes.Latitude ? 90d : 180d;
					if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees) || double.IsNaN(degrees) ||
						Math.Abs(degrees) > limit)
						return ImportConversion.Fail("IMP-TYPE-COORDINATE", P("limit", (int)limit));
					return ImportConversion.Ok(Math.Round(degrees, 7).ToString("0.0######", CultureInfo.InvariantCulture));
				}

				case ImportFieldTypes.Colour:
				{
					var match = HexColour.Match(text.Trim());
					if (!match.Success)
						return ImportConversion.Fail("IMP-TYPE-COLOUR");
					var hex = match.Groups[1].Value.ToLowerInvariant();
					if (hex.Length == 3)
						hex = string.Concat(hex.Select(c => new string(c, 2)));
					return ImportConversion.Ok("#" + hex);
				}

				case ImportFieldTypes.TimeZone:
				{
					var zone = text.Trim();
					var windows = TimeZones.Zones.Keys.FirstOrDefault(k => string.Equals(k, zone, StringComparison.OrdinalIgnoreCase));
					if (windows == null && TimeZoneInfo.TryConvertIanaIdToWindowsId(zone, out var converted))
						windows = TimeZones.Zones.Keys.FirstOrDefault(k => string.Equals(k, converted, StringComparison.OrdinalIgnoreCase));
					return windows == null ? ImportConversion.Fail("IMP-TYPE-TIMEZONE") : ImportConversion.Ok(windows);
				}

				case ImportFieldTypes.Language:
				{
					var code = text.Trim().ToLowerInvariant().Replace('_', '-');
					var two = code.Length >= 2 ? code.Substring(0, 2) : code;
					return Languages.Contains(two) ? ImportConversion.Ok(two) : ImportConversion.Fail("IMP-TYPE-LANGUAGE");
				}

				default:
					return ImportConversion.Fail("IMP-TYPE-UNKNOWN");
			}
		}

		private static ImportConversion ConvertDate(string text, string format)
		{
			// Spreadsheet date cells arrive as ISO already (the reader renders them); "2026-04-03T00:00:00" is a date too.
			if (DateTime.TryParseExact(text, new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy/MM/dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
				return ImportConversion.Ok(iso.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

			if (!string.IsNullOrWhiteSpace(format))
			{
				// The answer is about order (day first, month first), not punctuation: 03.04.2026 and 03-04-2026 read like 03/04/2026.
				var separator = format.FirstOrDefault(c => !char.IsLetter(c));
				var unified = separator == default(char) ? text : Regex.Replace(text, @"[./\-]", separator.ToString());
				var variants = new[] { format, format.Replace("dd", "d").Replace("MM", "M") };
				if (DateTime.TryParseExact(unified, variants, CultureInfo.InvariantCulture, DateTimeStyles.None, out var chosen))
					return ImportConversion.Ok(chosen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
				return ImportConversion.Fail("IMP-TYPE-DATE", P("format", format));
			}

			// Without the administrator's answer, a day-month order is only accepted when the value itself settles it.
			var parts = text.Split('/', '.', '-');
			if (parts.Length == 3 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b) && int.TryParse(parts[2], out var y) && parts[2].Length == 4)
			{
				if (a > 12 && b <= 12 && TryDate(y, b, a, out var dayFirst))
					return ImportConversion.Ok(dayFirst);
				if (b > 12 && a <= 12 && TryDate(y, a, b, out var monthFirst))
					return ImportConversion.Ok(monthFirst);
				if (a <= 12 && b <= 12)
					return a == b && TryDate(y, a, b, out var same) ? ImportConversion.Ok(same) : ImportConversion.Fail("IMP-TYPE-DATE-AMBIGUOUS");
			}

			if (DateTime.TryParseExact(text, new[] { "d MMM yyyy", "d MMMM yyyy", "MMM d, yyyy", "MMMM d, yyyy", "d-MMM-yyyy", "d-MMM-yy" },
				CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var named))
				return ImportConversion.Ok(named.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

			return ImportConversion.Fail("IMP-TYPE-DATE");
		}

		private static ImportConversion ConvertDateTime(string text, ImportConversionContext context)
		{
			if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var withOffset) &&
				(text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || text.Contains('+') || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$")))
				return ImportConversion.Ok(withOffset.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

			var date = ConvertDate(text.Split(' ', 'T')[0], context?.DateFormat);
			if (date.Invalid)
				return date;

			var timePart = text.Contains('T') ? text.Substring(text.IndexOf('T') + 1) : text.Contains(' ') ? text.Substring(text.IndexOf(' ') + 1) : "00:00";
			if (!TimeSpan.TryParse(timePart.Trim(), CultureInfo.InvariantCulture, out var time))
				return ImportConversion.Fail("IMP-TYPE-DATETIME");

			var local = DateTime.ParseExact((string)date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture).Add(time);
			try
			{
				var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(context?.TimeZoneId) ? "UTC" : context.TimeZoneId);
				var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
				return ImportConversion.Ok(utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
			}
			catch (ArgumentException)
			{
				// A local time skipped by a daylight-saving change cannot be placed: ask rather than guess.
				return ImportConversion.Fail("IMP-TYPE-DATETIME-DST");
			}
			catch (TimeZoneNotFoundException)
			{
				return ImportConversion.Fail("IMP-TYPE-DATETIME");
			}
		}

		private static bool TryDate(int year, int month, int day, out string value)
		{
			value = null;
			if (year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
				return false;
			value = new DateTime(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			return true;
		}

		/// <summary>Trims and drops characters that cannot be stored; null for blank.</summary>
		public static string Clean(string raw)
		{
			if (raw == null)
				return null;

			var trimmed = raw.Trim().Trim('﻿', '​');
			return trimmed.Length == 0 ? null : trimmed;
		}

		/// <summary>Case-, accent-, space- and punctuation-insensitive form used for header and code matching.</summary>
		public static string Normalize(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;

			var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
			var builder = new StringBuilder(decomposed.Length);
			foreach (var c in decomposed)
			{
				var category = CharUnicodeInfo.GetUnicodeCategory(c);
				if (category == UnicodeCategory.NonSpacingMark)
					continue;
				if (char.IsLetterOrDigit(c) || c == '@' || c == '.')
					builder.Append(c);
			}

			return builder.ToString().Normalize(NormalizationForm.FormC);
		}

		/// <summary>True for invisible control characters a cell must not carry (tabs and line breaks are allowed).</summary>
		public static bool HasControlCharacters(string value) =>
			!string.IsNullOrEmpty(value) && value.Any(c => (c < 0x20 && c != '\t' && c != '\n' && c != '\r') || c == 0x7F);

		private static Dictionary<string, string> P(string key, object value) =>
			new Dictionary<string, string> { [key] = System.Convert.ToString(value, CultureInfo.InvariantCulture) };

		private static Dictionary<string, string> Range(ImportFieldContract field) => new Dictionary<string, string>
		{
			["min"] = field.Min?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
			["max"] = field.Max?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
		};
	}
}
