using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;

namespace Resgrid.Providers.Workflow.Executors
{
	/// <summary>
	/// Evaluates a protected step's success rule against the destination's response, and reads the values its
	/// ResponseCapture asks for. The body is held only for the duration of this call. Everything reported back is value
	/// free — a rule name, an acknowledgement code (AA/AE/AR/CA/CE/CR), a key name — except the captured values themselves,
	/// which go to the caller in memory to be encrypted into the call.
	/// </summary>
	public static class ProtectedResponseRules
	{
		public sealed class Evaluation
		{
			/// <summary>The destination accepted the payload (a 2xx that satisfies the rule).</summary>
			public bool Accepted { get; init; }

			/// <summary>The rule said no (as opposed to a plain HTTP failure): outcome failed_ack.</summary>
			public bool RuleRejected { get; init; }

			/// <summary>Value-free, for the run log and the disclosure: "rule=hl7_ack ack=AE".</summary>
			public string Detail { get; init; }

			public Dictionary<string, string> Captured { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

			/// <summary>Capture keys the response did not carry (names only).</summary>
			public List<string> Missing { get; } = new List<string>();
		}

		private static readonly Regex FhirLocation = new Regex(@"(?:^|/)(?<type>[A-Z][A-Za-z]{0,63})/(?<id>[A-Za-z0-9\-\.]{1,64})(?:/_history/[^/?#]*)?(?:[?#].*)?$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <param name="header">Looks a response header up by name (null when absent).</param>
		public static Evaluation Evaluate(ProtectedStepOptions options, int status, string body, Func<string, string> header)
		{
			options ??= new ProtectedStepOptions();
			var is2xx = status >= 200 && status < 300;
			var ruleType = options.SuccessRuleType;
			var rule = options.SuccessRule;

			if (!is2xx)
			{
				// A FHIR server that explains its failure in an OperationOutcome is a rejected acknowledgement, not a bare
				// HTTP failure; the retry policy still decides from the status (5xx and 429 retry, other 4xx do not).
				if (ruleType == ProtectedSuccessRule.FhirOperationOutcome && HasOperationOutcomeError(body))
					return new Evaluation { RuleRejected = true, Detail = $"rule={ruleType} outcome=error status={status}" };
				return new Evaluation { Detail = null };
			}

			string rejection = ruleType switch
			{
				ProtectedSuccessRule.Http2xx => null,
				ProtectedSuccessRule.JsonPath => JsonPathMatches(body, rule?.Path, rule?.Expected) ? null : $"rule={ruleType}",
				ProtectedSuccessRule.XPath => XPathMatches(body, rule?.Path, rule?.Expected) ? null : $"rule={ruleType}",
				ProtectedSuccessRule.Hl7Ack => Hl7AckRejection(body),
				ProtectedSuccessRule.FhirOperationOutcome => HasOperationOutcomeError(body) ? $"rule={ruleType} outcome=error" : null,
				_ => $"rule={ruleType}"
			};

			if (rejection != null)
				return new Evaluation { RuleRejected = true, Detail = rejection };

			var evaluation = new Evaluation { Accepted = true };
			foreach (var entry in options.ResponseCapture)
			{
				var value = Capture(entry, body, header);
				value = value?.Trim();
				if (string.IsNullOrEmpty(value) || value.Length > CallSubjectIdentifiers.MaxValueLength || value.Any(char.IsControl))
					evaluation.Missing.Add(entry.Key);
				else
					evaluation.Captured[entry.Key] = value;
			}

			return evaluation;
		}

		// ── Rules ─────────────────────────────────────────────────────────────────────────────────────

		private static bool JsonPathMatches(string body, string path, string expected)
		{
			var token = SelectJson(body, path);
			if (token == null || token.Type == JTokenType.Null)
				return false;
			var text = token is JValue value ? Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture) : token.ToString(Newtonsoft.Json.Formatting.None);
			return expected == null ? !string.IsNullOrEmpty(text) : string.Equals(text, expected, StringComparison.Ordinal);
		}

		private static bool XPathMatches(string body, string path, string expected)
		{
			var text = SelectXml(body, path);
			return text != null && (expected == null ? text.Length > 0 : string.Equals(text, expected, StringComparison.Ordinal));
		}

		/// <summary>Null when MSA-1 is AA or CA; otherwise "rule=hl7_ack ack=AE" (or ack=missing).</summary>
		private static string Hl7AckRejection(string body)
		{
			var code = Hl7Field(body, "MSA", 1, 0);
			if (string.Equals(code, "AA", StringComparison.Ordinal) || string.Equals(code, "CA", StringComparison.Ordinal))
				return null;

			var safe = code != null && Regex.IsMatch(code, "^[A-Z]{2}$") ? code : "missing";
			return $"rule={ProtectedSuccessRule.Hl7Ack} ack={safe}";
		}

		/// <summary>An OperationOutcome (on its own, or in a transaction/batch response entry) with an error or fatal issue.</summary>
		private static bool HasOperationOutcomeError(string body)
		{
			var root = ParseJson(body) as JObject;
			if (root == null)
				return false;

			IEnumerable<JObject> Outcomes()
			{
				if ((string)root["resourceType"] == "OperationOutcome")
					yield return root;
				if ((string)root["resourceType"] == "Bundle" && root["entry"] is JArray entries)
				{
					foreach (var entry in entries.OfType<JObject>())
					{
						if (entry["response"]?["outcome"] is JObject outcome && (string)outcome["resourceType"] == "OperationOutcome")
							yield return outcome;
						if (entry["resource"] is JObject resource && (string)resource["resourceType"] == "OperationOutcome")
							yield return resource;
					}
				}
			}

			return Outcomes().Any(o => (o["issue"] as JArray)?.OfType<JObject>()
				.Any(i => (string)i["severity"] == "error" || (string)i["severity"] == "fatal") == true);
		}

		// ── Capture ───────────────────────────────────────────────────────────────────────────────────

		private static string Capture(ProtectedCaptureEntry entry, string body, Func<string, string> header)
		{
			switch (entry.Source)
			{
				case ProtectedCaptureEntry.JsonPath:
					var token = SelectJson(body, entry.Expression);
					return token is JValue value && value.Value != null
						? Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture)
						: null;
				case ProtectedCaptureEntry.XPath:
					return SelectXml(body, entry.Expression);
				case ProtectedCaptureEntry.Hl7Field:
					return Hl7FieldReference(body, entry.Expression);
				case ProtectedCaptureEntry.Header:
					return header?.Invoke(entry.Expression);
				case ProtectedCaptureEntry.FhirLocationId:
					return FhirResourceId(body, header, entry.Expression);
				default:
					return null;
			}
		}

		/// <summary>The id of the created resource: the Location header, or a Bundle entry's response.location, or the returned resource's id.</summary>
		private static string FhirResourceId(string body, Func<string, string> header, string resourceType)
		{
			foreach (var location in new[] { header?.Invoke("Location"), header?.Invoke("Content-Location") })
			{
				var id = LocationId(location, resourceType);
				if (id != null)
					return id;
			}

			var root = ParseJson(body) as JObject;
			if (root == null)
				return null;

			if ((string)root["resourceType"] == "Bundle" && root["entry"] is JArray entries)
			{
				foreach (var entry in entries.OfType<JObject>())
				{
					var id = LocationId((string)entry["response"]?["location"], resourceType);
					if (id != null)
						return id;
				}
				return null;
			}

			var type = (string)root["resourceType"];
			return resourceType == null || string.Equals(type, resourceType, StringComparison.Ordinal) ? (string)root["id"] : null;
		}

		private static string LocationId(string location, string resourceType)
		{
			if (string.IsNullOrWhiteSpace(location))
				return null;

			// Drop a trailing /_history/n so the pattern sees .../Type/id.
			var trimmed = Regex.Replace(location.Trim(), @"/_history/[^/?#]*", string.Empty);
			var match = FhirLocation.Match(trimmed);
			if (!match.Success)
				return null;
			return resourceType == null || string.Equals(match.Groups["type"].Value, resourceType, StringComparison.Ordinal)
				? match.Groups["id"].Value
				: null;
		}

		// ── Parsers ───────────────────────────────────────────────────────────────────────────────────

		private static JToken ParseJson(string body)
		{
			if (string.IsNullOrWhiteSpace(body))
				return null;
			try
			{
				using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 };
				return JToken.ReadFrom(reader);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static JToken SelectJson(string body, string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;
			try
			{
				return ParseJson(body)?.SelectToken(path, errorWhenNoMatch: false);
			}
			catch (JsonException)
			{
				return null; // a path that matches several tokens, or a malformed path
			}
		}

		/// <summary>The string value of an XPath expression (the first node of a node-set). DTDs are refused.</summary>
		private static string SelectXml(string body, string path)
		{
			if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(path))
				return null;
			try
			{
				var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
				using var reader = XmlReader.Create(new StringReader(body), settings);
				var navigator = new XPathDocument(reader).CreateNavigator();
				var result = navigator.Evaluate(path);
				return result switch
				{
					XPathNodeIterator nodes => nodes.MoveNext() ? nodes.Current?.Value : null,
					string text => text,
					bool flag => flag ? "true" : "false",
					double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
					_ => null
				};
			}
			catch (Exception ex) when (ex is XmlException || ex is XPathException)
			{
				return null;
			}
		}

		/// <summary>"MSA-2", "PID-3.1" or "PID-3.1.2": field, then optional component and subcomponent.</summary>
		private static string Hl7FieldReference(string body, string reference)
		{
			// ASCII digits only ([0-9], not \d), so every int.Parse below is safe.
			var match = Regex.Match(reference ?? string.Empty, @"^(?<seg>[A-Z0-9]{3})-(?<field>[0-9]{1,3})(\.(?<comp>[0-9]{1,3}))?(\.(?<sub>[0-9]{1,3}))?$");
			if (!match.Success)
				return null;

			var value = Hl7Field(body, match.Groups["seg"].Value, int.Parse(match.Groups["field"].Value), 0);
			if (value == null)
				return null;
			if (match.Groups["comp"].Success)
			{
				var components = value.Split('^');
				var index = int.Parse(match.Groups["comp"].Value) - 1;
				value = index >= 0 && index < components.Length ? components[index] : null;
			}
			if (value != null && match.Groups["sub"].Success)
			{
				var subcomponents = value.Split('&');
				var index = int.Parse(match.Groups["sub"].Value) - 1;
				value = index >= 0 && index < subcomponents.Length ? subcomponents[index] : null;
			}
			return value;
		}

		/// <summary>Field <paramref name="field"/> of the first <paramref name="segmentId"/> segment (MSH counts its field separator as MSH-1).</summary>
		private static string Hl7Field(string body, string segmentId, int field, int repetition)
		{
			if (string.IsNullOrEmpty(body) || field < 1)
				return null;

			foreach (var segment in body.Replace("\r\n", "\r").Replace('\n', '\r').Split('\r'))
			{
				if (!segment.StartsWith(segmentId + "|", StringComparison.Ordinal))
					continue;

				var parts = segment.Split('|');
				var index = segmentId == "MSH" ? field - 1 : field;
				if (segmentId == "MSH" && field == 1)
					return "|";
				if (index <= 0 || index >= parts.Length)
					return null;
				var repetitions = parts[index].Split('~');
				return repetition < repetitions.Length ? repetitions[repetition] : null;
			}

			return null;
		}
	}
}
