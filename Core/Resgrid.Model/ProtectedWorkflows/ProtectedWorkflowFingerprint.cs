using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The configuration fingerprint a Protected Workflow approval binds to: SHA-256 (lowercase hex) over canonical
	/// JSON of the trigger; every ENABLED step in execution order (action type, order, output template, condition,
	/// the full action config with object keys sorted, and the credential id); the sorted allow-listed field ids; the
	/// destination host; and the OAuth2 token host. Anything that changes what is sent, where, when or how changes
	/// the fingerprint. The credential's SECRET is deliberately not an input — rotating it must not force re-approval
	/// (the credential id is pinned, the secret is not). The step options (content type, success rule, capture,
	/// idempotency) live in the action config and so are covered with it; <see cref="ProtectedFingerprintExtras"/>
	/// adds the credential's OAuth2 method, the restricted and Part 2 attestations and every released custom field's
	/// sensitivity tag.
	/// </summary>
	public static class ProtectedWorkflowFingerprint
	{
		private const int Version = 2;

		public static string Compute(int triggerEventType, IEnumerable<WorkflowStep> steps, IEnumerable<string> allowedFieldIds,
			string destinationHost, string tokenHost, ProtectedFingerprintExtras extras = null)
		{
			extras ??= new ProtectedFingerprintExtras();
			var stepArray = new JArray();
			foreach (var step in OrderedEnabledSteps(steps))
			{
				stepArray.Add(new JObject
				{
					["actionType"] = step.ActionType,
					["stepOrder"] = step.StepOrder,
					["outputTemplate"] = NormalizeText(step.OutputTemplate),
					["conditionExpression"] = NormalizeText(step.ConditionExpression),
					["actionConfig"] = CanonicalizeActionConfig(step.ActionConfig),
					["credentialId"] = string.IsNullOrWhiteSpace(step.WorkflowCredentialId) ? null : step.WorkflowCredentialId.Trim().ToLowerInvariant()
				});
			}

			var canonical = new JObject
			{
				["v"] = Version,
				["trigger"] = triggerEventType,
				["steps"] = stepArray,
				["fields"] = new JArray(WorkflowProtectedRelease.NormalizeFieldIds(allowedFieldIds).Cast<object>().ToArray()),
				["destinationHost"] = NormalizeHost(destinationHost),
				["tokenHost"] = NormalizeHost(tokenHost),
				["authMethod"] = string.IsNullOrWhiteSpace(extras.AuthMethod) ? null : extras.AuthMethod.Trim().ToLowerInvariant(),
				["allowsRestricted"] = extras.AllowsRestricted,
				["allowsPart2"] = extras.AllowsPart2,
				["sensitivity"] = new JArray((extras.FieldSensitivities ?? new Dictionary<string, int>())
					.OrderBy(p => p.Key, StringComparer.Ordinal)
					.Select(p => (object)$"{p.Key.ToLowerInvariant()}={p.Value}").ToArray())
			};

			var bytes = Encoding.UTF8.GetBytes(canonical.ToString(Formatting.None));
			return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		}

		/// <summary>Enabled steps in the order the run executes them (StepOrder, then id for a stable tie-break).</summary>
		public static IEnumerable<WorkflowStep> OrderedEnabledSteps(IEnumerable<WorkflowStep> steps) =>
			(steps ?? Enumerable.Empty<WorkflowStep>())
				.Where(s => s != null && s.IsEnabled)
				.OrderBy(s => s.StepOrder)
				.ThenBy(s => s.WorkflowStepId ?? string.Empty, StringComparer.Ordinal);

		public static string NormalizeHost(string host) =>
			string.IsNullOrWhiteSpace(host) ? null : host.Trim().TrimEnd('.').ToLowerInvariant();

		private static string NormalizeText(string text) =>
			text?.Replace("\r\n", "\n").Replace("\r", "\n");

		private static JToken CanonicalizeActionConfig(string actionConfig)
		{
			if (string.IsNullOrWhiteSpace(actionConfig))
				return JValue.CreateNull();

			try
			{
				// No date or float coercion: a date-looking header value parsed to a local DateTime would make the
				// fingerprint depend on the host's time zone, and web and worker hosts must agree byte for byte.
				using var reader = new JsonTextReader(new System.IO.StringReader(actionConfig))
				{
					DateParseHandling = DateParseHandling.None,
					FloatParseHandling = FloatParseHandling.Decimal
				};
				return Sort(JToken.ReadFrom(reader));
			}
			catch (JsonException)
			{
				// Not JSON (a template that only becomes JSON when rendered): the raw text is the configuration.
				return new JValue(NormalizeText(actionConfig));
			}
		}

		private static JToken Sort(JToken token)
		{
			switch (token)
			{
				case JObject obj:
					var sorted = new JObject();
					foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
						sorted[property.Name] = Sort(property.Value);
					return sorted;
				case JArray array:
					return new JArray(array.Select(Sort));
				default:
					return token.DeepClone();
			}
		}
	}

	/// <summary>Fingerprint inputs beyond the steps, fields and hosts.</summary>
	public sealed class ProtectedFingerprintExtras
	{
		/// <summary>The pinned credential's OAuth2 client authentication (client_secret / private_key_jwt), or null.</summary>
		public string AuthMethod { get; set; }

		public bool AllowsRestricted { get; set; }

		public bool AllowsPart2 { get; set; }

		/// <summary>
		/// Released custom field id (calls.udf#name) to its current sensitivity (UdfFieldSensitivity), or -1 when the field
		/// no longer exists or is disabled. A retag or removal changes the fingerprint.
		/// </summary>
		public IReadOnlyDictionary<string, int> FieldSensitivities { get; set; }
	}
}
