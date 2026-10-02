using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A department's provider step-up mapping (passkey plan section 7.8): what Resgrid asks the identity provider for, and
	/// which values the provider returns that count as MFA. It is a validated structure, never free text, and it takes
	/// effect only after a successful test step-up with the same version (<see cref="IsTested"/>).
	/// </summary>
	public sealed class FederatedMfaMapping
	{
		private const int MaxValues = 10;
		private const int MaxValueLength = 256;
		private const int MaxClaimsLength = 2048;

		private static readonly JsonSerializerOptions Json = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		// ── What to request ─────────────────────────────────────────────────────────

		/// <summary>OIDC <c>acr_values</c> to request, such as an Okta assurance level.</summary>
		public List<string> RequestAcrValues { get; set; }

		/// <summary>An OIDC <c>claims</c> request (a JSON object), such as an Entra authentication-context <c>acrs</c> value.</summary>
		public string RequestClaims { get; set; }

		/// <summary>SAML <c>RequestedAuthnContext</c> class references, such as the REFEDS MFA profile.</summary>
		public List<string> RequestAuthnContextClassRefs { get; set; }

		// ── What counts as MFA (the response must carry at least one) ──────────────────

		public List<string> AcceptAmr { get; set; }
		public List<string> AcceptAcr { get; set; }
		public List<string> AcceptAcrs { get; set; }
		public List<string> AcceptAuthnContextClassRefs { get; set; }

		public string Serialize() => JsonSerializer.Serialize(this, Json);

		/// <summary>The mapping in a stored or submitted JSON document, or null when it is empty or unreadable.</summary>
		public static FederatedMfaMapping Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				return JsonSerializer.Deserialize<FederatedMfaMapping>(json, Json);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		/// <summary>Why the mapping cannot be used with the provider type; null when it is valid.</summary>
		public static string Validate(FederatedMfaMapping mapping, SsoProviderType providerType)
		{
			if (mapping == null)
				return "The mapping is empty or is not valid JSON.";

			foreach (var (name, values) in new[]
			{
				("requestAcrValues", mapping.RequestAcrValues), ("requestAuthnContextClassRefs", mapping.RequestAuthnContextClassRefs),
				("acceptAmr", mapping.AcceptAmr), ("acceptAcr", mapping.AcceptAcr), ("acceptAcrs", mapping.AcceptAcrs),
				("acceptAuthnContextClassRefs", mapping.AcceptAuthnContextClassRefs)
			})
			{
				var problem = ValidateValues(name, values);
				if (problem != null)
					return problem;
			}

			// A password is never a second factor, whatever the provider calls it.
			if (mapping.AcceptAmr?.Any(value => string.Equals(value, "pwd", StringComparison.OrdinalIgnoreCase)) == true)
				return "acceptAmr cannot accept \"pwd\": a password is not MFA.";

			if (providerType == SsoProviderType.Oidc)
			{
				if (mapping.RequestAuthnContextClassRefs?.Count > 0 || mapping.AcceptAuthnContextClassRefs?.Count > 0)
					return "SAML AuthnContext values do not apply to an OIDC provider.";
				if (Count(mapping.AcceptAmr) + Count(mapping.AcceptAcr) + Count(mapping.AcceptAcrs) == 0)
					return "Name at least one amr, acr or acrs value that counts as MFA.";

				if (!string.IsNullOrWhiteSpace(mapping.RequestClaims))
				{
					if (mapping.RequestClaims.Length > MaxClaimsLength)
						return "requestClaims is too long.";
					try
					{
						using var claims = JsonDocument.Parse(mapping.RequestClaims);
						if (claims.RootElement.ValueKind != JsonValueKind.Object || claims.RootElement.EnumerateObject()
								.Any(member => member.Name != "id_token" && member.Name != "userinfo"))
							return "requestClaims must be a JSON object with id_token and/or userinfo members.";
					}
					catch (JsonException)
					{
						return "requestClaims is not valid JSON.";
					}
				}
			}
			else if (providerType == SsoProviderType.Saml2)
			{
				if (mapping.RequestAcrValues?.Count > 0 || !string.IsNullOrWhiteSpace(mapping.RequestClaims) ||
					Count(mapping.AcceptAmr) + Count(mapping.AcceptAcr) + Count(mapping.AcceptAcrs) > 0)
					return "OIDC values do not apply to a SAML provider.";
				if (Count(mapping.AcceptAuthnContextClassRefs) == 0)
					return "Name at least one AuthnContextClassRef that counts as MFA.";
			}
			else
			{
				return "Unknown provider type.";
			}

			return null;
		}

		/// <summary>
		/// The first returned value this mapping counts as MFA, as <c>kind:value</c> for evidence and audit; null when the
		/// response carries none. Matching is exact and case-sensitive, as providers define these identifiers.
		/// </summary>
		public string Match(FederatedMfaSignals signals)
		{
			if (signals == null)
				return null;

			return First("amr", AcceptAmr, signals.Amr)
				?? First("acr", AcceptAcr, signals.Acr)
				?? First("acrs", AcceptAcrs, signals.Acrs)
				?? First("authncontext", AcceptAuthnContextClassRefs, signals.AuthnContextClassRefs);
		}

		/// <summary>Whether the configuration's mapping has passed a test at its current version (plan section 7.8).</summary>
		public static bool IsTested(DepartmentSsoConfig config) =>
			config != null && config.IsEnabled && !string.IsNullOrWhiteSpace(config.FederatedMfaMappingJson) &&
			config.FederatedMfaMappingVersion > 0 && config.FederatedMfaTestedVersion == config.FederatedMfaMappingVersion;

		/// <summary>
		/// Whether a redeemed brokered round trip carried provider MFA under the department's tested mapping as it is now:
		/// the same configuration and mapping version, with a matched value (plan section 7.8 acceptance rules).
		/// </summary>
		public static bool Satisfies(SsoLoginTransaction transaction, DepartmentSsoConfig testedConfig) =>
			transaction != null && IsTested(testedConfig) && !string.IsNullOrWhiteSpace(transaction.FederatedMfaValue) &&
			transaction.DepartmentId == testedConfig.DepartmentId &&
			string.Equals(transaction.DepartmentSsoConfigId, testedConfig.DepartmentSsoConfigId, StringComparison.Ordinal) &&
			transaction.FederatedMappingVersion == testedConfig.FederatedMfaMappingVersion;

		/// <summary>The MFA evidence factor reference for provider step-up: the SSO configuration and its mapping version.</summary>
		public static string FactorReferenceFor(string departmentSsoConfigId, long mappingVersion) =>
			$"federated:{departmentSsoConfigId}:{mappingVersion}";

		private static string First(string kind, IEnumerable<string> accepted, IEnumerable<string> returned)
		{
			if (accepted == null || returned == null)
				return null;

			var values = returned.Where(value => !string.IsNullOrEmpty(value)).ToHashSet(StringComparer.Ordinal);
			var match = accepted.FirstOrDefault(values.Contains);
			return match == null ? null : $"{kind}:{match}";
		}

		private static string ValidateValues(string name, IReadOnlyCollection<string> values)
		{
			if (values == null)
				return null;
			if (values.Count > MaxValues)
				return $"{name} lists more than {MaxValues} values.";
			if (values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)))
				return $"{name} has an empty value, a value over {MaxValueLength} characters, or a value containing spaces.";
			if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
				return $"{name} lists a value twice.";
			return null;
		}

		private static int Count(IReadOnlyCollection<string> values) => values?.Count ?? 0;
	}

	/// <summary>The MFA signals an identity provider returned: OIDC <c>amr</c>, <c>acr</c>, <c>acrs</c>, or SAML AuthnContext.</summary>
	public sealed class FederatedMfaSignals
	{
		public IReadOnlyCollection<string> Amr { get; init; }
		public IReadOnlyCollection<string> Acr { get; init; }
		public IReadOnlyCollection<string> Acrs { get; init; }
		public IReadOnlyCollection<string> AuthnContextClassRefs { get; init; }
	}
}
