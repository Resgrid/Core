using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Stores the SSO/SAML/OIDC identity-provider configuration for a department.
	/// Sensitive fields (client secret, certificates, SCIM token) are encrypted at rest
	/// via <see cref="Resgrid.Model.Services.IEncryptionService.EncryptForDepartment"/>.
	/// </summary>
	[Table("DepartmentSsoConfigs")]
	public class DepartmentSsoConfig : IEntity
	{
		/// <summary>Unique identifier (GUID string).</summary>
		[Key]
		[MaxLength(128)]
		public string DepartmentSsoConfigId { get; set; }

		/// <summary>Owning department.</summary>
		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		/// <summary>Which SSO protocol this record configures. Maps to <see cref="SsoProviderType"/>.</summary>
		[Required]
		public int SsoProviderType { get; set; }

		/// <summary>Whether SSO is active for this department.</summary>
		[Required]
		public bool IsEnabled { get; set; }

		// ── OIDC / shared fields ───────────────────────────────────────────────

		/// <summary>OAuth 2.0 / OIDC client identifier registered with the IdP.</summary>
		[MaxLength(512)]
		public string ClientId { get; set; }

		/// <summary>AES-encrypted OIDC client secret (encrypted via IEncryptionService.EncryptForDepartment).</summary>
		public string EncryptedClientSecret { get; set; }

		/// <summary>OIDC authority / issuer URL (e.g. https://login.microsoftonline.com/{tenant}/v2.0).</summary>
		[MaxLength(1024)]
		public string Authority { get; set; }

		// ── SAML 2.0 specific fields ──────────────────────────────────────────

		/// <summary>SAML metadata URL for automatic IdP configuration retrieval.</summary>
		[MaxLength(1024)]
		public string MetadataUrl { get; set; }

		/// <summary>SAML entity ID (SP entity ID registered with the IdP).</summary>
		[MaxLength(512)]
		public string EntityId { get; set; }

		/// <summary>SAML Assertion Consumer Service (ACS) URL.</summary>
		[MaxLength(1024)]
		public string AssertionConsumerServiceUrl { get; set; }

		/// <summary>AES-encrypted IdP public certificate (PEM) used to verify SAML assertions.</summary>
		public string EncryptedIdpCertificate { get; set; }

		/// <summary>AES-encrypted SP signing certificate private key (PEM) used to sign SAML requests.</summary>
		public string EncryptedSigningCertificate { get; set; }

		// ── Claim / attribute mapping ─────────────────────────────────────────

		/// <summary>
		/// JSON object mapping IdP claim names → Resgrid user fields
		/// (e.g. {"email":"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress","firstName":"given_name","lastName":"family_name"}).
		/// </summary>
		public string AttributeMappingJson { get; set; }

		// ── Provisioning policy ───────────────────────────────────────────────

		/// <summary>When true, users may also log in with their local username/password in addition to SSO.</summary>
		[Required]
		public bool AllowLocalLogin { get; set; } = true;

		/// <summary>When true, users arriving via SSO who do not yet exist are automatically provisioned.</summary>
		[Required]
		public bool AutoProvisionUsers { get; set; }

		/// <summary>Optional default rank assigned to auto-provisioned users.</summary>
		[ForeignKey("DefaultRank")]
		public int? DefaultRankId { get; set; }

		public virtual Rank DefaultRank { get; set; }

		// ── SCIM 2.0 ─────────────────────────────────────────────────────────

		/// <summary>Whether SCIM 2.0 provisioning is enabled for this department.</summary>
		[Required]
		public bool ScimEnabled { get; set; }

		/// <summary>AES-encrypted SCIM 2.0 bearer token used to authenticate inbound SCIM requests.</summary>
		public string EncryptedScimBearerToken { get; set; }

		// ── Audit ─────────────────────────────────────────────────────────────

		[Required]
		public string CreatedByUserId { get; set; }

		[Required]
		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		// ── IEntity ──────────────────────────────────────────────────────────

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => DepartmentSsoConfigId;
			set => DepartmentSsoConfigId = (string)value;
		}

		[NotMapped] public string TableName => "DepartmentSsoConfigs";
		[NotMapped] public string IdName => "DepartmentSsoConfigId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "Department", "DefaultRank" };
	}
}

