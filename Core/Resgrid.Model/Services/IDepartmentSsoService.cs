using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Service for managing department SSO/SAML/OIDC configuration, security policies,
	/// and the provisioning/linking of external identity provider users.
	/// </summary>
	public interface IDepartmentSsoService
	{
		// ── SSO Config CRUD ───────────────────────────────────────────────────

		/// <summary>Returns all SSO configurations for a department.</summary>
		Task<IEnumerable<DepartmentSsoConfig>> GetSsoConfigsForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Returns the SSO configuration for a specific provider type, or null if none exists.</summary>
		Task<DepartmentSsoConfig> GetSsoConfigForDepartmentAsync(int departmentId, SsoProviderType providerType, CancellationToken cancellationToken = default);

		/// <summary>Returns the SSO configuration matching a SAML EntityId, or null if not found.</summary>
		Task<DepartmentSsoConfig> GetSsoConfigByEntityIdAsync(string entityId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves (creates or updates) an SSO configuration.
		/// Sensitive fields are encrypted using <see cref="IEncryptionService.EncryptForDepartment"/>
		/// before persisting.
		/// </summary>
		Task<DepartmentSsoConfig> SaveSsoConfigAsync(DepartmentSsoConfig config, string departmentCode, CancellationToken cancellationToken = default);

		/// <summary>Deletes the SSO configuration for a specific provider type.</summary>
		Task<bool> DeleteSsoConfigAsync(int departmentId, SsoProviderType providerType, CancellationToken cancellationToken = default);

		// ── Security Policy CRUD ──────────────────────────────────────────────

		/// <summary>Returns the security policy for a department, or null if none is configured.</summary>
		Task<DepartmentSecurityPolicy> GetSecurityPolicyForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Saves (creates or updates) the security policy for a department.</summary>
		Task<DepartmentSecurityPolicy> SaveSecurityPolicyAsync(DepartmentSecurityPolicy policy, CancellationToken cancellationToken = default);

		// ── Token Validation & User Provisioning ──────────────────────────────

		/// <summary>
		/// Validates the external token/assertion against the department's SSO configuration.
		/// Returns a <see cref="ClaimsPrincipal"/> on success, or null on failure.
		/// For OIDC, <paramref name="externalToken"/> is a raw id_token JWT.
		/// For SAML 2.0, <paramref name="externalToken"/> is the base64-encoded SAMLResponse.
		/// </summary>
		Task<ClaimsPrincipal> ValidateExternalTokenAsync(int departmentId, SsoProviderType providerType, string externalToken, string departmentCode, CancellationToken cancellationToken = default);

		/// <summary>
		/// Provisions a new user or links an existing user from the supplied <paramref name="externalClaims"/>.
		/// <list type="bullet">
		///   <item>When <see cref="DepartmentSsoConfig.AutoProvisionUsers"/> is true and no match is found by email,
		///         a new <c>IdentityUser</c>, <c>UserProfile</c>, and <c>DepartmentMember</c> are created.</item>
		///   <item>When the user already exists (matched by email or <see cref="DepartmentMember.ExternalSsoId"/>),
		///         the <c>ExternalSsoId</c> and <c>LastSsoLoginOn</c> are updated.</item>
		/// </list>
		/// Returns the resolved (or newly created) <c>IdentityUser</c>, or null if provisioning is disabled
		/// and no existing user matches.
		/// </summary>
		Task<Identity.IdentityUser> ProvisionOrLinkUserAsync(int departmentId, ClaimsPrincipal externalClaims, DepartmentSsoConfig config, string departmentCode, CancellationToken cancellationToken = default);

		// ── Policy Enforcement ────────────────────────────────────────────────

		/// <summary>
		/// Enforces the department's <see cref="DepartmentSecurityPolicy"/> at login time.
		/// Checks allowed IP ranges, MFA requirement, and SSO-only enforcement.
		/// Returns null on success, or a human-readable failure reason string.
		/// </summary>
		Task<string> EnforceSecurityPolicyAsync(int departmentId, string userId, string clientIpAddress, bool mfaCompleted, bool loginViaSso, CancellationToken cancellationToken = default);

		// ── SCIM helpers ──────────────────────────────────────────────────────

		/// <summary>
		/// Validates the inbound SCIM bearer token for a department.
		/// Returns true if the token matches the stored (encrypted) SCIM token.
		/// </summary>
		Task<bool> ValidateScimBearerTokenAsync(int departmentId, string bearerToken, string departmentCode, CancellationToken cancellationToken = default);

		// ── Optional-feature guards ────────────────────────────────────────────

		/// <summary>
		/// Returns true only if the department has at least one <em>enabled</em> SSO configuration.
		/// Use this to fast-path the common case: departments without SSO skip all SSO overhead
		/// and are completely unaffected by the SSO/SCIM feature.
		/// </summary>
		Task<bool> IsSsoEnabledForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns true only if the department has a saved security policy with
		/// <see cref="DepartmentSecurityPolicy.RequireSso"/> set to true.
		/// Departments that have never saved a policy always return false.
		/// </summary>
		Task<bool> IsRequireSsoPolicyActiveAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns true only if the department has a saved security policy with
		/// <see cref="DepartmentSecurityPolicy.RequireMfa"/> set to true.
		/// Departments that have never saved a policy always return false.
		/// </summary>
		Task<bool> IsRequireMfaPolicyActiveAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}

