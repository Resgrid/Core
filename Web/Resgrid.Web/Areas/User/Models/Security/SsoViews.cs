using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Web.Areas.User.Models.Security
{
	/// <summary>View model for the SSO configuration list page.</summary>
	public class SsoIndexView
	{
		public bool IsAdmin { get; set; }
		public List<SsoConfigRowView> Configs { get; set; } = new();
		public bool HasOidcConfig { get; set; }
		public bool HasSamlConfig { get; set; }
		/// <summary>Symmetrically encrypted token carrying {departmentId}:{departmentCode} for use in public URLs.</summary>
		public string EncryptedDepartmentToken { get; set; }
		public string ScimBaseUrl { get; set; }
		public string ApiBaseUrl { get; set; }
		public string SsoDiscoveryUrl { get; set; }
		/// <summary>Numeric department identifier, used when displaying SCIM API usage examples.</summary>
		public int DepartmentId { get; set; }
		/// <summary>One-time plaintext SCIM bearer token shown immediately after generation/rotation on this page.</summary>
		public string NewScimBearerToken { get; set; }
		/// <summary>ID of the SSO config whose token was just generated, so the UI can scroll/highlight it.</summary>
		public string NewScimConfigId { get; set; }
	}

	public class SsoConfigRowView
	{
		public string DepartmentSsoConfigId { get; set; }
		public string ProviderType { get; set; }
		public bool IsEnabled { get; set; }
		public string Identifier { get; set; }
		public string EndpointUrl { get; set; }
		public bool AllowLocalLogin { get; set; }
		public bool AutoProvisionUsers { get; set; }
		public bool ScimEnabled { get; set; }
		public bool HasScimBearerToken { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	/// <summary>View model for the Create / Edit SSO config wizard.</summary>
	public class SsoConfigEditView
	{
		public bool IsNew { get; set; } = true;
		public string DepartmentSsoConfigId { get; set; }

		[Required]
		public string ProviderType { get; set; }
		public SelectList ProviderTypes { get; set; }

		public bool IsEnabled { get; set; } = true;

		// ── OIDC ─────────────────────────────────────────────────────────────
		public string ClientId { get; set; }
		/// <summary>Plaintext — never pre-populated; write-only on save.</summary>
		public string ClientSecret { get; set; }
		public string Authority { get; set; }

		// ── SAML 2.0 ─────────────────────────────────────────────────────────
		public string MetadataUrl { get; set; }
		public string EntityId { get; set; }
		public string AssertionConsumerServiceUrl { get; set; }
		public string IdpSsoUrl { get; set; }
		public string IdpCertificate { get; set; }
		public string SigningCertificate { get; set; }

		// ── Shared ────────────────────────────────────────────────────────────
		public string AttributeMappingJson { get; set; }
		public bool AllowLocalLogin { get; set; } = true;
		public bool AutoProvisionUsers { get; set; }
		public int? DefaultRankId { get; set; }
		public SelectList RankList { get; set; }
		public bool ScimEnabled { get; set; }

		// ── Secret presence flags (read-only, from API) ───────────────────────
		public bool HasClientSecret { get; set; }
		public bool HasIdpCertificate { get; set; }
		public bool HasSigningCertificate { get; set; }

		// ── Context for the view ──────────────────────────────────────────────
		public string AcsUrl { get; set; }
		public string ApiBaseUrl { get; set; }

		/// <summary>The OIDC redirect URI for brokered sign-in, which the department registers with its IdP.</summary>
		public string OidcBrokerRedirectUri { get; set; }

		/// <summary>
		/// Each app's own redirect URIs for sign-in the app runs itself (older app versions, or while brokered sign-in is off):
		/// its native scheme, and its web edition's page where this deployment serves one. The department registers every one
		/// with its IdP; an app can only receive a redirect on an address it owns.
		/// </summary>
		public IReadOnlyList<LegacyAppCallbacks.AppRedirectUri> OidcAppRedirectUris =>
			LegacyAppCallbacks.RedirectUris(Resgrid.Config.SsoConfig.AppWebOrigins);
	}

	/// <summary>View model for the security policy page.</summary>
	public class SecurityPolicyEditView
	{
		public int DepartmentSecurityPolicyId { get; set; }

		public bool RequireMfa { get; set; }
		public bool RequireSso { get; set; }
		public bool HasActiveSsoConfig { get; set; }

		[Range(0, 10080, ErrorMessage = "Must be between 0 and 10080 minutes.")]
		public int SessionTimeoutMinutes { get; set; }

		[Range(0, 100, ErrorMessage = "Must be between 0 and 100.")]
		public int MaxConcurrentSessions { get; set; }

		public string AllowedIpRanges { get; set; }

		[Range(0, 3650)]
		public int PasswordExpirationDays { get; set; }

		/// <summary>
		/// Minimum password length the department enforces. Must be 0 (use system default of 8)
		/// or ≥ 8. Values 1–7 are invalid and will be rejected at validation.
		/// </summary>
		[MinPasswordLength]
		public int MinPasswordLength { get; set; } = 8;


		public int DataClassificationLevel { get; set; }
		public SelectList DataClassificationLevels { get; set; }

		// Second-factor methods (passkey plan section 10.1). Defaults match a department with no policy row.
		public bool AllowPasskeysForLoginMfa { get; set; } = true;
		public bool AllowPasskeysForAdp { get; set; } = true;
		public bool AllowFederatedMfaForLoginMfa { get; set; }
		public bool AllowFederatedMfaForAdp { get; set; }
		public bool AllowResponderApproval { get; set; } = true;
		public bool AcceptRecentLoginMfaForAdp { get; set; } = true;
		public bool AcceptRecentUnlockMfaForAdp { get; set; } = true;

		/// <summary>Only the managing member changes which methods are accepted; other administrators see them read-only.</summary>
		public bool CanChangeMethodSwitches { get; set; }

		/// <summary>Whether this deployment accepts passkeys yet; the switches take effect once it does.</summary>
		public bool PasskeysAvailable { get; set; }
		public bool ResponderApprovalAvailable { get; set; }
		public bool ProviderStepUpAvailable { get; set; }

		// ── Shared vehicle and workstation devices (passkey plan section 10.5); managing member only ──

		public int SharedIdleLockMinutes { get; set; } = SharedSessionRules.DefaultIdleLockMinutes;
		public int SharedShiftHours { get; set; } = SharedSessionRules.DefaultShiftHours;
		public bool RequireSharedModeForUnit { get; set; }
		public bool RequireSharedModeForCommand { get; set; }
		public bool RequireSharedModeForDispatch { get; set; }

		/// <summary>Whether this deployment offers shared-device mode yet; a new requirement can only be added once it does.</summary>
		public bool SharedDeviceModeAvailable { get; set; }
		public int MaxSharedIdleLockMinutes { get; set; }
		public int MaxSharedShiftHours { get; set; }

		public int SharedModeRequiredApps =>
			(RequireSharedModeForUnit ? (int)SharedModeApps.Unit : 0) |
			(RequireSharedModeForCommand ? (int)SharedModeApps.Command : 0) |
			(RequireSharedModeForDispatch ? (int)SharedModeApps.Dispatch : 0);
	}

	/// <summary>
	/// View model for the provider step-up mapping page (passkey plan section 7.8). The value lists are edited one value per
	/// line; everything about the stored mapping (its version, test and who may change it) comes from the server, never the form.
	/// </summary>
	public class FederatedMfaEditView
	{
		public string RequestAcrValues { get; set; }
		public string RequestClaims { get; set; }
		public string RequestAuthnContextClassRefs { get; set; }
		public string AcceptAmr { get; set; }
		public string AcceptAcr { get; set; }
		public string AcceptAcrs { get; set; }
		public string AcceptAuthnContextClassRefs { get; set; }

		public bool HasActiveSsoConfig { get; set; }
		public bool IsOidc { get; set; }
		public bool CanChange { get; set; }
		public bool ProviderStepUpAvailable { get; set; }
		public bool HasMapping { get; set; }
		public long MappingVersion { get; set; }
		public bool Effective { get; set; }
		public DateTime? TestedOnUtc { get; set; }

		/// <summary>The mapping the form describes. Blank lines are dropped; everything else is left for validation to judge.</summary>
		public Resgrid.Model.Security.FederatedMfaMapping ToMapping() => new()
		{
			RequestAcrValues = Values(RequestAcrValues),
			RequestClaims = string.IsNullOrWhiteSpace(RequestClaims) ? null : RequestClaims.Trim(),
			RequestAuthnContextClassRefs = Values(RequestAuthnContextClassRefs),
			AcceptAmr = Values(AcceptAmr),
			AcceptAcr = Values(AcceptAcr),
			AcceptAcrs = Values(AcceptAcrs),
			AcceptAuthnContextClassRefs = Values(AcceptAuthnContextClassRefs)
		};

		public void CopyFrom(Resgrid.Model.Security.FederatedMfaMapping mapping)
		{
			RequestAcrValues = Lines(mapping?.RequestAcrValues);
			RequestClaims = mapping?.RequestClaims;
			RequestAuthnContextClassRefs = Lines(mapping?.RequestAuthnContextClassRefs);
			AcceptAmr = Lines(mapping?.AcceptAmr);
			AcceptAcr = Lines(mapping?.AcceptAcr);
			AcceptAcrs = Lines(mapping?.AcceptAcrs);
			AcceptAuthnContextClassRefs = Lines(mapping?.AcceptAuthnContextClassRefs);
		}

		private static List<string> Values(string lines)
		{
			var values = (lines ?? string.Empty).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
			return values.Count == 0 ? null : values;
		}

		private static string Lines(IEnumerable<string> values) => values == null ? null : string.Join("\n", values);
	}

	/// <summary>View model for the SCIM setup page.</summary>
	public class ScimSetupView
	{
		public string DepartmentSsoConfigId { get; set; }
		public string ProviderType { get; set; }
		public bool ScimEnabled { get; set; }
		public bool HasScimBearerToken { get; set; }

		/// <summary>One-time-visible plaintext token shown immediately after rotation.</summary>
		public string NewScimBearerToken { get; set; }

		public string ScimBaseUrl { get; set; }
		public string EncryptedDepartmentToken { get; set; }
		public int DepartmentId { get; set; }
	}
}

