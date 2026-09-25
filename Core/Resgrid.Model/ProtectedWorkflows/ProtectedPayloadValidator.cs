using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>The value-free result of checking a rendered protected payload: the rule that failed and where, never what.</summary>
	public readonly struct ProtectedPayloadCheck
	{
		private ProtectedPayloadCheck(bool ok, string rule, int? line, int? position)
		{
			Ok = ok;
			Rule = rule;
			Line = line;
			Position = position;
		}

		public bool Ok { get; }
		public string Rule { get; }
		public int? Line { get; }
		public int? Position { get; }

		public static ProtectedPayloadCheck Valid => new ProtectedPayloadCheck(true, null, null, null);

		public static ProtectedPayloadCheck Invalid(string rule, int? line = null, int? position = null) => new ProtectedPayloadCheck(false, rule, line, position);

		/// <summary>"payload_invalid: rule=json_parse line=3 position=14" — position and rule only.</summary>
		public string Describe()
		{
			if (Ok)
				return null;
			var text = $"{ProtectedWorkflowErrorCodes.PayloadInvalid}: rule={Rule}";
			if (Line.HasValue)
				text += $" line={Line.Value}";
			if (Position.HasValue)
				text += $" position={Position.Value}";
			return text;
		}
	}

	/// <summary>
	/// Pre-send validation of a rendered protected payload for its declared content type. A payload that fails never
	/// leaves: malformed JSON, a FHIR body without a resourceType, XML that is not well formed (DTDs are refused), or an
	/// HL7 v2 message whose header or segment ids are wrong. Parser messages are never surfaced — they can quote input.
	/// </summary>
	public static class ProtectedPayloadValidator
	{
		public const string RuleJsonParse = "json_parse";
		public const string RuleFhirResourceType = "fhir_resource_type";
		public const string RuleXmlWellFormed = "xml_well_formed";
		public const string RuleHl7Header = "hl7_header";
		public const string RuleHl7Segment = "hl7_segment";

		public const string Hl7MediaType = "x-application/hl7-v2+er7";
		public const string Hl7Header = "MSH|^~\\&";

		private static readonly Regex SegmentId = new Regex("^[A-Z0-9]{3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>
		/// The body as it will be sent. HL7 v2 segments are separated by a carriage return: a template written in a
		/// browser uses CRLF or LF between lines, so line breaks become CR and a trailing break is dropped. Line breaks
		/// inside values cannot reach here as raw characters when the values went through hl7_escape.
		/// </summary>
		public static string NormalizeBody(string mediaType, string body)
		{
			if (body == null || !string.Equals(mediaType, Hl7MediaType, StringComparison.Ordinal))
				return body;

			var normalized = body.Replace("\r\n", "\r").Replace('\n', '\r');
			return normalized.TrimEnd('\r');
		}

		public static ProtectedPayloadCheck Validate(string mediaType, string body)
		{
			body ??= string.Empty;
			switch (mediaType)
			{
				case "application/json":
					return ValidateJson(body, requireResourceType: false);
				case "application/fhir+json":
					return ValidateJson(body, requireResourceType: true);
				case "application/xml":
				case "text/xml":
				case "application/soap+xml":
					return ValidateXml(body);
				case Hl7MediaType:
					return ValidateHl7(body);
				case "text/plain":
					return ProtectedPayloadCheck.Valid;
				default:
					return ProtectedPayloadCheck.Invalid(ProtectedStepOptions.ContentTypeNotAllowed);
			}
		}

		private static ProtectedPayloadCheck ValidateJson(string body, bool requireResourceType)
		{
			JToken token;
			try
			{
				// Comments are not JSON, and the body leaves exactly as rendered. Json.NET would skip them wherever they
				// sit (inside the value as well as after it), so look at every token before parsing.
				using (var scan = NewJsonReader(body))
				{
					while (scan.Read())
					{
						if (scan.TokenType == JsonToken.Comment)
							return ProtectedPayloadCheck.Invalid(RuleJsonParse, scan.LineNumber, scan.LinePosition);
					}
				}

				using var reader = NewJsonReader(body);
				token = JToken.ReadFrom(reader);
				// Anything after the first value (a second object, stray text) is malformed.
				if (reader.Read())
					return ProtectedPayloadCheck.Invalid(RuleJsonParse, reader.LineNumber, reader.LinePosition);
			}
			catch (JsonReaderException ex)
			{
				return ProtectedPayloadCheck.Invalid(RuleJsonParse, ex.LineNumber, ex.LinePosition);
			}
			catch (JsonException)
			{
				return ProtectedPayloadCheck.Invalid(RuleJsonParse);
			}

			if (requireResourceType)
			{
				var resourceType = (token as JObject)?.GetValue("resourceType", StringComparison.Ordinal);
				if (resourceType == null || resourceType.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)resourceType))
					return ProtectedPayloadCheck.Invalid(RuleFhirResourceType);
			}

			return ProtectedPayloadCheck.Valid;
		}

		private static JsonTextReader NewJsonReader(string body) =>
			new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };

		private static ProtectedPayloadCheck ValidateXml(string body)
		{
			var settings = new XmlReaderSettings
			{
				DtdProcessing = DtdProcessing.Prohibit,
				XmlResolver = null,
				CheckCharacters = true,
				ConformanceLevel = ConformanceLevel.Document,
				MaxCharactersFromEntities = 0
			};

			try
			{
				using var reader = XmlReader.Create(new StringReader(body), settings);
				while (reader.Read())
				{
				}
			}
			catch (XmlException ex)
			{
				return ProtectedPayloadCheck.Invalid(RuleXmlWellFormed, ex.LineNumber, ex.LinePosition);
			}

			return ProtectedPayloadCheck.Valid;
		}

		private static ProtectedPayloadCheck ValidateHl7(string body)
		{
			if (!body.StartsWith(Hl7Header, StringComparison.Ordinal))
				return ProtectedPayloadCheck.Invalid(RuleHl7Header, 1, 1);

			var segments = body.Split('\r');
			for (var i = 0; i < segments.Length; i++)
			{
				var segment = segments[i];
				// A trailing CR leaves one empty tail; an empty segment anywhere else is malformed.
				if (segment.Length == 0 && i == segments.Length - 1 && i > 0)
					continue;

				if (segment.Length < 3 || !SegmentId.IsMatch(segment.Substring(0, 3)) || (segment.Length > 3 && segment[3] != '|'))
					return ProtectedPayloadCheck.Invalid(RuleHl7Segment, i + 1, 1);
			}

			return ProtectedPayloadCheck.Valid;
		}
	}
}
