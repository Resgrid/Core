using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>How a protected step decides the destination accepted the payload (action config "SuccessRule").</summary>
	public sealed class ProtectedSuccessRule
	{
		public const string Http2xx = "http_2xx";
		public const string JsonPath = "json_path";
		public const string XPath = "xpath";
		public const string Hl7Ack = "hl7_ack";
		public const string FhirOperationOutcome = "fhir_operation_outcome";

		public static readonly string[] Types = { Http2xx, JsonPath, XPath, Hl7Ack, FhirOperationOutcome };

		/// <summary>One of <see cref="Types"/>.</summary>
		public string Type { get; set; }

		/// <summary>json_path / xpath: the expression to read from the response (<c>$.status</c>, <c>//*[local-name()='status']</c>).</summary>
		public string Path { get; set; }

		/// <summary>json_path / xpath: the value it must equal (ordinal). Null means "present and not empty".</summary>
		public string Expected { get; set; }

		/// <summary>True when the response body has to be read to decide (everything but http_2xx).</summary>
		public bool NeedsBody => !string.Equals(Type, Http2xx, StringComparison.Ordinal);
	}

	/// <summary>One value a protected step copies from the response into the call's subject identifiers (action config "ResponseCapture").</summary>
	public sealed class ProtectedCaptureEntry
	{
		public const string JsonPath = "json_path";
		public const string XPath = "xpath";
		public const string Hl7Field = "hl7_field";
		public const string Header = "header";
		public const string FhirLocationId = "fhir_location_id";

		public static readonly string[] Sources = { JsonPath, XPath, Hl7Field, Header, FhirLocationId };

		/// <summary>One of <see cref="Sources"/>.</summary>
		public string Source { get; set; }

		/// <summary>
		/// json_path: <c>$.id</c>; xpath: <c>//*[local-name()='id']/@value</c>; hl7_field: <c>MSA-2</c> or <c>PID-3.1</c>;
		/// header: <c>Location</c>; fhir_location_id: an optional resource type (<c>Encounter</c>) to pick from a Bundle.
		/// </summary>
		public string Expression { get; set; }

		/// <summary>The subject identifier key the value is written under (<c>ehr_encounter_id</c>).</summary>
		public string Key { get; set; }
	}

	/// <summary>
	/// The EHR-integration options of a protected HTTP step, read from its action config: content type, success rule,
	/// response capture and idempotency. All of them live in the action config, so every one is part of the release
	/// fingerprint; changing any of them sends the release back for approval.
	/// </summary>
	public sealed class ProtectedStepOptions
	{
		public const string DefaultContentType = "application/json";

		// Validation codes (ValidationError_{code} in the UI).
		public const string ContentTypeNotAllowed = "content_type_not_allowed";
		public const string SuccessRuleInvalid = "success_rule_invalid";
		public const string CaptureInvalid = "capture_invalid";
		public const string CaptureTooMany = "capture_too_many";
		public const string IdempotencyHeaderInvalid = "idempotency_header_invalid";
		public const string IfNoneExistInvalid = "if_none_exist_invalid";

		/// <summary>The only content types a protected step may declare.</summary>
		public static readonly string[] AllowedContentTypes =
		{
			"application/json", "application/fhir+json", "application/xml", "text/xml", "application/soap+xml", "x-application/hl7-v2+er7", "text/plain"
		};

		/// <summary>Headers a protected step may never set itself; they carry authentication or framing.</summary>
		public static readonly HashSet<string> ForbiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Host", "Authorization", "Proxy-Authorization", "Content-Length", "Transfer-Encoding", "Connection", "Cookie", "Content-Type", "If-None-Exist"
		};

		public static readonly Regex SubjectKeyPattern = new Regex("^[a-z0-9_]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex HeaderToken = new Regex("^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		// [0-9], not \d: \d also matches non-ASCII digits, which the response rules' int.Parse would throw on after the send.
		private static readonly Regex Hl7FieldReference = new Regex(@"^[A-Z0-9]{3}-[0-9]{1,3}(\.[0-9]{1,3}){0,2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>The declared content type exactly as configured (null when absent).</summary>
		public string ContentType { get; set; }

		/// <summary>The media type, lower-cased and without parameters; application/json when none is configured.</summary>
		public string MediaType => NormalizeMediaType(ContentType);

		/// <summary>Null means http_2xx.</summary>
		public ProtectedSuccessRule SuccessRule { get; set; }

		public List<ProtectedCaptureEntry> ResponseCapture { get; set; } = new List<ProtectedCaptureEntry>();

		/// <summary>Header that carries run.idempotency_key (Idempotency-Key), or null.</summary>
		public string IdempotencyHeader { get; set; }

		/// <summary>The FHIR conditional-create query sent as If-None-Exist (it may use ordinary template values), or null.</summary>
		public string IfNoneExist { get; set; }

		public string SuccessRuleType => SuccessRule?.Type ?? ProtectedSuccessRule.Http2xx;

		/// <summary>True when the response body must be read (a rule that inspects it, or a capture).</summary>
		public bool NeedsResponseBody => (SuccessRule?.NeedsBody ?? false) || ResponseCapture.Any(c => c.Source != ProtectedCaptureEntry.Header);

		public static string NormalizeMediaType(string contentType)
		{
			if (string.IsNullOrWhiteSpace(contentType))
				return DefaultContentType;
			var semicolon = contentType.IndexOf(';');
			return (semicolon >= 0 ? contentType.Substring(0, semicolon) : contentType).Trim().ToLowerInvariant();
		}

		public static bool IsAllowedContentType(string contentType) =>
			AllowedContentTypes.Contains(NormalizeMediaType(contentType), StringComparer.Ordinal) &&
			(contentType == null || (contentType.IndexOf("{{", StringComparison.Ordinal) < 0 && contentType.IndexOfAny(new[] { '\r', '\n' }) < 0));

		/// <summary>
		/// Reads the options from an action config (keys are case-insensitive). Malformed JSON reads as no options;
		/// the structural validator reports it separately. <paramref name="errors"/> lists every problem found.
		/// </summary>
		public static ProtectedStepOptions Read(string actionConfigJson, out List<string> errors, int maxCaptureEntries = 5)
		{
			errors = new List<string>();
			var options = new ProtectedStepOptions();
			if (string.IsNullOrWhiteSpace(actionConfigJson))
				return options;

			JObject config;
			try
			{
				using var reader = new JsonTextReader(new System.IO.StringReader(actionConfigJson)) { DateParseHandling = DateParseHandling.None };
				config = JToken.ReadFrom(reader) as JObject;
			}
			catch (JsonException)
			{
				return options;
			}

			if (config == null)
				return options;

			options.ContentType = String(config, "ContentType");
			if (options.ContentType != null && !IsAllowedContentType(options.ContentType))
				errors.Add(ContentTypeNotAllowed);

			var rule = config.GetValue("SuccessRule", StringComparison.OrdinalIgnoreCase);
			if (rule != null && rule.Type != JTokenType.Null)
			{
				if (rule is JObject ruleObject)
				{
					options.SuccessRule = new ProtectedSuccessRule
					{
						Type = String(ruleObject, "Type")?.ToLowerInvariant(),
						Path = String(ruleObject, "Path"),
						Expected = String(ruleObject, "Expected")
					};
				}
				else if (rule.Type == JTokenType.String)
				{
					options.SuccessRule = new ProtectedSuccessRule { Type = ((string)rule)?.Trim().ToLowerInvariant() };
				}

				var parsed = options.SuccessRule;
				if (parsed == null || !ProtectedSuccessRule.Types.Contains(parsed.Type, StringComparer.Ordinal) ||
					((parsed.Type == ProtectedSuccessRule.JsonPath || parsed.Type == ProtectedSuccessRule.XPath) && string.IsNullOrWhiteSpace(parsed.Path)) ||
					HasTemplate(parsed.Path) || HasTemplate(parsed.Expected))
					errors.Add(SuccessRuleInvalid);
			}

			var capture = config.GetValue("ResponseCapture", StringComparison.OrdinalIgnoreCase);
			if (capture != null && capture.Type != JTokenType.Null)
			{
				if (capture is JArray entries)
				{
					foreach (var entry in entries)
					{
						if (entry is not JObject entryObject)
						{
							errors.Add(CaptureInvalid);
							continue;
						}

						var parsed = new ProtectedCaptureEntry
						{
							Source = String(entryObject, "Source")?.ToLowerInvariant(),
							Expression = String(entryObject, "Expression"),
							Key = String(entryObject, "Key")
						};
						options.ResponseCapture.Add(parsed);
						if (!IsValidCapture(parsed))
							errors.Add(CaptureInvalid);
					}

					if (options.ResponseCapture.Count > Math.Max(0, maxCaptureEntries))
						errors.Add(CaptureTooMany);
					if (options.ResponseCapture.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != options.ResponseCapture.Count)
						errors.Add(CaptureInvalid);
				}
				else
				{
					errors.Add(CaptureInvalid);
				}
			}

			options.IdempotencyHeader = String(config, "IdempotencyHeader");
			if (options.IdempotencyHeader != null &&
				(!HeaderToken.IsMatch(options.IdempotencyHeader) || ForbiddenHeaders.Contains(options.IdempotencyHeader)))
				errors.Add(IdempotencyHeaderInvalid);

			options.IfNoneExist = String(config, "IfNoneExist");
			if (options.IfNoneExist != null && (options.IfNoneExist.Length > 1024 || options.IfNoneExist.IndexOfAny(new[] { '\r', '\n' }) >= 0))
				errors.Add(IfNoneExistInvalid);

			errors = errors.Distinct(StringComparer.Ordinal).ToList();
			return options;
		}

		/// <summary>The header names the action config's Headers object sets (for the forbidden-header check).</summary>
		public static IEnumerable<string> ReadHeaderNames(string actionConfigJson)
		{
			if (string.IsNullOrWhiteSpace(actionConfigJson))
				yield break;

			JObject headers;
			try
			{
				headers = JObject.Parse(actionConfigJson).GetValue("Headers", StringComparison.OrdinalIgnoreCase) as JObject;
			}
			catch (JsonException)
			{
				yield break;
			}

			if (headers == null)
				yield break;
			foreach (var property in headers.Properties())
				yield return property.Name;
		}

		private static bool IsValidCapture(ProtectedCaptureEntry entry)
		{
			if (entry.Key == null || !SubjectKeyPattern.IsMatch(entry.Key))
				return false;
			if (!ProtectedCaptureEntry.Sources.Contains(entry.Source, StringComparer.Ordinal) || HasTemplate(entry.Expression))
				return false;

			return entry.Source switch
			{
				ProtectedCaptureEntry.FhirLocationId => entry.Expression == null || Regex.IsMatch(entry.Expression, "^[A-Za-z]{1,64}$"),
				ProtectedCaptureEntry.Hl7Field => entry.Expression != null && Hl7FieldReference.IsMatch(entry.Expression),
				ProtectedCaptureEntry.Header => entry.Expression != null && HeaderToken.IsMatch(entry.Expression),
				_ => !string.IsNullOrWhiteSpace(entry.Expression) && entry.Expression.Length <= 512
			};
		}

		private static bool HasTemplate(string value) => value != null && (value.Contains("{{") || value.Contains("}}"));

		private static string String(JObject json, string name)
		{
			var token = json.GetValue(name, StringComparison.OrdinalIgnoreCase);
			if (token == null || token.Type == JTokenType.Null)
				return null;
			var text = token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None);
			return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
		}
	}
}
