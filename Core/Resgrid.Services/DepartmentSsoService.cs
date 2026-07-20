using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
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
		private static readonly TimeSpan TokenClockSkew = TimeSpan.FromMinutes(2);
		private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> OidcConfigurationManagers = new(StringComparer.OrdinalIgnoreCase);
		private readonly IDepartmentSsoConfigRepository _ssoConfigRepository;
		private readonly IDepartmentSecurityPolicyRepository _securityPolicyRepository;
		private readonly IDepartmentMembersRepository _departmentMembersRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEncryptionService _encryptionService;
		private readonly ICacheProvider _cacheProvider;

		public DepartmentSsoService(
			IDepartmentSsoConfigRepository ssoConfigRepository,
			IDepartmentSecurityPolicyRepository securityPolicyRepository,
			IDepartmentMembersRepository departmentMembersRepository,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IEncryptionService encryptionService,
			ICacheProvider cacheProvider)
		{
			_ssoConfigRepository = ssoConfigRepository;
			_securityPolicyRepository = securityPolicyRepository;
			_departmentMembersRepository = departmentMembersRepository;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_encryptionService = encryptionService;
			_cacheProvider = cacheProvider;
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
			if (config == null)
				throw new ArgumentNullException(nameof(config));

			var providerType = (SsoProviderType)config.SsoProviderType;
			var existing = await _ssoConfigRepository.GetByDepartmentIdAndTypeAsync(config.DepartmentId, providerType);

			if (existing == null)
			{
				if (string.IsNullOrWhiteSpace(config.DepartmentSsoConfigId))
					config.DepartmentSsoConfigId = Guid.NewGuid().ToString();

				if (config.CreatedOn == default)
					config.CreatedOn = DateTime.UtcNow;

				config.EncryptedClientSecret = EncryptNewSecret(config.EncryptedClientSecret, config.DepartmentId, departmentCode);
				config.EncryptedIdpCertificate = EncryptNewSecret(config.EncryptedIdpCertificate, config.DepartmentId, departmentCode);
				config.EncryptedSigningCertificate = EncryptNewSecret(config.EncryptedSigningCertificate, config.DepartmentId, departmentCode);
				config.EncryptedScimBearerToken = EncryptNewSecret(config.EncryptedScimBearerToken, config.DepartmentId, departmentCode);

				return await _ssoConfigRepository.InsertAsync(config, cancellationToken);
			}

			// Blank secret fields mean "keep the stored value". The generic repository updates
			// every column, so this preservation must happen before issuing the UPDATE.
			config.DepartmentSsoConfigId = existing.DepartmentSsoConfigId;
			config.CreatedByUserId = existing.CreatedByUserId;
			config.CreatedOn = existing.CreatedOn;
			config.EncryptedClientSecret = EncryptUpdatedSecret(config.EncryptedClientSecret, existing.EncryptedClientSecret, config.DepartmentId, departmentCode);
			config.EncryptedIdpCertificate = EncryptUpdatedSecret(config.EncryptedIdpCertificate, existing.EncryptedIdpCertificate, config.DepartmentId, departmentCode);
			config.EncryptedSigningCertificate = EncryptUpdatedSecret(config.EncryptedSigningCertificate, existing.EncryptedSigningCertificate, config.DepartmentId, departmentCode);
			config.EncryptedScimBearerToken = EncryptUpdatedSecret(config.EncryptedScimBearerToken, existing.EncryptedScimBearerToken, config.DepartmentId, departmentCode);
			config.UpdatedOn = DateTime.UtcNow;

			return await _ssoConfigRepository.UpdateAsync(config, cancellationToken);
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
					return await ValidateOidcTokenAsync(externalToken, config, cancellationToken);

				if (providerType == SsoProviderType.Saml2)
					return await ValidateSamlResponseAsync(externalToken, config, departmentCode);

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

		/// <summary>
		/// Validates the SCIM bearer token against the claimed department and returns the
		/// owning department ID on success. The returned value is always equal to
		/// <paramref name="claimedDepartmentId"/> when valid, allowing the controller to
		/// confirm the token genuinely belongs to that department.
		/// Returns null when the token is missing, invalid, or belongs to a different department.
		/// </summary>
		public async Task<int?> ValidateScimBearerTokenAndGetDepartmentAsync(string bearerToken, int claimedDepartmentId, string departmentCode, CancellationToken cancellationToken = default)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(bearerToken))
					return null;

				var configs = await _ssoConfigRepository.GetAllByDepartmentIdAsync(claimedDepartmentId);
				var scimConfig = configs?.FirstOrDefault(c => c.ScimEnabled && !string.IsNullOrWhiteSpace(c.EncryptedScimBearerToken));
				if (scimConfig == null)
					return null;

				// The config was loaded specifically for claimedDepartmentId, so if
				// the decrypted token matches we know it belongs to that department.
				var storedToken = _encryptionService.DecryptForDepartment(
					scimConfig.EncryptedScimBearerToken, claimedDepartmentId, departmentCode);

				if (!string.Equals(storedToken, bearerToken, StringComparison.Ordinal))
					return null;

				// Double-check: the config's own DepartmentId must equal the claimed ID.
				// This guards against any accidental data inconsistency.
				if (scimConfig.DepartmentId != claimedDepartmentId)
					return null;

				return scimConfig.DepartmentId;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
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

		// ── Password Policy helpers ────────────────────────────────────────────

		/// <summary>The platform-enforced minimum password length. Department policies may not go below this.</summary>
		private const int SystemMinPasswordLength = 8;

		public async Task<int> GetEffectiveMinPasswordLengthAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			try
			{
				var policy = await _securityPolicyRepository.GetByDepartmentIdAsync(departmentId);
				if (policy == null || policy.MinPasswordLength <= SystemMinPasswordLength)
					return SystemMinPasswordLength;

				return policy.MinPasswordLength;
			}
			catch
			{
				return SystemMinPasswordLength;
			}
		}

		public async Task<string> ValidatePasswordAgainstPolicyAsync(int departmentId, string newPassword, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrEmpty(newPassword))
				return "PwdErrorEmpty";

			// System-enforced complexity: digit, uppercase, lowercase
			if (!newPassword.Any(char.IsDigit))
				return "PwdErrorNoDigit";
			if (!newPassword.Any(char.IsUpper))
				return "PwdErrorNoUppercase";
			if (!newPassword.Any(char.IsLower))
				return "PwdErrorNoLowercase";

			var minLength = await GetEffectiveMinPasswordLengthAsync(departmentId, cancellationToken);
			if (newPassword.Length < minLength)
				return $"PwdErrorTooShort:{minLength}";

			return null;
		}

		public bool IsPasswordExpired(DepartmentSecurityPolicy policy, DateTime? passwordLastSetOn)
		{
			if (policy == null || policy.PasswordExpirationDays <= 0)
				return false;

			// If the user has never changed their password since tracking began, don't force expiry —
			// they'll be required to update on the next natural change. This avoids a mass lockout.
			if (passwordLastSetOn == null)
				return false;

			return DateTime.UtcNow > passwordLastSetOn.Value.AddDays(policy.PasswordExpirationDays);
		}

		public async Task RecordPasswordChangedAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			try
			{
				var member = await _departmentMembersRepository.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userId);
				if (member == null)
					return;

				member.PasswordLastSetOn = DateTime.UtcNow;
				await _departmentMembersRepository.SaveOrUpdateAsync(member, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		// ── Private helpers ───────────────────────────────────────────────────

		private string EncryptNewSecret(string plaintext, int departmentId, string departmentCode)
		{
			return string.IsNullOrWhiteSpace(plaintext)
				? null
				: _encryptionService.EncryptForDepartment(plaintext, departmentId, departmentCode);
		}

		private string EncryptUpdatedSecret(string submittedValue, string storedCiphertext, int departmentId, string departmentCode)
		{
			if (string.IsNullOrWhiteSpace(submittedValue) || string.Equals(submittedValue, storedCiphertext, StringComparison.Ordinal))
				return storedCiphertext;

			return _encryptionService.EncryptForDepartment(submittedValue, departmentId, departmentCode);
		}

		private async Task<ClaimsPrincipal> ValidateOidcTokenAsync(string idToken, DepartmentSsoConfig config, CancellationToken cancellationToken)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(config.Authority) || string.IsNullOrWhiteSpace(config.ClientId))
					return null;

				if (!Uri.TryCreate(config.Authority, UriKind.Absolute, out var authorityUri) || authorityUri.Scheme != Uri.UriSchemeHttps)
					return null;

				var authority = config.Authority.TrimEnd('/');
				var manager = OidcConfigurationManagers.GetOrAdd(authority, static value =>
					new ConfigurationManager<OpenIdConnectConfiguration>(
						$"{value}/.well-known/openid-configuration",
						new OpenIdConnectConfigurationRetriever(),
						new HttpDocumentRetriever { RequireHttps = true }));

				var oidcConfiguration = await manager.GetConfigurationAsync(cancellationToken);
				var handler = new JwtSecurityTokenHandler();
				try
				{
					return handler.ValidateToken(idToken, BuildOidcValidationParameters(config, oidcConfiguration), out _);
				}
				catch (SecurityTokenSignatureKeyNotFoundException)
				{
					manager.RequestRefresh();
					oidcConfiguration = await manager.GetConfigurationAsync(cancellationToken);
					return handler.ValidateToken(idToken, BuildOidcValidationParameters(config, oidcConfiguration), out _);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private static TokenValidationParameters BuildOidcValidationParameters(DepartmentSsoConfig config, OpenIdConnectConfiguration oidcConfiguration)
		{
			return new TokenValidationParameters
			{
				ValidateIssuer = true,
				ValidIssuer = oidcConfiguration.Issuer,
				ValidateAudience = true,
				ValidAudience = config.ClientId,
				ValidateLifetime = true,
				RequireExpirationTime = true,
				ValidateIssuerSigningKey = true,
				RequireSignedTokens = true,
				IssuerSigningKeys = oidcConfiguration.SigningKeys,
				ClockSkew = TokenClockSkew
			};
		}

		private async Task<ClaimsPrincipal> ValidateSamlResponseAsync(string base64SamlResponse, DepartmentSsoConfig config, string departmentCode)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(base64SamlResponse) || base64SamlResponse.Length > 2_800_000 ||
					string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate) || string.IsNullOrWhiteSpace(config.EntityId) ||
					string.IsNullOrWhiteSpace(config.AssertionConsumerServiceUrl))
					return null;

				var samlBytes = Convert.FromBase64String(base64SamlResponse);
				if (samlBytes.Length > 2_000_000)
					return null;

				var document = LoadSamlDocument(samlBytes);
				var response = document.DocumentElement;
				if (response == null || response.LocalName != "Response" || response.NamespaceURI != "urn:oasis:names:tc:SAML:2.0:protocol")
					return null;

				var namespaces = new XmlNamespaceManager(document.NameTable);
				namespaces.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
				namespaces.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
				namespaces.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

				var statusCode = response.SelectSingleNode("./samlp:Status/samlp:StatusCode", namespaces) as XmlElement;
				if (statusCode?.GetAttribute("Value") != "urn:oasis:names:tc:SAML:2.0:status:Success")
					return null;

				var assertionNodes = response.SelectNodes("./saml:Assertion", namespaces);
				if (assertionNodes?.Count != 1 || assertionNodes[0] is not XmlElement assertion || !HasUniqueSamlIds(document))
					return null;

				var certificatePem = _encryptionService.DecryptForDepartment(
					config.EncryptedIdpCertificate, config.DepartmentId, departmentCode);
				using var certificate = X509Certificate2.CreateFromPem(certificatePem);

				var now = DateTime.UtcNow;
				if (now + TokenClockSkew < certificate.NotBefore.ToUniversalTime() || now - TokenClockSkew >= certificate.NotAfter.ToUniversalTime())
					return null;

				if (!ValidateSamlSignature(document, response, assertion, namespaces, certificate) ||
					!ValidateSamlDestinationAndConditions(response, assertion, namespaces, config, now, out var assertionExpiresOn))
					return null;

				var assertionId = assertion.GetAttribute("ID");
				if (string.IsNullOrWhiteSpace(assertionId) ||
					!await MarkSamlAssertionConsumedAsync(config.DepartmentSsoConfigId, assertionId, assertionExpiresOn, now))
					return null;

				var claims = ExtractSamlClaims(assertion, namespaces);
				return claims.Count == 0 ? null : new ClaimsPrincipal(new ClaimsIdentity(claims, "SAML2"));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private static XmlDocument LoadSamlDocument(byte[] samlBytes)
		{
			var settings = new XmlReaderSettings
			{
				DtdProcessing = DtdProcessing.Prohibit,
				XmlResolver = null,
				MaxCharactersInDocument = 2_000_000
			};

			var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
			using var stream = new MemoryStream(samlBytes, writable: false);
			using var reader = XmlReader.Create(stream, settings);
			document.Load(reader);
			return document;
		}

		private static bool HasUniqueSamlIds(XmlDocument document)
		{
			var ids = new HashSet<string>(StringComparer.Ordinal);
			var nodes = document.SelectNodes("//*[@ID]");
			if (nodes == null)
				return false;

			foreach (XmlElement node in nodes)
			{
				var id = node.GetAttribute("ID");
				if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
					return false;
			}

			return ids.Count > 0;
		}

		private static bool ValidateSamlSignature(XmlDocument document, XmlElement response, XmlElement assertion,
			XmlNamespaceManager namespaces, X509Certificate2 certificate)
		{
			var signature = assertion.SelectSingleNode("./ds:Signature", namespaces) as XmlElement;
			var signedElement = assertion;
			if (signature == null)
			{
				signature = response.SelectSingleNode("./ds:Signature", namespaces) as XmlElement;
				signedElement = response;
			}

			if (signature == null)
				return false;

			var signedXml = new SignedXml(document);
			signedXml.LoadXml(signature);
			if (signedXml.SignedInfo.CanonicalizationMethod != SignedXml.XmlDsigExcC14NTransformUrl ||
				!IsAllowedSamlSignatureAlgorithm(signedXml.SignedInfo.SignatureMethod) || signedXml.SignedInfo.References.Count != 1)
				return false;

			if (signedXml.SignedInfo.References[0] is not Reference reference ||
				!IsAllowedSamlDigestAlgorithm(reference.DigestMethod) || !HasOnlyAllowedSamlTransforms(reference))
				return false;

			var id = signedElement.GetAttribute("ID");
			if (string.IsNullOrWhiteSpace(id) || reference.Uri != $"#{id}" || !ReferenceEquals(signedXml.GetIdElement(document, id), signedElement))
				return false;

			return signedXml.CheckSignature(certificate, verifySignatureOnly: true);
		}

		private static bool HasOnlyAllowedSamlTransforms(Reference reference)
		{
			if (reference.TransformChain.Count is < 1 or > 2)
				return false;

			var hasEnvelopedSignatureTransform = false;
			var hasExclusiveCanonicalizationTransform = false;
			foreach (Transform transform in reference.TransformChain)
			{
				if (transform.Algorithm == SignedXml.XmlDsigEnvelopedSignatureTransformUrl && !hasEnvelopedSignatureTransform)
				{
					hasEnvelopedSignatureTransform = true;
					continue;
				}

				if (transform.Algorithm == SignedXml.XmlDsigExcC14NTransformUrl && !hasExclusiveCanonicalizationTransform)
				{
					hasExclusiveCanonicalizationTransform = true;
					continue;
				}

				return false;
			}

			return hasEnvelopedSignatureTransform;
		}

		private static bool IsAllowedSamlSignatureAlgorithm(string algorithm)
		{
			return algorithm == SignedXml.XmlDsigRSASHA256Url ||
				algorithm == "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384" ||
				algorithm == "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512";
		}

		private static bool IsAllowedSamlDigestAlgorithm(string algorithm)
		{
			return algorithm == SignedXml.XmlDsigSHA256Url ||
				algorithm == "http://www.w3.org/2001/04/xmldsig-more#sha384" ||
				algorithm == "http://www.w3.org/2001/04/xmlenc#sha512";
		}

		private static bool ValidateSamlDestinationAndConditions(XmlElement response, XmlElement assertion,
			XmlNamespaceManager namespaces, DepartmentSsoConfig config, DateTime now, out DateTime assertionExpiresOn)
		{
			assertionExpiresOn = default;
			var destination = response.GetAttribute("Destination");
			if (!string.IsNullOrWhiteSpace(destination) && !string.Equals(destination, config.AssertionConsumerServiceUrl, StringComparison.Ordinal))
				return false;

			if (assertion.SelectSingleNode("./saml:Conditions", namespaces) is not XmlElement conditions ||
				!TryReadSamlInstant(conditions.GetAttribute("NotOnOrAfter"), out assertionExpiresOn) ||
				now - TokenClockSkew >= assertionExpiresOn)
				return false;

			if (TryReadSamlInstant(conditions.GetAttribute("NotBefore"), out var notBefore) && now + TokenClockSkew < notBefore)
				return false;

			var audienceNodes = conditions.SelectNodes("./saml:AudienceRestriction/saml:Audience", namespaces);
			if (audienceNodes == null || !audienceNodes.Cast<XmlNode>().Any(node =>
				string.Equals(node.InnerText.Trim(), config.EntityId, StringComparison.Ordinal)))
				return false;

			var confirmationNodes = assertion.SelectNodes("./saml:Subject/saml:SubjectConfirmation[@Method='urn:oasis:names:tc:SAML:2.0:cm:bearer']/saml:SubjectConfirmationData", namespaces);
			return confirmationNodes != null && confirmationNodes.Cast<XmlElement>().Any(data =>
				string.Equals(data.GetAttribute("Recipient"), config.AssertionConsumerServiceUrl, StringComparison.Ordinal) &&
				TryReadSamlInstant(data.GetAttribute("NotOnOrAfter"), out var subjectExpiresOn) &&
				now - TokenClockSkew < subjectExpiresOn);
		}

		private static bool TryReadSamlInstant(string value, out DateTime instant)
		{
			instant = default;
			if (string.IsNullOrWhiteSpace(value))
				return false;

			try
			{
				instant = XmlConvert.ToDateTime(value, XmlDateTimeSerializationMode.Utc);
				return true;
			}
			catch (FormatException)
			{
				return false;
			}
		}

		private async Task<bool> MarkSamlAssertionConsumedAsync(string configId, string assertionId, DateTime expiresOn, DateTime now)
		{
			var remainingLifetime = expiresOn - now;
			if (remainingLifetime <= TimeSpan.Zero)
				return false;

			var replayIdentifier = Convert.ToHexString(
				SHA256.HashData(Encoding.UTF8.GetBytes($"{configId}:{assertionId}")));
			return await _cacheProvider.IncrementAsync(
				$"Sso:SamlAssertion:{replayIdentifier}", remainingLifetime) == 1;
		}

		private static List<Claim> ExtractSamlClaims(XmlElement assertion, XmlNamespaceManager namespaces)
		{
			var claims = new List<Claim>();
			var nameId = assertion.SelectSingleNode("./saml:Subject/saml:NameID", namespaces)?.InnerText?.Trim();
			if (!string.IsNullOrWhiteSpace(nameId))
				claims.Add(new Claim(ClaimTypes.NameIdentifier, nameId));

			var attributes = assertion.SelectNodes("./saml:AttributeStatement/saml:Attribute", namespaces);
			if (attributes == null)
				return claims;

			foreach (XmlElement attribute in attributes)
			{
				var name = attribute.GetAttribute("Name");
				if (string.IsNullOrWhiteSpace(name))
					continue;

				var values = attribute.SelectNodes("./saml:AttributeValue", namespaces);
				if (values == null)
					continue;

				foreach (XmlNode valueNode in values)
				{
					var value = valueNode.InnerText?.Trim();
					if (string.IsNullOrWhiteSpace(value))
						continue;

					claims.Add(new Claim(name, value));
					var standardClaimType = GetStandardSamlClaimType(name);
					if (standardClaimType != null && !string.Equals(standardClaimType, name, StringComparison.Ordinal))
						claims.Add(new Claim(standardClaimType, value));
				}
			}

			return claims;
		}

		private static string GetStandardSamlClaimType(string attributeName)
		{
			if (attributeName.Equals("email", StringComparison.OrdinalIgnoreCase) || attributeName.Equals("EmailAddress", StringComparison.OrdinalIgnoreCase) || attributeName.Equals(ClaimTypes.Email, StringComparison.OrdinalIgnoreCase))
				return ClaimTypes.Email;

			if (attributeName.Equals("givenname", StringComparison.OrdinalIgnoreCase) || attributeName.Equals("given_name", StringComparison.OrdinalIgnoreCase) || attributeName.Equals(ClaimTypes.GivenName, StringComparison.OrdinalIgnoreCase))
				return ClaimTypes.GivenName;

			if (attributeName.Equals("surname", StringComparison.OrdinalIgnoreCase) || attributeName.Equals("family_name", StringComparison.OrdinalIgnoreCase) || attributeName.Equals(ClaimTypes.Surname, StringComparison.OrdinalIgnoreCase))
				return ClaimTypes.Surname;

			return null;
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



