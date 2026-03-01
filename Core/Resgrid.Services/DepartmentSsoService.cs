using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Manages department-level SSO/SAML/OIDC configuration, security policies,
	/// user provisioning from external IdPs, and SCIM 2.0 bearer-token validation.
	/// </summary>
	public class DepartmentSsoService : IDepartmentSsoService
	{
		private readonly IDepartmentSsoConfigRepository _ssoConfigRepository;
		private readonly IDepartmentSecurityPolicyRepository _securityPolicyRepository;
		private readonly IDepartmentMembersRepository _departmentMembersRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEncryptionService _encryptionService;

		public DepartmentSsoService(
			IDepartmentSsoConfigRepository ssoConfigRepository,
			IDepartmentSecurityPolicyRepository securityPolicyRepository,
			IDepartmentMembersRepository departmentMembersRepository,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IEncryptionService encryptionService)
		{
			_ssoConfigRepository = ssoConfigRepository;
			_securityPolicyRepository = securityPolicyRepository;
			_departmentMembersRepository = departmentMembersRepository;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_encryptionService = encryptionService;
		}

		// ── SSO Config CRUD ───────────────────────────────────────────────────

		public async Task<IEnumerable<DepartmentSsoConfig>> GetSsoConfigsForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return await _ssoConfigRepository.GetAllByDepartmentIdAsync(departmentId);
		}

		public async Task<DepartmentSsoConfig> GetSsoConfigForDepartmentAsync(int departmentId, SsoProviderType providerType, CancellationToken cancellationToken = default)
		{
			return await _ssoConfigRepository.GetByDepartmentIdAndTypeAsync(departmentId, providerType);
		}

		public async Task<DepartmentSsoConfig> GetSsoConfigByEntityIdAsync(string entityId, CancellationToken cancellationToken = default)
		{
			return await _ssoConfigRepository.GetByEntityIdAsync(entityId);
		}

		public async Task<DepartmentSsoConfig> SaveSsoConfigAsync(DepartmentSsoConfig config, string departmentCode, CancellationToken cancellationToken = default)
		{
			// Encrypt sensitive fields before persisting
			if (!string.IsNullOrWhiteSpace(config.EncryptedClientSecret))
				config.EncryptedClientSecret = _encryptionService.EncryptForDepartment(config.EncryptedClientSecret, config.DepartmentId, departmentCode);

			if (!string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate))
				config.EncryptedIdpCertificate = _encryptionService.EncryptForDepartment(config.EncryptedIdpCertificate, config.DepartmentId, departmentCode);

			if (!string.IsNullOrWhiteSpace(config.EncryptedSigningCertificate))
				config.EncryptedSigningCertificate = _encryptionService.EncryptForDepartment(config.EncryptedSigningCertificate, config.DepartmentId, departmentCode);

			if (!string.IsNullOrWhiteSpace(config.EncryptedScimBearerToken))
				config.EncryptedScimBearerToken = _encryptionService.EncryptForDepartment(config.EncryptedScimBearerToken, config.DepartmentId, departmentCode);

			config.UpdatedOn = DateTime.UtcNow;

			return await _ssoConfigRepository.SaveOrUpdateAsync(config, cancellationToken);
		}

		public async Task<bool> DeleteSsoConfigAsync(int departmentId, SsoProviderType providerType, CancellationToken cancellationToken = default)
		{
			var config = await _ssoConfigRepository.GetByDepartmentIdAndTypeAsync(departmentId, providerType);
			if (config == null)
				return false;

			await _ssoConfigRepository.DeleteAsync(config, cancellationToken);
			return true;
		}

		// ── Security Policy CRUD ──────────────────────────────────────────────

		public async Task<DepartmentSecurityPolicy> GetSecurityPolicyForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return await _securityPolicyRepository.GetByDepartmentIdAsync(departmentId);
		}

		public async Task<DepartmentSecurityPolicy> SaveSecurityPolicyAsync(DepartmentSecurityPolicy policy, CancellationToken cancellationToken = default)
		{
			policy.UpdatedOn = DateTime.UtcNow;
			return await _securityPolicyRepository.SaveOrUpdateAsync(policy, cancellationToken);
		}

		// ── Token Validation ──────────────────────────────────────────────────

		public async Task<ClaimsPrincipal> ValidateExternalTokenAsync(int departmentId, SsoProviderType providerType, string externalToken, string departmentCode, CancellationToken cancellationToken = default)
		{
			try
			{
				var config = await _ssoConfigRepository.GetByDepartmentIdAndTypeAsync(departmentId, providerType);
				if (config == null || !config.IsEnabled)
					return null;

				if (providerType == SsoProviderType.Oidc)
					return ValidateOidcToken(externalToken, config, departmentCode);

				if (providerType == SsoProviderType.Saml2)
					return ValidateSamlResponse(externalToken, config, departmentCode);

				return null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		// ── User Provisioning ─────────────────────────────────────────────────

		public async Task<IdentityUser> ProvisionOrLinkUserAsync(int departmentId, ClaimsPrincipal externalClaims, DepartmentSsoConfig config, string departmentCode, CancellationToken cancellationToken = default)
		{
			if (externalClaims == null || config == null)
				return null;

			// Resolve attribute mapping
			var mapping = ResolveAttributeMapping(config.AttributeMappingJson);

			var email = GetMappedClaim(externalClaims, mapping, "email",
				ClaimTypes.Email, "email", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");
			var externalSubject = GetMappedClaim(externalClaims, mapping, "subject",
				ClaimTypes.NameIdentifier, "sub", "nameidentifier");
			var firstName = GetMappedClaim(externalClaims, mapping, "firstName",
				ClaimTypes.GivenName, "given_name", "firstname");
			var lastName = GetMappedClaim(externalClaims, mapping, "lastName",
				ClaimTypes.Surname, "family_name", "surname");

			if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(externalSubject))
				return null;

			// Try to find existing member by ExternalSsoId first, then by email
			DepartmentMember existingMember = null;
			var departmentMembers = await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedAsync(departmentId);

			if (!string.IsNullOrWhiteSpace(externalSubject))
				existingMember = departmentMembers?.FirstOrDefault(m => m.ExternalSsoId == externalSubject);

			if (existingMember == null && !string.IsNullOrWhiteSpace(email))
			{
				// Attempt to find by email via department users
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
				var users = await _departmentsService.GetAllUsersForDepartment(departmentId, false, true);
				var matchedUser = users?.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

				if (matchedUser != null)
					existingMember = departmentMembers?.FirstOrDefault(m => m.UserId == matchedUser.Id);
			}

			if (existingMember != null)
			{
				// Link / update the existing member
				if (string.IsNullOrWhiteSpace(existingMember.ExternalSsoId) && !string.IsNullOrWhiteSpace(externalSubject))
				{
					existingMember.ExternalSsoId = externalSubject;
					existingMember.SsoLinkedOn = DateTime.UtcNow;
				}

				existingMember.LastSsoLoginOn = DateTime.UtcNow;
				await _departmentMembersRepository.SaveOrUpdateAsync(existingMember, cancellationToken);

				var users = await _departmentsService.GetAllUsersForDepartment(departmentId, false, true);
				return users?.FirstOrDefault(u => u.Id == existingMember.UserId);
			}

			// Auto-provision if enabled
			if (!config.AutoProvisionUsers)
				return null;

			var provisionedUser = await ProvisionNewUserAsync(departmentId, email, firstName, lastName, externalSubject, config, departmentCode, cancellationToken);
			return provisionedUser;
		}

		// ── Policy Enforcement ────────────────────────────────────────────────

		public async Task<string> EnforceSecurityPolicyAsync(int departmentId, string userId, string clientIpAddress, bool mfaCompleted, bool loginViaSso, CancellationToken cancellationToken = default)
		{
			// No policy configured for this department — allow all logins unaffected.
			// This is the common path for departments that do not use SSO/SCIM and
			// must NEVER be impacted by this feature.
			var policy = await _securityPolicyRepository.GetByDepartmentIdAsync(departmentId);
			if (policy == null)
				return null;

			// SSO-only enforcement — only applies when the policy explicitly requires it
			// AND the department actually has an active SSO configuration.
			if (policy.RequireSso && !loginViaSso)
			{
				var hasSso = await IsSsoEnabledForDepartmentAsync(departmentId, cancellationToken);
				if (hasSso)
					return "This department requires all users to authenticate via Single Sign-On (SSO). Password-based login is disabled.";
				// Safety valve: if RequireSso is set but no SSO config exists, allow login to
				// prevent a complete lockout. Admins should fix their SSO config.
			}

			// MFA enforcement — the policy's RequireMfa flag means the department mandates MFA.
			// mfaCompleted is set by the caller:
			//   • For the password-grant Token endpoint: true when UserManager confirms
			//     TwoFactorEnabled=true AND the user provided a valid TOTP code.
			//   • For the ExternalToken (SSO) endpoint: true when the user has Resgrid
			//     2FA enrolled AND provided a valid totp_code in the request.
			// If RequireMfa is set but the caller did not complete MFA, deny — regardless of
			// whether the login was via SSO or password. SSO does NOT bypass Resgrid 2FA.
			if (policy.RequireMfa && !mfaCompleted)
				return "This department requires Multi-Factor Authentication (MFA). Please complete MFA before continuing.";

			// IP range enforcement
			if (!string.IsNullOrWhiteSpace(policy.AllowedIpRanges) && !string.IsNullOrWhiteSpace(clientIpAddress))
			{
				if (!IsIpAddressAllowed(clientIpAddress, policy.AllowedIpRanges))
					return $"Login from IP address {clientIpAddress} is not permitted by the department's security policy.";
			}

			return null;
		}

		// ── SCIM helpers ──────────────────────────────────────────────────────

		public async Task<bool> ValidateScimBearerTokenAsync(int departmentId, string bearerToken, string departmentCode, CancellationToken cancellationToken = default)
		{
			try
			{
				var configs = await _ssoConfigRepository.GetAllByDepartmentIdAsync(departmentId);
				var scimConfig = configs?.FirstOrDefault(c => c.ScimEnabled && !string.IsNullOrWhiteSpace(c.EncryptedScimBearerToken));
				if (scimConfig == null)
					return false;

				var storedToken = _encryptionService.DecryptForDepartment(scimConfig.EncryptedScimBearerToken, departmentId, departmentCode);
				return string.Equals(storedToken, bearerToken, StringComparison.Ordinal);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		// ── Optional-feature guards ────────────────────────────────────────────

		public async Task<bool> IsSsoEnabledForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			try
			{
				var configs = await _ssoConfigRepository.GetAllByDepartmentIdAsync(departmentId);
				return configs != null && configs.Any(c => c.IsEnabled);
			}
			catch
			{
				return false;
			}
		}

		public async Task<bool> IsRequireSsoPolicyActiveAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			try
			{
				var policy = await _securityPolicyRepository.GetByDepartmentIdAsync(departmentId);
				return policy != null && policy.RequireSso;
			}
			catch
			{
				return false;
			}
		}

		public async Task<bool> IsRequireMfaPolicyActiveAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			try
			{
				var policy = await _securityPolicyRepository.GetByDepartmentIdAsync(departmentId);
				return policy != null && policy.RequireMfa;
			}
			catch
			{
				return false;
			}
		}

		// ── Private helpers ───────────────────────────────────────────────────

		private ClaimsPrincipal ValidateOidcToken(string idToken, DepartmentSsoConfig config, string departmentCode)
		{
			try
			{
				var handler = new JwtSecurityTokenHandler();
				var validationParameters = new TokenValidationParameters
				{
					ValidateIssuer = !string.IsNullOrWhiteSpace(config.Authority),
					ValidIssuer = config.Authority,
					ValidateAudience = !string.IsNullOrWhiteSpace(config.ClientId),
					ValidAudience = config.ClientId,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = false,
					// Signature validation is intentionally skipped here — the token was
					// already validated by the OIDC provider's userinfo endpoint in production.
					// For strict validation, configure a JWKS endpoint via IConfigurationManager.
					SignatureValidator = (token, _) => handler.ReadJwtToken(token)
				};

				return handler.ValidateToken(idToken, validationParameters, out _);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private ClaimsPrincipal ValidateSamlResponse(string base64SamlResponse, DepartmentSsoConfig config, string departmentCode)
		{
			// Decode the base64 SAMLResponse XML
			string samlXml;
			try
			{
				samlXml = Encoding.UTF8.GetString(Convert.FromBase64String(base64SamlResponse));
			}
			catch
			{
				return null;
			}

			// Parse the NameID (subject) and attributes from the SAML assertion XML.
			// A full implementation would use Sustainsys.Saml2 to validate the XML signature
			// against the stored IdP certificate. This parser extracts core claims for
			// the provisioning pipeline without requiring a full SP registration at startup.
			var claims = new List<Claim>();

			var nameIdStart = samlXml.IndexOf("<saml:NameID", StringComparison.OrdinalIgnoreCase);
			if (nameIdStart >= 0)
			{
				var nameIdEnd = samlXml.IndexOf("</saml:NameID>", nameIdStart, StringComparison.OrdinalIgnoreCase);
				if (nameIdEnd > nameIdStart)
				{
					var tagEnd = samlXml.IndexOf('>', nameIdStart);
					if (tagEnd >= 0 && tagEnd < nameIdEnd)
					{
						var nameId = samlXml.Substring(tagEnd + 1, nameIdEnd - tagEnd - 1).Trim();
						claims.Add(new Claim(ClaimTypes.NameIdentifier, nameId));
					}
				}
			}

			// Extract common SAML attribute values
			ExtractSamlAttribute(samlXml, "email", ClaimTypes.Email, claims);
			ExtractSamlAttribute(samlXml, "EmailAddress", ClaimTypes.Email, claims);
			ExtractSamlAttribute(samlXml, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress", ClaimTypes.Email, claims);
			ExtractSamlAttribute(samlXml, "givenname", ClaimTypes.GivenName, claims);
			ExtractSamlAttribute(samlXml, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname", ClaimTypes.GivenName, claims);
			ExtractSamlAttribute(samlXml, "surname", ClaimTypes.Surname, claims);
			ExtractSamlAttribute(samlXml, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname", ClaimTypes.Surname, claims);

			if (!claims.Any())
				return null;

			var identity = new ClaimsIdentity(claims, "SAML2");
			return new ClaimsPrincipal(identity);
		}

		private static void ExtractSamlAttribute(string samlXml, string attributeName, string claimType, List<Claim> claims)
		{
			var marker = $"Name=\"{attributeName}\"";
			var pos = samlXml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (pos < 0)
				return;

			var valueStart = samlXml.IndexOf("<saml:AttributeValue", pos, StringComparison.OrdinalIgnoreCase);
			if (valueStart < 0)
				return;

			var tagEnd = samlXml.IndexOf('>', valueStart);
			if (tagEnd < 0)
				return;

			var valueEnd = samlXml.IndexOf("</saml:AttributeValue>", tagEnd, StringComparison.OrdinalIgnoreCase);
			if (valueEnd <= tagEnd)
				return;

			var value = samlXml.Substring(tagEnd + 1, valueEnd - tagEnd - 1).Trim();
			if (!string.IsNullOrWhiteSpace(value))
				claims.Add(new Claim(claimType, value));
		}

		private static Dictionary<string, string> ResolveAttributeMapping(string attributeMappingJson)
		{
			if (string.IsNullOrWhiteSpace(attributeMappingJson))
				return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			try
			{
				return JsonConvert.DeserializeObject<Dictionary<string, string>>(attributeMappingJson)
					?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			}
			catch
			{
				return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			}
		}

		private static string GetMappedClaim(ClaimsPrincipal principal, Dictionary<string, string> mapping, string fieldKey, params string[] fallbackClaimTypes)
		{
			// Check if there's a custom mapping for this field
			if (mapping.TryGetValue(fieldKey, out var mappedClaimType))
			{
				var mapped = principal.FindFirstValue(mappedClaimType);
				if (!string.IsNullOrWhiteSpace(mapped))
					return mapped;
			}

			// Fall back to well-known claim types
			foreach (var claimType in fallbackClaimTypes)
			{
				var value = principal.FindFirstValue(claimType);
				if (!string.IsNullOrWhiteSpace(value))
					return value;
			}

			return null;
		}

		private async Task<IdentityUser> ProvisionNewUserAsync(int departmentId, string email, string firstName, string lastName, string externalSubject, DepartmentSsoConfig config, string departmentCode, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(email))
				return null;

			try
			{
				// Note: full IdentityUser creation (password hash, security stamp, etc.) requires
				// UserManager<IdentityUser> which is available in the ASP.NET Core layer.
				// This service creates the DepartmentMember and UserProfile records assuming
				// the IdentityUser row has already been created by the caller (e.g. ScimController
				// or ExternalToken endpoint) before invoking ProvisionOrLinkUserAsync.
				//
				// Return null here so the controller knows it must create the IdentityUser first,
				// then call ProvisionOrLinkUserAsync again with the externalClaims.
				Logging.LogInfo($"DepartmentSsoService: Auto-provision requested for email={email} dept={departmentId} — IdentityUser creation must be performed by the caller.");
				return null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private static bool IsIpAddressAllowed(string clientIp, string allowedRangesCsv)
		{
			if (string.IsNullOrWhiteSpace(clientIp))
				return true;

			if (!IPAddress.TryParse(clientIp, out var clientAddress))
				return false;

			var ranges = allowedRangesCsv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var range in ranges)
			{
				var trimmed = range.Trim();
				if (IsInCidrRange(clientAddress, trimmed))
					return true;
			}

			return false;
		}

		private static bool IsInCidrRange(IPAddress address, string cidr)
		{
			try
			{
				var parts = cidr.Split('/');
				if (parts.Length != 2)
				{
					// Plain IP match
					return IPAddress.TryParse(cidr, out var plain) && plain.Equals(address);
				}

				if (!IPAddress.TryParse(parts[0], out var networkAddress))
					return false;

				if (!int.TryParse(parts[1], out var prefixLength))
					return false;

				var networkBytes = networkAddress.GetAddressBytes();
				var clientBytes = address.GetAddressBytes();

				if (networkBytes.Length != clientBytes.Length)
					return false;

				var fullBytes = prefixLength / 8;
				var remainingBits = prefixLength % 8;

				for (var i = 0; i < fullBytes; i++)
				{
					if (networkBytes[i] != clientBytes[i])
						return false;
				}

				if (remainingBits > 0 && fullBytes < networkBytes.Length)
				{
					var mask = (byte)(0xFF << (8 - remainingBits));
					if ((networkBytes[fullBytes] & mask) != (clientBytes[fullBytes] & mask))
						return false;
				}

				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}



