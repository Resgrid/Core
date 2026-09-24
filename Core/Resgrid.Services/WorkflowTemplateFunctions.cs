using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Scriban.Runtime;

namespace Resgrid.Services
{
	/// <summary>
	/// Escaping and date helpers available to every workflow template (pipe a value into them:
	/// <c>{{ call.notes | json_escape }}</c>). They make free text safe to drop into a structured payload: a quote,
	/// an angle bracket or an HL7 delimiter in a note can never break out of the value it was put in.
	/// </summary>
	public static class WorkflowTemplateFunctions
	{
		public const string JsonEscapeName = "json_escape";
		public const string XmlEscapeName = "xml_escape";
		public const string Hl7EscapeName = "hl7_escape";
		public const string FhirDateTimeName = "fhir_datetime";
		public const string Hl7TimestampName = "hl7_ts";

		/// <summary>The helpers that make a value safe inside a payload (the date helpers format, they do not escape).</summary>
		public static readonly string[] EscapeHelperNames = { JsonEscapeName, XmlEscapeName, Hl7EscapeName };

		/// <summary>Adds the helpers to a template context's global object.</summary>
		public static void AddTo(ScriptObject scriptObject)
		{
			if (scriptObject == null)
				return;

			scriptObject.Import(JsonEscapeName, new Func<object, string>(JsonEscape));
			scriptObject.Import(XmlEscapeName, new Func<object, string>(XmlEscape));
			scriptObject.Import(Hl7EscapeName, new Func<object, string>(Hl7Escape));
			scriptObject.Import(FhirDateTimeName, new Func<object, string>(FhirDateTime));
			scriptObject.Import(Hl7TimestampName, new Func<object, string>(Hl7Timestamp));
		}

		/// <summary>The value escaped for use INSIDE a JSON string (no surrounding quotes). Null is an empty string.</summary>
		public static string JsonEscape(object value)
		{
			var text = AsText(value);
			if (text.Length == 0)
				return string.Empty;

			// Escaping HTML characters too keeps "</script>" and friends inert if a payload is ever embedded in a page.
			var quoted = JsonConvert.ToString(text, '"', StringEscapeHandling.EscapeHtml);
			return quoted.Substring(1, quoted.Length - 2);
		}

		/// <summary>
		/// The value escaped for XML text or attribute content. Characters XML 1.0 cannot carry at all (control characters
		/// other than tab, CR and LF, and unpaired surrogates) are dropped rather than written as an invalid document.
		/// </summary>
		public static string XmlEscape(object value)
		{
			var text = AsText(value);
			if (text.Length == 0)
				return string.Empty;

			var sb = new StringBuilder(text.Length + 16);
			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];
				switch (c)
				{
					case '&': sb.Append("&amp;"); continue;
					case '<': sb.Append("&lt;"); continue;
					case '>': sb.Append("&gt;"); continue;
					case '"': sb.Append("&quot;"); continue;
					case '\'': sb.Append("&apos;"); continue;
					case '\t': sb.Append("&#x9;"); continue;
					case '\n': sb.Append("&#xA;"); continue;
					case '\r': sb.Append("&#xD;"); continue;
				}

				if (char.IsHighSurrogate(c))
				{
					if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
					{
						sb.Append(c).Append(text[i + 1]);
						i++;
					}
					continue;
				}

				if (char.IsLowSurrogate(c) || c < 0x20 || c == '\uFFFE' || c == '\uFFFF')
					continue;

				sb.Append(c);
			}

			return sb.ToString();
		}

		/// <summary>
		/// HL7 v2 escaping with the standard encoding characters (<c>|^~\&amp;</c>): the escape character first, then the
		/// field, component, repetition and subcomponent separators. CR and LF become <c>\X0D\</c> and <c>\X0A\</c>, so a
		/// line break in a note can never start a new segment. Other control characters are dropped.
		/// </summary>
		public static string Hl7Escape(object value)
		{
			var text = AsText(value);
			if (text.Length == 0)
				return string.Empty;

			var sb = new StringBuilder(text.Length + 16);
			foreach (var c in text)
			{
				switch (c)
				{
					case '\\': sb.Append("\\E\\"); break;
					case '|': sb.Append("\\F\\"); break;
					case '^': sb.Append("\\S\\"); break;
					case '&': sb.Append("\\T\\"); break;
					case '~': sb.Append("\\R\\"); break;
					case '\r': sb.Append("\\X0D\\"); break;
					case '\n': sb.Append("\\X0A\\"); break;
					default:
						if (c >= 0x20 || c == '\t')
							sb.Append(c);
						break;
				}
			}

			return sb.ToString();
		}

		/// <summary>A date as a FHIR <c>dateTime</c> in UTC (<c>2026-09-24T14:05:00Z</c>). Empty when there is no date.</summary>
		public static string FhirDateTime(object value)
		{
			var utc = AsUtc(value);
			return utc.HasValue ? utc.Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : string.Empty;
		}

		/// <summary>A date as an HL7 <c>TS</c> in UTC (<c>20260924140500+0000</c>). Empty when there is no date.</summary>
		public static string Hl7Timestamp(object value)
		{
			var utc = AsUtc(value);
			return utc.HasValue ? utc.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+0000" : string.Empty;
		}

		private static string AsText(object value) => value switch
		{
			null => string.Empty,
			string s => s,
			bool b => b ? "true" : "false",
			DateTime d => FhirDateTime(d),
			DateTimeOffset o => FhirDateTime(o),
			IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString() ?? string.Empty
		};

		/// <summary>
		/// A UTC instant from a template value. Unspecified DateTimes are UTC (the platform stores UTC); strings are parsed
		/// invariantly, and one without an offset is taken as UTC.
		/// </summary>
		private static DateTime? AsUtc(object value)
		{
			switch (value)
			{
				case DateTime d:
					return d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
				case DateTimeOffset o:
					return o.UtcDateTime;
				case string s when !string.IsNullOrWhiteSpace(s):
					if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed))
						return parsed.UtcDateTime;
					return null;
				default:
					return null;
			}
		}
	}
}
