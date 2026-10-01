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
		private readonly IExternalIdentityLinkService _externalIdentityLinkService;
		private readonly ILimitsService _limitsService;
		private readonly Resgrid.Model.Repositories.Queries.IUnitOfWork _unitOfWork;
		private readonly IDepartmentDataProtectionPolicyRepository _dataProtectionPolicyRepository;
		private readonly Lazy<IDepartmentDataProtectionService> _dataProtectionService;
		private readonly IUserSessionMfaEvidenceRepository _mfaEvidence;
		private readonly IAuditLogsRepository _auditLogs;

		public DepartmentSsoService(
			IDepartmentSsoConfigRepository ssoConfigRepository,
			IDepartmentSecurityPolicyRepository securityPolicyRepository,
			IDepartmentMembersRepository departmentMembersRepository,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IEncryptionService encryptionService,
			ICacheProvider cacheProvider,
			IExternalIdentityLinkService externalIdentityLinkService,
			ILimitsService limitsService,
			Resgrid.Model.Repositories.Queries.IUnitOfWork unitOfWork,
			IDepartmentDataProtectionPolicyRepository dataProtectionPolicyRepository,
			Lazy<IDepartmentDataProtectionService> dataProtectionService,
			IUserSessionMfaEvidenceRepository mfaEvidence,
			IAuditLogsRepository auditLogs)
		{
			_mfaEvidence = mfaEvidence;
			_auditLogs = auditLogs;
			_unitOfWork = unitOfWork;
			_dataProtectionPolicyRepository = dataProtectionPolicyRepository;
			_dataProtectionService = dataProtectionService;
			_ssoConfigRepository = ssoConfigRepository;
			_securityPolicyRepository = securityPolicyRepository;
			_departmentMembersRepository = departmentMembersRepository;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_encryptionService = encryptionService;
			_cacheProvider = cacheProvider;
			_externalIdentityLinkService = externalIdentityLinkService;
			_limitsService = limitsService;
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

				var inserted = await _ssoConfigRepository.InsertAsync(config, cancellationToken);
				if (!string.IsNullOrWhiteSpace(inserted.FederatedMfaMappingJson))
					inserted.FederatedMfaMappingVersion = await AdvanceFederatedMfaMappingAsync(inserted.DepartmentSsoConfigId, 0, cancellationToken);
				return inserted;
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

			var updated = await _ssoConfigRepository.UpdateAsync(config, cancellationToken);

			// A provider step-up test proves one mapping against one issuer and client: changing either needs a new test,
			// and what the old version verified stops counting (plan section 7.8).
			if (FederatedMfaIdentityChanged(existing, config) &&
				(!string.IsNullOrWhiteSpace(existing.FederatedMfaMappingJson) || !string.IsNullOrWhiteSpace(config.FederatedMfaMappingJson)))
				updated.FederatedMfaMappingVersion = await AdvanceFederatedMfaMappingAsync(existing.DepartmentSsoConfigId,
					existing.FederatedMfaMappingVersion, cancellationToken);

			return updated;
		}

		/// <summary>Every rule the policy sets, as JSON for its audit record; the same value means nothing changed.</summary>
		private static string AuditSnapshot(DepartmentSecurityPolicy policy) =>
			System.Text.Json.JsonSerializer.Serialize(new
			{
				policy.RequireMfa, policy.RequireSso, policy.SessionTimeoutMinutes, policy.MaxConcurrentSessions, policy.AllowedIpRanges,
				policy.PasswordExpirationDays, policy.MinPasswordLength, policy.RequirePasswordComplexity, policy.DataClassificationLevel,
				policy.AllowPasskeysForLoginMfa, policy.AllowPasskeysForAdp, policy.AllowFederatedMfaForLoginMfa, policy.AllowFederatedMfaForAdp,
				policy.AllowResponderApproval, policy.AcceptRecentLoginMfaForAdp, policy.AcceptRecentUnlockMfaForAdp,
				policy.SharedIdleLockMinutes, policy.SharedShiftHours, policy.SharedModeRequiredApps
			});

		private static bool FederatedMfaIdentityChanged(DepartmentSsoConfig existing, DepartmentSsoConfig config) =>
			!string.Equals(existing.FederatedMfaMappingJson, config.FederatedMfaMappingJson, StringComparison.Ordinal) ||
			!string.Equals(existing.Authority, config.Authority, StringComparison.Ordinal) ||
			!string.Equals(existing.ClientId, config.ClientId, StringComparison.Ordinal) ||
			!string.Equals(existing.EntityId, config.EntityId, StringComparison.Ordinal) ||
			!string.Equals(existing.IdpSsoUrl, config.IdpSsoUrl, StringComparison.Ordinal) ||
			!string.Equals(existing.EncryptedIdpCertificate, config.EncryptedIdpCertificate, StringComparison.Ordinal);

		/// <summary>Advances the mapping version (clearing its test) and retires the evidence the previous version produced.</summary>
		private async Task<long> AdvanceFederatedMfaMappingAsync(string configId, long previousVersion, CancellationToken cancellationToken)
		{
			var version = await _ssoConfigRepository.AdvanceFederatedMfaMappingVersionAsync(configId, cancellationToken);
			if (previousVersion > 0)
			{
				try
				{
					await _mfaEvidence.RevokeByFactorReferenceAsync(Resgrid.Model.Security.FederatedMfaMapping.FactorReferenceFor(configId, previousVersion),
						DateTime.UtcNow, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					// The version already advanced, so nothing new can rely on the old mapping; the old evidence expires on its own.
					Logging.LogException(ex, "Provider step-up evidence for a changed mapping could not be revoked.");
				}
			}

			return version;
		}

		// ── Provider step-up (passkey plan section 7.8) ───────────────────────

		public async Task<DepartmentSsoConfig> GetTestedFederatedMfaConfigAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var config = (await _ssoConfigRepository.GetAllByDepartmentIdAsync(departmentId))?.FirstOrDefault(c => c.IsEnabled);
			return Resgrid.Model.Security.FederatedMfaMapping.IsTested(config) ? config : null;
		}

		public async Task<bool> IsFederatedMfaAvailableAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || await GetTestedFederatedMfaConfigAsync(departmentId, cancellationToken) == null)
				return false;

			// Offered to members signed in through this department's provider before; the callback still checks the identity.
			var members = await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedAsync(departmentId);
			return members?.Any(member => string.Equals(member.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
				!string.IsNullOrWhiteSpace(member.ExternalSsoId) && !member.IsDeleted) == true;
		}

		public Task<bool> RecordFederatedMfaTestAsync(string departmentSsoConfigId, long version, string userId, CancellationToken cancellationToken = default) =>
			_ssoConfigRepository.TryRecordFederatedMfaTestAsync(departmentSsoConfigId, version, userId, DateTime.UtcNow, cancellationToken);

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

		public Task<DepartmentSecurityPolicy> SaveSecurityPolicyAsync(DepartmentSecurityPolicy policy, CancellationToken cancellationToken = default) =>
			SaveSecurityPolicyAsync(policy, null, cancellationToken);

		/// <summary>
		/// Saves the policy and, in the same transaction, advances what its change invalidates (passkey plan section 10.1):
		/// MfaPolicyVersion when the sign-in MFA rules move, and the ADP PolicyEpoch (revoking grants) when the rules for
		/// grants move. The stored row is read under an update lock first, so concurrent changes are versioned one after
		/// the other, and the version is always the server's, never the caller's. A change is written to the department's
		/// audit log in the same transaction, so there is no change without its record and no record of a change that
		/// rolled back. The ADP cache is cleared after commit, so no reader can re-cache the old epoch.
		/// </summary>
		public async Task<DepartmentSecurityPolicy> SaveSecurityPolicyAsync(DepartmentSecurityPolicy policy, string changedByUserId,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(policy);
			policy.UpdatedOn = DateTime.UtcNow;

			var owns = _unitOfWork.Transaction == null;
			await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			bool adpChanged;
			DepartmentSecurityPolicy saved;
			try
			{
				// No stored row behaves as the defaults, so a first save that departs from them still advances.
				var stored = await _securityPolicyRepository.GetByDepartmentIdForUpdateAsync(policy.DepartmentId, cancellationToken)
					?? new DepartmentSecurityPolicy { DepartmentId = policy.DepartmentId };
				var mfaChanged = DepartmentSecurityPolicyDecisions.MfaPolicyChanged(stored, policy);
				adpChanged = DepartmentSecurityPolicyDecisions.AdpMethodPolicyChanged(stored, policy);

				saved = await _securityPolicyRepository.SaveOrUpdateAsync(policy, cancellationToken);
				saved.MfaPolicyVersion = mfaChanged
					? await _securityPolicyRepository.IncrementMfaPolicyVersionAsync(policy.DepartmentId, cancellationToken)
					: stored.MfaPolicyVersion;
				if (adpChanged)
					await _dataProtectionPolicyRepository.IncrementPolicyEpochAsync(policy.DepartmentId, changedByUserId, cancellationToken);

				var before = AuditSnapshot(stored);
				var after = AuditSnapshot(policy);
				if (!string.Equals(before, after, StringComparison.Ordinal))
					await _auditLogs.SaveOrUpdateAsync(new AuditLog
					{
						DepartmentId = policy.DepartmentId,
						ObjectDepartmentId = policy.DepartmentId,
						UserId = changedByUserId,
						LogType = (int)AuditLogTypes.DepartmentSecurityPolicyChanged,
						LoggedOn = DateTime.UtcNow,
						Successful = true,
						ObjectId = saved.DepartmentSecurityPolicyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
						Message = "SecurityPolicyChanged",
						Data = $"{{\"before\":{before},\"after\":{after},\"mfaPolicyVersion\":{saved.MfaPolicyVersion},\"adpEpochAdvanced\":{(adpChanged ? "true" : "false")}}}",
						ServerName = Environment.MachineName
					}, cancellationToken);

				if (owns)
					_unitOfWork.CommitChanges();
			}
			catch
			{
				if (owns)
					_unitOfWork.DiscardChanges();
				throw;
			}

			if (adpChanged)
				await _dataProtectionService.Value.InvalidateProtectionCacheAsync(policy.DepartmentId);

			return saved;
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

		public async Task<Resgrid.Model.Security.SsoIdentityAssertion> ValidateBrokeredSamlResponseAsync(int departmentId, string base64SamlResponse,
			string departmentCode, string expectedRequestId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(expectedRequestId))
				return null;

			try
			{
				var config = await _ssoConfigRepository.GetByDepartmentIdAndTypeAsync(departmentId, SsoProviderType.Saml2);
				if (config == null || !config.IsEnabled)
					return null;

				var (principal, authnInstant, authnContexts) = await ValidateSamlResponseCoreAsync(base64SamlResponse, config, departmentCode, expectedRequestId);
				return principal == null
					? null
					: new Resgrid.Model.Security.SsoIdentityAssertion { Principal = principal, AuthenticatedAtUtc = authnInstant, AuthnContextClassRefs = authnContexts };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		public async Task<string> FindLinkedUserIdAsync(int departmentId, ClaimsPrincipal externalClaims, DepartmentSsoConfig config,
			CancellationToken cancellationToken = default)
		{
			if (externalClaims == null || config == null || config.DepartmentId != departmentId)
				return null;

			var mapping = ResolveAttributeMapping(config.AttributeMappingJson);
			var externalSubject = GetMappedClaim(externalClaims, mapping, "subject",
				ClaimTypes.NameIdentifier, "sub", "nameidentifier");
			if (string.IsNullOrWhiteSpace(externalSubject))
				return null;

			var link = await _externalIdentityLinkService.GetBySubjectAsync(config.DepartmentSsoConfigId, externalSubject, cancellationToken);
			if (link != null)
				return link.DepartmentId == departmentId ? link.UserId : null;

			// Accounts linked before the durable binding table existed.
			var members = await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedAsync(departmentId);
			return members?.FirstOrDefault(candidate => string.Equals(candidate.ExternalSsoId, externalSubject, StringComparison.Ordinal))?.UserId;
		}

		// ── User Provisioning ─────────────────────────────────────────────────

		public async Task<IdentityUser> ProvisionOrLinkUserAsync(int departmentId, ClaimsPrincipal externalClaims, DepartmentSsoConfig config, string departmentCode, CancellationToken cancellationToken = default)
		{
			if (externalClaims == null || config == null || config.DepartmentId != departmentId)
				return null;

			var mapping = ResolveAttributeMapping(config.AttributeMappingJson);
			var email = GetMappedClaim(externalClaims, mapping, "email",
				ClaimTypes.Email, "email", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");
			var externalSubject = GetMappedClaim(externalClaims, mapping, "subject",
				ClaimTypes.NameIdentifier, "sub", "nameidentifier");
			var firstName = GetMappedClaim(externalClaims, mapping, "firstName",
				ClaimTypes.GivenName, "given_name", "firstname");
			var lastName = GetMappedClaim(externalClaims, mapping, "lastName",
				ClaimTypes.Surname, "family_name", "surname");

			// A mutable email address is never accepted as the durable external identifier.
			if (string.IsNullOrWhiteSpace(externalSubject))
				return null;

			var now = DateTime.UtcNow;
			var members = await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedAsync(departmentId);
			var link = await _externalIdentityLinkService.GetBySubjectAsync(config.DepartmentSsoConfigId,
				externalSubject, cancellationToken);
			DepartmentMember member = null;
			var linkMethod = ExternalIdentityLinkMethod.Subject;

			if (link != null)
			{
				if (link.DepartmentId != departmentId)
					return null;
				member = members?.FirstOrDefault(candidate => candidate.UserId == link.UserId);
			}

			// Compatibility for accounts linked before the durable binding table existed.
			member ??= members?.FirstOrDefault(candidate => candidate.ExternalSsoId == externalSubject);

			// Bootstrap-by-email is permitted only when the signed IdP assertion explicitly
			// marks the email as verified. SAML deployments without such a claim require an
			// administrator-created/SCIM link instead of silently taking over an email match.
			if (member == null && !string.IsNullOrWhiteSpace(email) && IsVerifiedEmail(externalClaims))
			{
				var users = await _departmentsService.GetAllUsersForDepartment(departmentId, false, true);
				var matchedUser = users?.FirstOrDefault(candidate =>
					string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));
				if (matchedUser != null)
				{
					member = members?.FirstOrDefault(candidate => candidate.UserId == matchedUser.Id);
					linkMethod = ExternalIdentityLinkMethod.VerifiedEmail;
				}
			}

			if (member != null)
			{
				if (string.IsNullOrWhiteSpace(member.ExternalSsoId))
				{
					member.ExternalSsoId = externalSubject;
					member.SsoLinkedOn = now;
				}
				else if (!string.Equals(member.ExternalSsoId, externalSubject, StringComparison.Ordinal))
				{
					return null;
				}

				member.LastSsoLoginOn = now;
				await _departmentMembersRepository.SaveOrUpdateAsync(member, cancellationToken);
				await SaveExternalLinkAsync(link, member, config, externalClaims, externalSubject, email,
					linkMethod, linkMethod == ExternalIdentityLinkMethod.VerifiedEmail, now, cancellationToken);

				var users = await _departmentsService.GetAllUsersForDepartment(departmentId, false, true);
				return users?.FirstOrDefault(candidate => candidate.Id == member.UserId);
			}

			if (!config.AutoProvisionUsers || string.IsNullOrWhiteSpace(email))
				return null;

			// A provisioned member takes a personnel seat. Linking an existing member (above) never does.
			if (!await _limitsService.CanDepartmentAddNewUserAsync(departmentId, true))
			{
				Logging.LogInfo($"DepartmentSsoService: auto-provision refused for dept={departmentId}: personnel limit reached or the plan could not be checked.");
				return null;
			}

			var provisionedUser = await ProvisionNewUserAsync(departmentId, email, firstName, lastName,
				externalSubject, config, departmentCode, cancellationToken);
			if (provisionedUser != null)
			{
				var provisionedMember = await _departmentsService.GetDepartmentMemberAsync(provisionedUser.Id, departmentId);
				if (provisionedMember != null)
					await SaveExternalLinkAsync(null, provisionedMember, config, externalClaims, externalSubject,
						email, ExternalIdentityLinkMethod.Subject, true, now, cancellationToken);
			}

			return provisionedUser;
		}

		private async Task SaveExternalLinkAsync(UserExternalIdentityLink link, DepartmentMember member,
			DepartmentSsoConfig config, ClaimsPrincipal externalClaims, string externalSubject, string email,
			ExternalIdentityLinkMethod linkMethod, bool emailExternallyManaged, DateTime now,
			CancellationToken cancellationToken)
		{
			link ??= new UserExternalIdentityLink
			{
				UserId = member.UserId,
				DepartmentId = config.DepartmentId,
				DepartmentMemberId = member.DepartmentMemberId,
				DepartmentSsoConfigId = config.DepartmentSsoConfigId,
				ProviderType = config.SsoProviderType,
				Issuer = GetExternalIssuer(externalClaims, config),
				ExternalSubject = externalSubject,
				EmailAtLink = email,
				LinkMethod = (int)linkMethod,
				IsEmailExternallyManaged = emailExternallyManaged,
				LinkedOn = now
			};

			link.LastLoginOn = now;
			await _externalIdentityLinkService.SaveAsync(link, cancellationToken);
		}

		private static bool IsVerifiedEmail(ClaimsPrincipal principal)
		{
			var value = principal.Claims.FirstOrDefault(claim =>
				string.Equals(claim.Type, "email_verified", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(claim.Type, "http://schemas.openid.net/claim/email_verified", StringComparison.OrdinalIgnoreCase))?.Value;
			return bool.TryParse(value, out var verified) && verified;
		}

		private static string GetExternalIssuer(ClaimsPrincipal principal, DepartmentSsoConfig config) =>
			principal.Claims.FirstOrDefault(claim => string.Equals(claim.Type, "iss", StringComparison.OrdinalIgnoreCase))?.Value
			?? config.Authority
			?? config.EntityId
			?? $"department-sso:{config.DepartmentSsoConfigId}";

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
				if (DepartmentSecurityPolicyDecisions.BlocksPasswordLogin(policy.RequireSso, hasSso, loginViaSso))
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
			if (DepartmentSecurityPolicyDecisions.RequiresMfaCompletion(policy.RequireMfa, mfaCompleted))
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
				return FixedTimeSecretEquals(storedToken, bearerToken);
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
		public async Task<DepartmentSsoConfig> ValidateScimBearerTokenAndGetConfigAsync(string bearerToken, int claimedDepartmentId, string departmentCode, CancellationToken cancellationToken = default)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(bearerToken))
					return null;

				var configs = await _ssoConfigRepository.GetAllByDepartmentIdAsync(claimedDepartmentId);
				var scimConfigs = configs?
					.Where(c => c.ScimEnabled && !string.IsNullOrWhiteSpace(c.EncryptedScimBearerToken))
					.ToList();
				if (scimConfigs == null || scimConfigs.Count == 0)
					return null;

				// Every SCIM-enabled configuration is a candidate, not just the first one: a department can
				// provision more than one, and the caller needs to know which token actually authorized the
				// request. The loop does not exit early, so the work does not vary with the match position.
				DepartmentSsoConfig matched = null;
				foreach (var scimConfig in scimConfigs)
				{
					string storedToken;
					try
					{
						// The config was loaded specifically for claimedDepartmentId, so if
						// the decrypted token matches we know it belongs to that department.
						storedToken = _encryptionService.DecryptForDepartment(
							scimConfig.EncryptedScimBearerToken, claimedDepartmentId, departmentCode);
					}
					catch (Exception ex)
					{
						// One unreadable blob must not disable SCIM for the department's other configurations.
						Logging.LogException(ex,
							$"Unable to decrypt the SCIM bearer token for config {scimConfig.DepartmentSsoConfigId}.");
						continue;
					}

					// Double-check: the config's own DepartmentId must equal the claimed ID.
					// This guards against any accidental data inconsistency.
					if (FixedTimeSecretEquals(storedToken, bearerToken) && scimConfig.DepartmentId == claimedDepartmentId)
						matched = scimConfig;
				}

				return matched;
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

				return DepartmentSecurityPolicyDecisions.MinimumPasswordLength(policy.MinPasswordLength);
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

			return DepartmentSecurityPolicyDecisions.PasswordExpired(policy.PasswordExpirationDays, passwordLastSetOn, DateTime.UtcNow);
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

		private static TokenValidationParameters BuildOidcValidationParameters(DepartmentSsoConfig config, OpenIdConnectConfiguration oidcConfiguration) =>
			BuildOidcValidationParameters(config.ClientId, oidcConfiguration.Issuer, oidcConfiguration.SigningKeys);

		/// <summary>The id_token checks shared by the legacy exchange and brokered SSO: issuer, audience, lifetime, signature.</summary>
		internal static TokenValidationParameters BuildOidcValidationParameters(string clientId, string issuer, IEnumerable<SecurityKey> signingKeys)
		{
			return new TokenValidationParameters
			{
				ValidateIssuer = true,
				ValidIssuer = issuer,
				ValidateAudience = true,
				ValidAudience = clientId,
				ValidateLifetime = true,
				RequireExpirationTime = true,
				ValidateIssuerSigningKey = true,
				RequireSignedTokens = true,
				IssuerSigningKeys = signingKeys,
				ClockSkew = TokenClockSkew
			};
		}

		private async Task<ClaimsPrincipal> ValidateSamlResponseAsync(string base64SamlResponse, DepartmentSsoConfig config, string departmentCode)
		{
			try
			{
				// The legacy relay accepts IdP-initiated (unsolicited) responses; brokered SSO never does.
				return (await ValidateSamlResponseCoreAsync(base64SamlResponse, config, departmentCode, expectedInResponseTo: null)).Principal;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		/// <summary>
		/// Every SAML check, and when <paramref name="expectedInResponseTo"/> is set, the binding to that AuthnRequest: the
		/// signed element must carry it (the Response when the Response is signed, otherwise the assertion's bearer
		/// SubjectConfirmationData), and no <c>InResponseTo</c> anywhere may name another request.
		/// </summary>
		private async Task<(ClaimsPrincipal Principal, DateTime? AuthnInstant, IReadOnlyCollection<string> AuthnContexts)> ValidateSamlResponseCoreAsync(string base64SamlResponse,
			DepartmentSsoConfig config, string departmentCode, string expectedInResponseTo)
		{
			if (string.IsNullOrWhiteSpace(base64SamlResponse) || base64SamlResponse.Length > 2_800_000 ||
				string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate) || string.IsNullOrWhiteSpace(config.EntityId) ||
				string.IsNullOrWhiteSpace(config.AssertionConsumerServiceUrl))
				return default;

			var samlBytes = Convert.FromBase64String(base64SamlResponse);
			if (samlBytes.Length > 2_000_000)
				return default;

			var document = LoadSamlDocument(samlBytes);
			var response = document.DocumentElement;
			if (response == null || response.LocalName != "Response" || response.NamespaceURI != "urn:oasis:names:tc:SAML:2.0:protocol")
				return default;

			var namespaces = new XmlNamespaceManager(document.NameTable);
			namespaces.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
			namespaces.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
			namespaces.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

			var statusCode = response.SelectSingleNode("./samlp:Status/samlp:StatusCode", namespaces) as XmlElement;
			if (statusCode?.GetAttribute("Value") != "urn:oasis:names:tc:SAML:2.0:status:Success")
				return default;

			var assertionNodes = response.SelectNodes("./saml:Assertion", namespaces);
			if (assertionNodes?.Count != 1 || assertionNodes[0] is not XmlElement assertion || !HasUniqueSamlIds(document))
				return default;

			var certificatePem = _encryptionService.DecryptForDepartment(
				config.EncryptedIdpCertificate, config.DepartmentId, departmentCode);
			using var certificate = X509Certificate2.CreateFromPem(certificatePem);

			var now = DateTime.UtcNow;
			if (now + TokenClockSkew < certificate.NotBefore.ToUniversalTime() || now - TokenClockSkew >= certificate.NotAfter.ToUniversalTime())
				return default;

			if (!ValidateSamlSignature(document, response, assertion, namespaces, certificate, out var signedElement) ||
				!ValidateSamlDestinationAndConditions(response, assertion, namespaces, config, now, out var assertionExpiresOn))
				return default;

			if (expectedInResponseTo != null && !IsBoundToRequest(response, assertion, signedElement, namespaces, expectedInResponseTo))
				return default;

			var assertionId = assertion.GetAttribute("ID");
			if (string.IsNullOrWhiteSpace(assertionId) ||
				!await MarkSamlAssertionConsumedAsync(config.DepartmentSsoConfigId, assertionId, assertionExpiresOn, now))
				return default;

			var claims = ExtractSamlClaims(assertion, namespaces);
			if (claims.Count == 0)
				return default;

			DateTime? authnInstant = null;
			if (assertion.SelectSingleNode("./saml:AuthnStatement", namespaces) is XmlElement authnStatement &&
				TryReadSamlInstant(authnStatement.GetAttribute("AuthnInstant"), out var instant))
			{
				authnInstant = instant;
				// Under the claim an id_token carries, for the legacy exchange's check on shared installations (section 12.5.2).
				claims.Add(new Claim(Resgrid.Model.Security.ProviderSignInTime.ClaimType, Resgrid.Model.Security.ProviderSignInTime.ClaimValue(instant),
					ClaimValueTypes.Integer64));
			}

			// How the IdP says it authenticated the user, for provider step-up mappings (plan section 7.8).
			var authnContexts = assertion.SelectNodes("./saml:AuthnStatement/saml:AuthnContext/saml:AuthnContextClassRef", namespaces)?
				.Cast<XmlNode>().Select(node => node.InnerText.Trim()).Where(value => value.Length > 0).ToList() ?? new List<string>();

			return (new ClaimsPrincipal(new ClaimsIdentity(claims, "SAML2")), authnInstant, authnContexts);
		}

		/// <summary>
		/// A brokered response must answer our AuthnRequest in a place the signature covers: the Response itself when it is
		/// the signed element, otherwise the assertion's bearer SubjectConfirmationData. No InResponseTo may name another.
		/// </summary>
		private static bool IsBoundToRequest(XmlElement response, XmlElement assertion, XmlElement signedElement, XmlNamespaceManager namespaces,
			string expectedInResponseTo)
		{
			var responseInResponseTo = response.GetAttribute("InResponseTo");
			if (!string.IsNullOrEmpty(responseInResponseTo) && !string.Equals(responseInResponseTo, expectedInResponseTo, StringComparison.Ordinal))
				return false;

			var confirmations = assertion.SelectNodes(
				"./saml:Subject/saml:SubjectConfirmation[@Method='urn:oasis:names:tc:SAML:2.0:cm:bearer']/saml:SubjectConfirmationData", namespaces)?
				.Cast<XmlElement>().ToList() ?? new List<XmlElement>();
			if (confirmations.Any(data => data.HasAttribute("InResponseTo") &&
					!string.Equals(data.GetAttribute("InResponseTo"), expectedInResponseTo, StringComparison.Ordinal)))
				return false;

			return ReferenceEquals(signedElement, response)
				? string.Equals(responseInResponseTo, expectedInResponseTo, StringComparison.Ordinal)
				: confirmations.Any(data => string.Equals(data.GetAttribute("InResponseTo"), expectedInResponseTo, StringComparison.Ordinal));
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
			XmlNamespaceManager namespaces, X509Certificate2 certificate, out XmlElement signedElement)
		{
			var signature = assertion.SelectSingleNode("./ds:Signature", namespaces) as XmlElement;
			signedElement = assertion;
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
			remainingLifetime += TokenClockSkew;

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

		private static bool FixedTimeSecretEquals(string stored, string provided)
		{
			if (stored == null || provided == null)
				return false;

			var storedBytes = Encoding.UTF8.GetBytes(stored);
			var providedBytes = Encoding.UTF8.GetBytes(provided);
			return storedBytes.Length == providedBytes.Length &&
				CryptographicOperations.FixedTimeEquals(storedBytes, providedBytes);
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



