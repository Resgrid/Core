using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

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

		[Range(0, 128)]
		public int MinPasswordLength { get; set; } = 8;

		public bool RequirePasswordComplexity { get; set; }

		public int DataClassificationLevel { get; set; }
		public SelectList DataClassificationLevels { get; set; }
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

