using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>One blocking problem with a workflow's protected configuration.</summary>
	public sealed class ProtectedWorkflowValidationError
	{
		public ProtectedWorkflowValidationError(string code, string workflowStepId = null)
		{
			Code = code;
			WorkflowStepId = workflowStepId;
		}

		/// <summary>Stable code; the UI maps it to localized text (ValidationError_{code}).</summary>
		public string Code { get; }

		public string WorkflowStepId { get; }
	}

	public sealed class ProtectedWorkflowValidationResult
	{
		public List<ProtectedWorkflowValidationError> Errors { get; } = new List<ProtectedWorkflowValidationError>();

		/// <summary>Non-blocking advice (for example a protected value without an escape helper). Never stops a request.</summary>
		public List<ProtectedWorkflowValidationError> Warnings { get; } = new List<ProtectedWorkflowValidationError>();

		public bool IsValid => Errors.Count == 0;

		/// <summary>True when any enabled step writes values from its response into the call's subject identifiers.</summary>
		public bool CapturesResponse { get; set; }

		/// <summary>The single literal https host every step posts to, when there is exactly one.</summary>
		public string DestinationHost { get; set; }

		/// <summary>The single credential every step uses, when there is exactly one.</summary>
		public string WorkflowCredentialId { get; set; }

		/// <summary>Maps to <see cref="WorkflowCredentialType"/> for the pinned credential.</summary>
		public int? CredentialType { get; set; }

		public void Add(string code, string stepId = null)
		{
			if (!Errors.Any(e => e.Code == code && e.WorkflowStepId == stepId))
				Errors.Add(new ProtectedWorkflowValidationError(code, stepId));
		}
	}

	/// <summary>
	/// Pure structural validation of a workflow before a protected release can be requested or approved, and again
	/// before every protected send. Codes are value-free and stable.
	/// </summary>
	public static class ProtectedWorkflowValidator
	{
		public const string NoSteps = "no_steps";
		public const string ActionNotAllowed = "action_not_allowed";
		public const string CredentialRequired = "credential_required";
		public const string CredentialMismatch = "credential_mismatch";
		public const string CredentialNotAllowed = "credential_not_allowed";
		public const string CredentialMissing = "credential_missing";
		public const string UrlRequired = "url_required";
		public const string SchemeNotHttps = "scheme_not_https";
		public const string HostNotLiteral = "host_not_literal";
		public const string HostMismatch = "host_mismatch";
		public const string ProtectedInActionConfig = "protected_in_action_config";
		public const string ProtectedInCondition = "protected_in_condition";
		public const string TriggerNotSupported = "trigger_not_supported";
		public const string TokenUrlInvalid = "token_url_invalid";
		public const string ProtectedWithoutRelease = "protected_reference_without_release";
		public const string SigningKeyMissing = "signing_key_missing";

		/// <summary>Warning: a protected value is placed in a structured payload without json_escape, xml_escape or hl7_escape.</summary>
		public const string UnescapedProtectedValue = "unescaped_protected_value";

		/// <summary>The template helpers that make a value safe inside JSON, XML and HL7 v2.</summary>
		public static readonly string[] EscapeHelpers = { "json_escape", "xml_escape", "hl7_escape" };

		private static readonly Regex CodeBlock = new(@"\{\{(.*?)\}\}", RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex ProtectedReference = new(@"(?<![\w.])protected\s*(\.|\[)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex LoopHeader = new(@"^\s*for\s+\w+\s+in\s+protected(\.[\w]+)+\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private static readonly Regex HttpsAuthority = new(@"^\s*https://(?<host>[^/:?#\s]+)(:(?<port>\d{1,5}))?(?=[/?#]|\s*$)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

		/// <summary>Credential types a protected step may authenticate with.</summary>
		public static bool IsAllowedCredentialType(int credentialType, bool allowHttpBasic) =>
			credentialType == (int)WorkflowCredentialType.HttpBearer ||
			credentialType == (int)WorkflowCredentialType.HttpApiKey ||
			credentialType == (int)WorkflowCredentialType.OAuth2ClientCredentials ||
			(allowHttpBasic && credentialType == (int)WorkflowCredentialType.HttpBasic);

		public static bool IsAllowedActionType(int actionType) =>
			actionType == (int)WorkflowActionType.CallApiPost || actionType == (int)WorkflowActionType.CallApiPut;

		/// <summary>True when a Scriban template references the protected.* namespace inside a code block.</summary>
		public static bool ReferencesProtectedNamespace(string template)
		{
			if (string.IsNullOrWhiteSpace(template) || template.IndexOf("protected", StringComparison.Ordinal) < 0)
				return false;

			foreach (Match block in CodeBlock.Matches(template))
				if (ProtectedReference.IsMatch(block.Groups[1].Value))
					return true;

			return false;
		}

		/// <summary>
		/// True when a code block reads a protected value without passing it through an escape helper. Advice only: a
		/// note with a quote or a line break can break the JSON, XML or HL7 structure it is placed in.
		/// </summary>
		public static bool HasUnescapedProtectedReference(string template)
		{
			if (string.IsNullOrWhiteSpace(template) || template.IndexOf("protected", StringComparison.Ordinal) < 0)
				return false;

			foreach (Match block in CodeBlock.Matches(template))
			{
				var code = block.Groups[1].Value;
				// "for field in protected.call.udf" only chooses what to loop over; the values it yields are checked where they are output.
				if (LoopHeader.IsMatch(code))
					continue;
				if (ProtectedReference.IsMatch(code) && !EscapeHelpers.Any(h => Regex.IsMatch(code, @"(?<![\w.])" + h + @"(?!\w)", RegexOptions.CultureInvariant)))
					return true;
			}

			return false;
		}

		/// <summary>The Url value of an HTTP step's action config (case-insensitive key), or null.</summary>
		public static string ReadUrl(string actionConfigJson)
		{
			if (string.IsNullOrWhiteSpace(actionConfigJson))
				return null;
			try
			{
				var config = JObject.Parse(actionConfigJson);
				var token = config.GetValue("Url", StringComparison.OrdinalIgnoreCase);
				return token?.Type == JTokenType.String ? ((string)token)?.Trim() : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		/// <summary>
		/// Parses the literal host of an https URL. Returns false (with a code) for a non-https scheme, a missing URL,
		/// or a host that contains template syntax — the destination must be pinned before anything renders.
		/// </summary>
		public static bool TryParseLiteralHttpsHost(string url, out string host, out string errorCode)
		{
			host = null;
			errorCode = null;
			if (string.IsNullOrWhiteSpace(url))
			{
				errorCode = UrlRequired;
				return false;
			}

			if (!url.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				errorCode = SchemeNotHttps;
				return false;
			}

			var match = HttpsAuthority.Match(url);
			if (!match.Success)
			{
				errorCode = HostNotLiteral;
				return false;
			}

			var candidate = match.Groups["host"].Value;
			if (candidate.IndexOfAny(new[] { '{', '}', '%', '@', '*' }) >= 0 || candidate.Contains(".."))
			{
				errorCode = HostNotLiteral;
				return false;
			}

			host = ProtectedWorkflowFingerprint.NormalizeHost(candidate);
			return !string.IsNullOrEmpty(host);
		}

		/// <summary>Host of an already-rendered absolute URL (runtime check); false unless the scheme is https.</summary>
		public static bool TryGetRenderedHttpsHost(string url, out string host, out string errorCode)
		{
			host = null;
			errorCode = null;
			if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
			{
				errorCode = UrlRequired;
				return false;
			}

			if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
			{
				errorCode = SchemeNotHttps;
				return false;
			}

			if (!string.IsNullOrEmpty(uri.UserInfo))
			{
				errorCode = HostNotLiteral;
				return false;
			}

			host = ProtectedWorkflowFingerprint.NormalizeHost(uri.IdnHost);
			return !string.IsNullOrEmpty(host);
		}

		/// <summary>
		/// Validates every step of a workflow for protected use. <paramref name="credentialTypes"/> maps credential id
		/// to WorkflowCredentialType (missing ids are reported as credential_missing).
		/// </summary>
		public static ProtectedWorkflowValidationResult Validate(int triggerEventType, IEnumerable<WorkflowStep> steps,
			IReadOnlyDictionary<string, int> credentialTypes, bool allowHttpBasic, int maxCaptureEntries = 5)
		{
			var result = new ProtectedWorkflowValidationResult();
			if (!ProtectedWorkflowFieldCatalog.IsSupportedTrigger(triggerEventType))
				result.Add(TriggerNotSupported);

			var allSteps = (steps ?? Enumerable.Empty<WorkflowStep>()).Where(s => s != null).ToList();
			var enabled = ProtectedWorkflowFingerprint.OrderedEnabledSteps(allSteps).ToList();
			if (enabled.Count == 0)
				result.Add(NoSteps);

			// Any other action type in the workflow blocks — a disabled email step is one toggle away from running.
			foreach (var step in allSteps.Where(s => !IsAllowedActionType(s.ActionType)))
				result.Add(ActionNotAllowed, step.WorkflowStepId);

			var hosts = new HashSet<string>(StringComparer.Ordinal);
			var credentialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var step in enabled)
			{
				if (ReferencesProtectedNamespace(step.ActionConfig))
					result.Add(ProtectedInActionConfig, step.WorkflowStepId);
				if (ReferencesProtectedNamespace(step.ConditionExpression))
					result.Add(ProtectedInCondition, step.WorkflowStepId);

				if (string.IsNullOrWhiteSpace(step.WorkflowCredentialId))
				{
					result.Add(CredentialRequired, step.WorkflowStepId);
				}
				else
				{
					credentialIds.Add(step.WorkflowCredentialId.Trim());
					if (credentialTypes == null || !credentialTypes.TryGetValue(step.WorkflowCredentialId.Trim(), out var type))
						result.Add(CredentialMissing, step.WorkflowStepId);
					else if (!IsAllowedCredentialType(type, allowHttpBasic))
						result.Add(CredentialNotAllowed, step.WorkflowStepId);
				}

				if (IsAllowedActionType(step.ActionType))
				{
					if (TryParseLiteralHttpsHost(ReadUrl(step.ActionConfig), out var host, out var urlError))
						hosts.Add(host);
					else
						result.Add(urlError, step.WorkflowStepId);

					// Content type, success rule, capture and idempotency options (EHR integration).
					var options = ProtectedStepOptions.Read(step.ActionConfig, out var optionErrors, maxCaptureEntries);
					foreach (var code in optionErrors)
						result.Add(code, step.WorkflowStepId);
					if (options.ResponseCapture.Count > 0)
						result.CapturesResponse = true;

					if (options.MediaType != "text/plain" && HasUnescapedProtectedReference(step.OutputTemplate) &&
						!result.Warnings.Any(w => w.WorkflowStepId == step.WorkflowStepId))
						result.Warnings.Add(new ProtectedWorkflowValidationError(UnescapedProtectedValue, step.WorkflowStepId));
				}
			}

			if (hosts.Count > 1)
				result.Add(HostMismatch);
			else if (hosts.Count == 1)
				result.DestinationHost = hosts.First();

			if (credentialIds.Count > 1)
				result.Add(CredentialMismatch);
			else if (credentialIds.Count == 1)
			{
				result.WorkflowCredentialId = credentialIds.First();
				if (credentialTypes != null && credentialTypes.TryGetValue(result.WorkflowCredentialId, out var pinnedType))
					result.CredentialType = pinnedType;
			}

			return result;
		}
	}
}
