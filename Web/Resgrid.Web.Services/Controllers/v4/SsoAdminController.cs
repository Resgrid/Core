using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Sso;
using SsoBeginResult = Resgrid.Web.Services.Models.v4.Sso.SsoBeginResult;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Allows department administrators to configure SSO (SAML 2.0 / OIDC),
	/// SCIM 2.0 provisioning, and department-level security policies.
	/// All write operations require department admin rights.
	/// Secrets (client secrets, certificates, SCIM tokens) are accepted as plaintext
	/// on input and encrypted before storage — they are NEVER returned in any response.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class SsoAdminController : V4AuthenticatedApiControllerbase
	{
		private readonly IDepartmentSsoService _ssoService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IMfaEvidenceService _mfaEvidence;
		private readonly IMfaPolicyService _mfaPolicy;

		/// <summary>Constructor.</summary>
		public SsoAdminController(
			IDepartmentSsoService ssoService,
			IDepartmentsService departmentsService,
			IPermissionsService permissionsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IMfaEvidenceService mfaEvidence,
			IMfaPolicyService mfaPolicy,
			ISsoBrokerService ssoBroker,
			ISystemAuditsService systemAuditsService,
			IPasskeyFeatureGates passkeyGates)
		{
			_passkeyGates = passkeyGates;
			_ssoBroker = ssoBroker;
			_systemAuditsService = systemAuditsService;
			_mfaPolicy = mfaPolicy;
			_ssoService = ssoService;
			_departmentsService = departmentsService;
			_permissionsService = permissionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_mfaEvidence = mfaEvidence;
		}

		/// <summary>
		/// Security, SSO, SCIM and MFA policy changes need an actual second factor on this session within the last few
		/// minutes (passkey plan section 7.6 row 13): call Mfa/VerifyStepUp with operation <c>security_change</c>, then
		/// retry. Null when the change may proceed.
		/// </summary>
		private async Task<ActionResult> RequireRecentMfaAsync(CancellationToken cancellationToken, bool excludeFederated = false)
		{
			var window = MfaStepUpOperations.WindowFor(MfaStepUpOperations.SecurityChange);
			if (await ApiStepUpEvidence.HasRecentSecondFactorAsync(_mfaEvidence, _mfaPolicy, UserId, HttpContext, DepartmentId,
					MfaMethodScope.SecurityChange, window, cancellationToken, excludeFederated))
				return null;

			return Problem(type: "step_up_required",
				title: excludeFederated
					? $"Verify with your authenticator app or a passkey (Mfa/VerifyStepUp, operation {MfaStepUpOperations.SecurityChange}) within the last {(int)window.TotalMinutes} minutes; provider step-up cannot authorize changes to itself."
					: $"Verify a second factor (Mfa/VerifyStepUp, operation {MfaStepUpOperations.SecurityChange}) within the last {(int)window.TotalMinutes} minutes, then retry.",
				statusCode: StatusCodes.Status403Forbidden);
		}

		private readonly ISsoBrokerService _ssoBroker;
		private readonly IPasskeyFeatureGates _passkeyGates;
		private readonly ISystemAuditsService _systemAuditsService;

		// ── SSO Config — list / get ───────────────────────────────────────────

		/// <summary>
		/// Returns all SSO configurations for the current department.
		/// Secrets are never included in the response — use HasClientSecret /
		/// HasIdpCertificate / HasSigningCertificate boolean flags to check presence.
		/// </summary>
		[HttpGet("GetSsoConfigs")]
		[Authorize(Policy = ResgridResources.Sso_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<GetSsoConfigsResult>> GetSsoConfigs(CancellationToken cancellationToken)
		{
			if (!await IsAdminAsync()) return Forbid();

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var result = new GetSsoConfigsResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = ResponseHelper.Success;

			result.Data = configs
				.Select(c => MapToSummary(c))
				.ToList();

			result.PageSize = result.Data.Count;
			return Ok(result);
		}

		/// <summary>
		/// Returns a single SSO configuration by ID.
		/// Secrets are never included in the response.
		/// </summary>
		[HttpGet("GetSsoConfig/{id}")]
		[Authorize(Policy = ResgridResources.Sso_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetSsoConfigResult>> GetSsoConfig(string id, CancellationToken cancellationToken)
		{
			if (!await IsAdminAsync()) return Forbid();

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs.FirstOrDefault(c => c.DepartmentSsoConfigId == id);

			if (config == null)
			{
				var notFound = new GetSsoConfigResult();
				ResponseHelper.PopulateV4ResponseNotFound(notFound);
				return NotFound(notFound);
			}

			var result = new GetSsoConfigResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = ResponseHelper.Success;
			result.PageSize = 1;
			result.Data = MapToDetail(config);
			return Ok(result);
		}

		// ── SSO Config — create / update / delete ─────────────────────────────

		/// <summary>
		/// Creates a new SSO configuration for the current department.
		/// Only one configuration per provider type is permitted per department.
		/// All secret fields (ClientSecret, IdpCertificate, SigningCertificate) are
		/// encrypted using the department-specific key before storage.
		/// </summary>
		[HttpPost("CreateSsoConfig")]
		[Authorize(Policy = ResgridResources.Sso_Create)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<SaveSsoConfigResult>> CreateSsoConfig(
			[FromBody] SaveSsoConfigInput input,
			CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid) return BadRequest(ModelState);
			if (!await IsAdminAsync()) return Forbid();
			var mfaProblem = await RequireRecentMfaAsync(cancellationToken);
			if (mfaProblem != null) return mfaProblem;

			if (!Enum.TryParse<SsoProviderType>(input.ProviderType, ignoreCase: true, out var providerType) || !Enum.IsDefined(providerType))
				return BadRequest(new { error = "Invalid providerType. Must be 'saml2' or 'oidc'." });

			var validationError = ValidateSsoConfiguration(input, providerType, existing: null);
			if (validationError != null)
				return BadRequest(new { error = validationError });

			// Enforce one config per provider type per department
			var existing = await _ssoService.GetSsoConfigForDepartmentAsync(DepartmentId, providerType, cancellationToken);
			if (existing != null)
				return Conflict(new { error = $"An SSO configuration for provider '{input.ProviderType}' already exists. Use UpdateSsoConfig to modify it." });

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var config = BuildConfigFromInput(input, providerType, department.DepartmentId, UserId);

			var saved = await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);

			var result = new SaveSsoConfigResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = ResponseHelper.Created;
			result.DepartmentSsoConfigId = saved.DepartmentSsoConfigId;
			return Ok(result);
		}

		/// <summary>
		/// Updates an existing SSO configuration.
		/// Secret fields are only re-encrypted and overwritten when a non-null, non-empty
		/// value is supplied. Omit secret fields (or send null) to leave them unchanged.
		/// </summary>
		[HttpPut("UpdateSsoConfig/{id}")]
		[Authorize(Policy = ResgridResources.Sso_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<SaveSsoConfigResult>> UpdateSsoConfig(
			string id,
			[FromBody] SaveSsoConfigInput input,
			CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid) return BadRequest(ModelState);
			if (!await IsAdminAsync()) return Forbid();
			var mfaProblem = await RequireRecentMfaAsync(cancellationToken);
			if (mfaProblem != null) return mfaProblem;

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs.FirstOrDefault(c => c.DepartmentSsoConfigId == id);

			if (config == null)
			{
				var notFound = new SaveSsoConfigResult();
				ResponseHelper.PopulateV4ResponseNotFound(notFound);
				return NotFound(notFound);
			}

			if (!Enum.TryParse<SsoProviderType>(input.ProviderType, ignoreCase: true, out var providerType) ||
				!Enum.IsDefined(providerType) || providerType != (SsoProviderType)config.SsoProviderType)
				return BadRequest(new { error = "providerType must match the existing SSO configuration." });

			var validationError = ValidateSsoConfiguration(input, providerType, config);
			if (validationError != null)
				return BadRequest(new { error = validationError });

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			// Update non-secret fields
			config.IsEnabled = input.IsEnabled;
			config.ClientId = input.ClientId ?? config.ClientId;
			config.Authority = input.Authority ?? config.Authority;
			config.MetadataUrl = input.MetadataUrl ?? config.MetadataUrl;
			config.EntityId = input.EntityId ?? config.EntityId;
			config.AssertionConsumerServiceUrl = input.AssertionConsumerServiceUrl ?? config.AssertionConsumerServiceUrl;
			config.IdpSsoUrl = input.IdpSsoUrl ?? config.IdpSsoUrl;
			config.AttributeMappingJson = input.AttributeMappingJson ?? config.AttributeMappingJson;
			config.AllowLocalLogin = input.AllowLocalLogin;
			config.AutoProvisionUsers = input.AutoProvisionUsers;
			config.DefaultRankId = input.DefaultRankId ?? config.DefaultRankId;
			config.ScimEnabled = input.ScimEnabled;
			config.UpdatedByUserId = UserId;

			// Only overwrite secrets when the caller supplies new plaintext values
			// (SaveSsoConfigAsync encrypts non-null values and leaves null values untouched
			//  by design — we replicate that here by setting to null when not provided)
			if (!string.IsNullOrWhiteSpace(input.ClientSecret))
				config.EncryptedClientSecret = input.ClientSecret; // service will encrypt
			else
				config.EncryptedClientSecret = null; // signal: do not overwrite

			if (!string.IsNullOrWhiteSpace(input.IdpCertificate))
				config.EncryptedIdpCertificate = input.IdpCertificate;
			else
				config.EncryptedIdpCertificate = null;

			if (!string.IsNullOrWhiteSpace(input.SigningCertificate))
				config.EncryptedSigningCertificate = input.SigningCertificate;
			else
				config.EncryptedSigningCertificate = null;

			var saved = await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);

			var result = new SaveSsoConfigResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = ResponseHelper.Updated;
			result.DepartmentSsoConfigId = saved.DepartmentSsoConfigId;
			return Ok(result);
		}

		/// <summary>
		/// Deletes the SSO configuration for a given provider type.
		/// This does not affect existing user accounts or memberships.
		/// </summary>
		[HttpDelete("DeleteSsoConfig/{providerType}")]
		[Authorize(Policy = ResgridResources.Sso_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<SsoOperationResult>> DeleteSsoConfig(
			string providerType,
			CancellationToken cancellationToken)
		{
			if (!await IsAdminAsync()) return Forbid();
			var mfaProblem = await RequireRecentMfaAsync(cancellationToken);
			if (mfaProblem != null) return mfaProblem;

			if (!Enum.TryParse<SsoProviderType>(providerType, ignoreCase: true, out var provider) || !Enum.IsDefined(provider))
				return BadRequest(new { error = "Invalid providerType. Must be 'saml2' or 'oidc'." });

			var success = await _ssoService.DeleteSsoConfigAsync(DepartmentId, provider, cancellationToken);

			var result = new SsoOperationResult();
			ResponseHelper.PopulateV4ResponseData(result);

			if (!success)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				result.Success = false;
				return NotFound(result);
			}

			result.Status = ResponseHelper.Deleted;
			result.Success = true;
			return Ok(result);
		}

		// ── SCIM bearer token management ──────────────────────────────────────

		/// <summary>
		/// Rotates the SCIM 2.0 bearer token for the department's SSO configuration.
		/// A new cryptographically random token is generated, encrypted, and stored.
		/// The plaintext token is returned ONCE in this response — it cannot be
		/// retrieved again. Store it immediately in your identity provider.
		/// </summary>
		[HttpPost("RotateScimToken/{providerType}")]
		[Authorize(Policy = ResgridResources.Sso_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<RotateScimTokenResult>> RotateScimToken(
			string providerType,
			CancellationToken cancellationToken)
		{
			if (!await IsAdminAsync()) return Forbid();
			var mfaProblem = await RequireRecentMfaAsync(cancellationToken);
			if (mfaProblem != null) return mfaProblem;

			if (!Enum.TryParse<SsoProviderType>(providerType, ignoreCase: true, out var provider) || !Enum.IsDefined(provider))
				return BadRequest(new { error = "Invalid providerType. Must be 'saml2' or 'oidc'." });

			var config = await _ssoService.GetSsoConfigForDepartmentAsync(DepartmentId, provider, cancellationToken);
			if (config == null)
			{
				var notFound = new RotateScimTokenResult();
				ResponseHelper.PopulateV4ResponseNotFound(notFound);
				return NotFound(notFound);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			// Generate a new cryptographically random 48-byte (384-bit) bearer token
			var tokenBytes = new byte[48];
			System.Security.Cryptography.RandomNumberGenerator.Fill(tokenBytes);
			var newToken = Convert.ToBase64String(tokenBytes);

			// Store the new plaintext token — SaveSsoConfigAsync will encrypt it
			config.EncryptedScimBearerToken = newToken;
			config.ScimEnabled = true;
			config.UpdatedByUserId = UserId;

			// Clear other secrets so they are not re-encrypted (null = do not overwrite)
			config.EncryptedClientSecret = null;
			config.EncryptedIdpCertificate = null;
			config.EncryptedSigningCertificate = null;

			await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);

			var result = new RotateScimTokenResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = ResponseHelper.Success;
			result.ScimBearerToken = newToken; // one-time plaintext exposure
			return Ok(result);
		}

		// ── Security Policy ───────────────────────────────────────────────────

		/// <summary>
		/// Returns the department's compliance security policy.
		/// Returns an empty policy object (with all defaults) if none has been saved yet.
		/// </summary>
		[HttpGet("GetSecurityPolicy")]
		[Authorize(Policy = ResgridResources.Sso_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<GetSecurityPolicyResult>> GetSecurityPolicy(CancellationToken cancellationToken)
		{
			if (!await IsAdminAsync()) return Forbid();

			var policy = await _ssoService.GetSecurityPolicyForDepartmentAsync(DepartmentId, cancellationToken);

			var result = new GetSecurityPolicyResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = ResponseHelper.Success;
			result.PageSize = 1;
			// No row behaves as the defaults (passkey plan section 10.1), so report them rather than all-false switches.
			var data = MapToSecurityPolicyData(policy ?? new DepartmentSecurityPolicy { CreatedOn = DateTime.UtcNow, MinPasswordLength = 8 });
			if (policy == null)
				data.UpdatedOn = null;
			result.Data = data;

			return Ok(result);
		}

		/// <summary>
		/// Creates or updates the department's compliance security policy.
		/// Warning: enabling RequireSso will prevent all users from logging in with
		/// a password — ensure at least one working SSO configuration exists first.
		/// </summary>
		[HttpPost("SaveSecurityPolicy")]
		[Authorize(Policy = ResgridResources.Sso_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<SaveSecurityPolicyResult>> SaveSecurityPolicy(
			[FromBody] SaveSecurityPolicyInput input,
			CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid) return BadRequest(ModelState);
			if (!await IsAdminAsync()) return Forbid();
			var mfaProblem = await RequireRecentMfaAsync(cancellationToken);
			if (mfaProblem != null) return mfaProblem;

			// Safety guard: disallow RequireSso=true when no active SSO config exists
			if (input.RequireSso)
			{
				var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
				if (!configs.Any(c => c.IsEnabled))
					return BadRequest(new
					{
						error = "Cannot enable RequireSso: no active SSO configuration exists for this department. " +
						        "Create and enable an SSO configuration before locking down to SSO-only login."
					});
			}

			var existing = await _ssoService.GetSecurityPolicyForDepartmentAsync(DepartmentId, cancellationToken);

			var policy = existing ?? new DepartmentSecurityPolicy
			{
				DepartmentId = DepartmentId,
				CreatedOn = DateTime.UtcNow
			};

			var storedRules = DepartmentSecurityPolicyDecisions.SnapshotMfaRules(policy);

			policy.RequireMfa = input.RequireMfa;
			policy.RequireSso = input.RequireSso;
			policy.SessionTimeoutMinutes = input.SessionTimeoutMinutes;
			policy.MaxConcurrentSessions = input.MaxConcurrentSessions;
			policy.AllowedIpRanges = input.AllowedIpRanges;
			policy.PasswordExpirationDays = input.PasswordExpirationDays;
			policy.MinPasswordLength = input.MinPasswordLength;
			policy.RequirePasswordComplexity = input.RequirePasswordComplexity;
			policy.DataClassificationLevel = input.DataClassificationLevel;

			policy.AllowPasskeysForLoginMfa = input.AllowPasskeysForLoginMfa ?? policy.AllowPasskeysForLoginMfa;
			policy.AllowPasskeysForAdp = input.AllowPasskeysForAdp ?? policy.AllowPasskeysForAdp;
			policy.AllowFederatedMfaForLoginMfa = input.AllowFederatedMfaForLoginMfa ?? policy.AllowFederatedMfaForLoginMfa;
			policy.AllowFederatedMfaForAdp = input.AllowFederatedMfaForAdp ?? policy.AllowFederatedMfaForAdp;
			policy.AllowResponderApproval = input.AllowResponderApproval ?? policy.AllowResponderApproval;
			policy.AcceptRecentLoginMfaForAdp = input.AcceptRecentLoginMfaForAdp ?? policy.AcceptRecentLoginMfaForAdp;
			policy.AcceptRecentUnlockMfaForAdp = input.AcceptRecentUnlockMfaForAdp ?? policy.AcceptRecentUnlockMfaForAdp;
			policy.SharedIdleLockMinutes = input.SharedIdleLockMinutes ?? policy.SharedIdleLockMinutes;
			policy.SharedShiftHours = input.SharedShiftHours ?? policy.SharedShiftHours;
			policy.SharedModeRequiredApps = input.SharedModeRequiredApps ?? policy.SharedModeRequiredApps;

			// Which second factors the department accepts is the managing member's decision, like the other ADP and
			// security controls it owns (passkey plan section 10.1). Other administrators can change everything else.
			if (DepartmentSecurityPolicyDecisions.MethodSwitchesChanged(storedRules, policy) && !await IsManagingMemberAsync())
				return Problem(type: "managing_member_required",
					title: "Only the department's managing member can change which sign-in methods are accepted.",
					statusCode: StatusCodes.Status403Forbidden);

			// The shared-device policy is the managing member's too (plan section 10.5). Stricter values reach running shared
			// sessions at their next request; requiring shared mode for another app needs the deployment to offer it.
			if (DepartmentSecurityPolicyDecisions.SharedPolicyChanged(storedRules, policy))
			{
				if (!await IsManagingMemberAsync())
					return Problem(type: "managing_member_required",
						title: "Only the department's managing member can change the shared-device policy.",
						statusCode: StatusCodes.Status403Forbidden);
				if (!DepartmentSecurityPolicyDecisions.SharedPolicyValid(policy))
					return Problem(type: "invalid_request",
						title: $"Choose an idle lock of 1-{SharedSessionRules.MaxIdleLockMinutes} minutes, a shift of 1-{SharedSessionRules.MaxShiftHours} hours, and apps from Unit (1), IC (2) and Dispatch (4).",
						statusCode: StatusCodes.Status400BadRequest);
				if (!_passkeyGates.SharedDeviceModeEnabled && DepartmentSecurityPolicyDecisions.AddsSharedRequirement(storedRules, policy))
					return Problem(type: "shared_mode_unavailable",
						title: "Shared-device mode is not available on this deployment yet, so it cannot be required for another app.",
						statusCode: StatusCodes.Status409Conflict);
			}

			// Turning provider step-up on needs a mapping that passed its test, and cannot be authorized by provider step-up.
			if (DepartmentSecurityPolicyDecisions.EnablesFederatedMfa(storedRules, policy))
			{
				if (await _ssoService.GetTestedFederatedMfaConfigAsync(DepartmentId, cancellationToken) == null)
					return Problem(type: "federated_mapping_untested",
						title: "Save and successfully test a provider step-up mapping (SsoAdmin/FederatedMfaTest) before accepting provider MFA.",
						statusCode: StatusCodes.Status409Conflict);

				var federatedProblem = await RequireRecentMfaAsync(cancellationToken, excludeFederated: true);
				if (federatedProblem != null) return federatedProblem;
			}

			var saved = await _ssoService.SaveSecurityPolicyAsync(policy, UserId, cancellationToken);

			var result = new SaveSecurityPolicyResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = existing == null ? ResponseHelper.Created : ResponseHelper.Updated;
			result.DepartmentSecurityPolicyId = saved.DepartmentSecurityPolicyId;
			return Ok(result);
		}

		// ── Provider step-up mapping (passkey plan section 7.8) ──────────────

		/// <summary>The active SSO configuration's provider step-up mapping and whether it has passed its test.</summary>
		[HttpGet("FederatedMfaMapping")]
		[Authorize(Policy = ResgridResources.Sso_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FederatedMfaMappingResult>> GetFederatedMfaMapping(CancellationToken cancellationToken)
		{
			if (!await IsAdminAsync()) return Forbid();

			var config = await ActiveConfigAsync(cancellationToken);
			return config == null ? NotFound() : MappingResult(config);
		}

		/// <summary>
		/// Saves (or removes, with a null mapping) the provider step-up mapping. Managing member only, after an authenticator
		/// app or passkey step-up; every change advances the version and needs a new test before it counts.
		/// </summary>
		[HttpPut("FederatedMfaMapping")]
		[Authorize(Policy = ResgridResources.Sso_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FederatedMfaMappingResult>> SaveFederatedMfaMapping([FromBody] SaveFederatedMfaMappingInput input,
			CancellationToken cancellationToken)
		{
			if (!await IsManagingMemberAsync())
				return Problem(type: "managing_member_required", title: "Only the department's managing member can change the provider step-up mapping.",
					statusCode: StatusCodes.Status403Forbidden);
			var mfaProblem = await RequireRecentMfaAsync(cancellationToken, excludeFederated: true);
			if (mfaProblem != null) return mfaProblem;

			var config = await ActiveConfigAsync(cancellationToken);
			if (config == null) return NotFound();

			string mappingJson = null;
			if (input?.Mapping != null && input.Mapping.Type != Newtonsoft.Json.Linq.JTokenType.Null)
			{
				var mapping = Resgrid.Model.Security.FederatedMfaMapping.Parse(input.Mapping.ToString(Newtonsoft.Json.Formatting.None));
				var problem = Resgrid.Model.Security.FederatedMfaMapping.Validate(mapping, (SsoProviderType)config.SsoProviderType);
				if (problem != null)
					return Problem(type: "invalid_request", title: problem, statusCode: StatusCodes.Status400BadRequest);
				mappingJson = mapping.Serialize();
			}

			var changed = !string.Equals(config.FederatedMfaMappingJson ?? string.Empty, mappingJson ?? string.Empty, StringComparison.Ordinal);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			config.FederatedMfaMappingJson = mappingJson;
			config.UpdatedByUserId = UserId;
			var saved = await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);

			// Who changed what counts as MFA is audited (plan section 7.8), whichever surface changed it.
			if (changed)
				await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Api,
					Type = (int)SystemAuditTypes.FederatedMfaMappingChanged,
					UserId = UserId,
					Username = UserName,
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					Data = mappingJson == null
						? $"Provider step-up mapping for SSO configuration {config.DepartmentSsoConfigId} removed (now version {saved?.FederatedMfaMappingVersion})."
						: $"Provider step-up mapping for SSO configuration {config.DepartmentSsoConfigId} saved as version {saved?.FederatedMfaMappingVersion}; " +
						  "it counts once it passes its test."
				}, cancellationToken);

			return MappingResult(saved);
		}

		/// <summary>Starts the managing member's test step-up with the saved mapping; it proves the provider returns a mapped MFA value.</summary>
		[HttpPost("FederatedMfaTest/Begin")]
		[Authorize(Policy = ResgridResources.Sso_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SsoBeginResult>> BeginFederatedMfaTest([FromBody] FederatedMfaTestBeginInput input, CancellationToken cancellationToken)
		{
			if (!await IsManagingMemberAsync())
				return Problem(type: "managing_member_required", title: "Only the department's managing member can test the provider step-up mapping.",
					statusCode: StatusCodes.Status403Forbidden);
			var mfaProblem = await RequireRecentMfaAsync(cancellationToken, excludeFederated: true);
			if (mfaProblem != null) return mfaProblem;

			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			if (session == null || input == null)
				return Problem(type: "session_required", title: "Sign in again to test the mapping.", statusCode: StatusCodes.Status409Conflict);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var begun = await _ssoBroker.BeginAsync(new Resgrid.Model.Security.SsoBeginRequest
			{
				DepartmentId = DepartmentId,
				DepartmentCode = department?.Code,
				Purpose = Resgrid.Model.Security.SsoTransactionPurpose.MappingTest,
				ClientApplication = (UserSessionClientApplication)session.ClientApplication,
				Platform = input.Platform,
				ReturnTarget = input.ReturnTarget,
				ClientState = input.State,
				CodeChallenge = input.CodeChallenge,
				CodeChallengeMethod = input.CodeChallengeMethod,
				SessionId = session.SessionId,
				UserId = UserId,
				AuthenticationGeneration = session.AuthenticationGeneration
			}, cancellationToken);
			if (!begun.Succeeded)
				return Problem(type: Resgrid.Model.Security.SsoBrokerOutcomes.ErrorCode(begun.Outcome) ?? "sso_failed",
					title: "The mapping test could not be started.", statusCode: begun.Outcome == Resgrid.Model.Security.SsoBrokerOutcome.ServiceUnavailable
						? StatusCodes.Status503ServiceUnavailable
						: StatusCodes.Status400BadRequest);

			var result = new SsoBeginResult
			{
				Data = new SsoBeginResultData { AuthorizeUrl = begun.AuthorizeUrl, SsoTransactionId = begun.TransactionId, ExpiresIn = begun.ExpiresInSeconds },
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Completes the test: the provider's fresh response carried a value the mapping counts as MFA, so that exact mapping
		/// version becomes effective. A mapping changed during the test must be tested again.
		/// </summary>
		[HttpPost("FederatedMfaTest/Complete")]
		[Authorize(Policy = ResgridResources.Sso_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FederatedMfaTestResult>> CompleteFederatedMfaTest([FromBody] FederatedMfaTestCompleteInput input,
			CancellationToken cancellationToken)
		{
			if (!await IsManagingMemberAsync())
				return Problem(type: "managing_member_required", title: "Only the department's managing member can test the provider step-up mapping.",
					statusCode: StatusCodes.Status403Forbidden);

			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			if (session == null)
				return Problem(type: "session_required", title: "Sign in again to test the mapping.", statusCode: StatusCodes.Status409Conflict);

			var redeemed = await _ssoBroker.RedeemAsync(input?.SsoTransactionId, input?.SsoCode, input?.CodeVerifier,
				(UserSessionClientApplication)session.ClientApplication, cancellationToken, Resgrid.Model.Security.SsoTransactionPurpose.MappingTest);
			var transaction = redeemed.Transaction;
			if (!redeemed.Succeeded || !string.Equals(transaction.SessionId, session.SessionId, StringComparison.Ordinal) ||
				!string.Equals(transaction.ExpectedUserId, UserId, StringComparison.OrdinalIgnoreCase) ||
				string.IsNullOrWhiteSpace(transaction.FederatedMfaValue) || transaction.FederatedMappingVersion == null)
				return Problem(type: Resgrid.Model.Security.SsoBrokerOutcomes.ErrorCode(redeemed.Succeeded
						? Resgrid.Model.Security.SsoBrokerOutcome.TransactionInvalid
						: redeemed.Outcome) ?? "sso_failed",
					title: "The mapping test could not be completed. Start it again.", statusCode: StatusCodes.Status400BadRequest);

			if (!await _ssoService.RecordFederatedMfaTestAsync(transaction.DepartmentSsoConfigId, transaction.FederatedMappingVersion.Value, UserId, cancellationToken))
				return Problem(type: "federated_mapping_changed", title: "The mapping changed during the test. Test it again.",
					statusCode: StatusCodes.Status409Conflict);

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.FederatedMfaMappingTested,
				UserId = UserId,
				Username = UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Provider step-up mapping version {transaction.FederatedMappingVersion} for SSO configuration {transaction.DepartmentSsoConfigId} " +
					$"passed its test ({transaction.FederatedMfaValue})."
			}, cancellationToken);

			var result = new FederatedMfaTestResult
			{
				Data = new FederatedMfaTestResultData
				{
					Tested = true,
					MatchedValue = transaction.FederatedMfaValue,
					MappingVersion = transaction.FederatedMappingVersion.Value
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private async Task<DepartmentSsoConfig> ActiveConfigAsync(CancellationToken cancellationToken) =>
			(await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken))?.FirstOrDefault(c => c.IsEnabled);

		private ActionResult<FederatedMfaMappingResult> MappingResult(DepartmentSsoConfig config)
		{
			var result = new FederatedMfaMappingResult
			{
				Data = new FederatedMfaMappingResultData
				{
					DepartmentSsoConfigId = config.DepartmentSsoConfigId,
					ProviderType = ((SsoProviderType)config.SsoProviderType).ToString().ToLowerInvariant(),
					Mapping = string.IsNullOrWhiteSpace(config.FederatedMfaMappingJson) ? null : Newtonsoft.Json.Linq.JToken.Parse(config.FederatedMfaMappingJson),
					MappingVersion = config.FederatedMfaMappingVersion,
					TestedVersion = config.FederatedMfaTestedVersion,
					TestedOn = config.FederatedMfaTestedOnUtc,
					TestedByUserId = config.FederatedMfaTestedByUserId,
					Effective = Resgrid.Model.Security.FederatedMfaMapping.IsTested(config)
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		// ── Test / Validation helpers ─────────────────────────────────────────

		/// <summary>
		/// Validates that the SCIM endpoint is reachable and the stored SCIM bearer token
		/// is correctly configured by performing a test GET /scim/v2/Users request against
		/// the local SCIM controller. Returns success/failure and the HTTP status received.
		/// </summary>
		[HttpGet("TestScimConnection/{providerType}")]
		[Authorize(Policy = ResgridResources.Sso_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<SsoOperationResult>> TestScimConnection(
			string providerType,
			CancellationToken cancellationToken)
		{
			if (!await IsAdminAsync()) return Forbid();

			if (!Enum.TryParse<SsoProviderType>(providerType, ignoreCase: true, out var provider) || !Enum.IsDefined(provider))
				return BadRequest(new { error = "Invalid providerType." });

			var config = await _ssoService.GetSsoConfigForDepartmentAsync(DepartmentId, provider, cancellationToken);

			var result = new SsoOperationResult();
			ResponseHelper.PopulateV4ResponseData(result);

			if (config == null || !config.ScimEnabled || string.IsNullOrWhiteSpace(config.EncryptedScimBearerToken))
			{
				result.Status = ResponseHelper.Failure;
				result.Success = false;
				return Ok(result);
			}

			// A token exists and SCIM is enabled — connection considered configured
			result.Status = ResponseHelper.Success;
			result.Success = true;
			return Ok(result);
		}

		// ── Private helpers ───────────────────────────────────────────────────

		private async Task<bool> IsManagingMemberAsync()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			return department != null && department.ManagingUserId == UserId;
		}

		private async Task<bool> IsAdminAsync()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			return department != null && department.IsUserAnAdmin(UserId);
		}

		private static string ValidateSsoConfiguration(SaveSsoConfigInput input, SsoProviderType providerType, DepartmentSsoConfig existing)
		{
			if (providerType == SsoProviderType.Oidc)
			{
				var clientId = input.ClientId ?? existing?.ClientId;
				var authorityValue = input.Authority ?? existing?.Authority;
				if (string.IsNullOrWhiteSpace(clientId))
					return "OIDC clientId is required.";

				if (!Uri.TryCreate(authorityValue, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps)
					return "OIDC authority must be a valid HTTPS URL.";

				return null;
			}

			if (string.IsNullOrWhiteSpace(input.EntityId ?? existing?.EntityId))
				return "SAML entityId is required.";

			var assertionConsumerServiceUrl = input.AssertionConsumerServiceUrl ?? existing?.AssertionConsumerServiceUrl;
			if (!Uri.TryCreate(assertionConsumerServiceUrl, UriKind.Absolute, out var acsUri) || acsUri.Scheme != Uri.UriSchemeHttps)
				return "SAML assertionConsumerServiceUrl must be a valid HTTPS URL.";

			if (string.IsNullOrWhiteSpace(input.IdpCertificate) && string.IsNullOrWhiteSpace(existing?.EncryptedIdpCertificate))
				return "An IdP signing certificate is required to validate SAML assertions.";

			var idpSsoUrl = input.IdpSsoUrl ?? existing?.IdpSsoUrl;
			if (!string.IsNullOrWhiteSpace(idpSsoUrl) && (!Uri.TryCreate(idpSsoUrl, UriKind.Absolute, out var idpSso) || idpSso.Scheme != Uri.UriSchemeHttps))
				return "SAML idpSsoUrl must be a valid HTTPS URL.";

			return null;
		}

		private static DepartmentSsoConfig BuildConfigFromInput(
			SaveSsoConfigInput input,
			SsoProviderType providerType,
			int departmentId,
			string userId)
		{
			return new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				SsoProviderType = (int)providerType,
				IsEnabled = input.IsEnabled,
				ClientId = input.ClientId,
				// Plaintext secrets — SaveSsoConfigAsync will encrypt these
				EncryptedClientSecret = input.ClientSecret,
				Authority = input.Authority,
				MetadataUrl = input.MetadataUrl,
				EntityId = input.EntityId,
				AssertionConsumerServiceUrl = input.AssertionConsumerServiceUrl,
				IdpSsoUrl = input.IdpSsoUrl,
				EncryptedIdpCertificate = input.IdpCertificate,
				EncryptedSigningCertificate = input.SigningCertificate,
				AttributeMappingJson = input.AttributeMappingJson,
				AllowLocalLogin = input.AllowLocalLogin,
				AutoProvisionUsers = input.AutoProvisionUsers,
				DefaultRankId = input.DefaultRankId,
				ScimEnabled = input.ScimEnabled,
				CreatedByUserId = userId,
				CreatedOn = DateTime.UtcNow
			};
		}

		private static SsoConfigSummaryData MapToSummary(DepartmentSsoConfig c) =>
			new SsoConfigSummaryData
			{
				DepartmentSsoConfigId = c.DepartmentSsoConfigId,
				ProviderType = ((SsoProviderType)c.SsoProviderType).ToString().ToLowerInvariant(),
				IsEnabled = c.IsEnabled,
				Identifier = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.ClientId : c.EntityId,
				EndpointUrl = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.Authority : c.MetadataUrl,
				AllowLocalLogin = c.AllowLocalLogin,
				AutoProvisionUsers = c.AutoProvisionUsers,
				ScimEnabled = c.ScimEnabled,
				CreatedOn = c.CreatedOn,
				UpdatedOn = c.UpdatedOn
			};

		private static SsoConfigDetailData MapToDetail(DepartmentSsoConfig c) =>
			new SsoConfigDetailData
			{
				DepartmentSsoConfigId = c.DepartmentSsoConfigId,
				ProviderType = ((SsoProviderType)c.SsoProviderType).ToString().ToLowerInvariant(),
				IsEnabled = c.IsEnabled,
				Identifier = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.ClientId : c.EntityId,
				EndpointUrl = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.Authority : c.MetadataUrl,
				AllowLocalLogin = c.AllowLocalLogin,
				AutoProvisionUsers = c.AutoProvisionUsers,
				ScimEnabled = c.ScimEnabled,
				CreatedOn = c.CreatedOn,
				UpdatedOn = c.UpdatedOn,
				// Detail fields
				Authority = c.Authority,
				ClientId = c.ClientId,
				MetadataUrl = c.MetadataUrl,
				EntityId = c.EntityId,
				AssertionConsumerServiceUrl = c.AssertionConsumerServiceUrl,
				IdpSsoUrl = c.IdpSsoUrl,
				OidcBrokerRedirectUri = c.SsoProviderType == (int)SsoProviderType.Oidc
					? $"{Config.SystemBehaviorConfig.ResgridApiBaseUrl?.TrimEnd('/')}{Config.SsoConfig.OidcCallbackPath}"
					: null,
				OidcAppRedirectUris = c.SsoProviderType == (int)SsoProviderType.Oidc
					? LegacyAppCallbacks.RedirectUris(Config.SsoConfig.AppWebOrigins)
						.Select(a => new SsoAppRedirectUriData { Client = a.Name, DisplayName = a.DisplayName, Web = a.Web, RedirectUri = a.Uri }).ToList()
					: null,
				AttributeMappingJson = c.AttributeMappingJson,
				DefaultRankId = c.DefaultRankId,
				// Secret presence flags — values never returned
				HasClientSecret = !string.IsNullOrWhiteSpace(c.EncryptedClientSecret),
				HasIdpCertificate = !string.IsNullOrWhiteSpace(c.EncryptedIdpCertificate),
				HasSigningCertificate = !string.IsNullOrWhiteSpace(c.EncryptedSigningCertificate),
				HasScimBearerToken = !string.IsNullOrWhiteSpace(c.EncryptedScimBearerToken)
			};

		private static SecurityPolicyData MapToSecurityPolicyData(DepartmentSecurityPolicy p) =>
			new SecurityPolicyData
			{
				DepartmentSecurityPolicyId = p.DepartmentSecurityPolicyId,
				RequireMfa = p.RequireMfa,
				RequireSso = p.RequireSso,
				SessionTimeoutMinutes = p.SessionTimeoutMinutes,
				MaxConcurrentSessions = p.MaxConcurrentSessions,
				AllowedIpRanges = p.AllowedIpRanges,
				PasswordExpirationDays = p.PasswordExpirationDays,
				MinPasswordLength = p.MinPasswordLength,
				RequirePasswordComplexity = p.RequirePasswordComplexity,
				DataClassificationLevel = p.DataClassificationLevel,
				AllowPasskeysForLoginMfa = p.AllowPasskeysForLoginMfa,
				AllowPasskeysForAdp = p.AllowPasskeysForAdp,
				AllowFederatedMfaForLoginMfa = p.AllowFederatedMfaForLoginMfa,
				AllowFederatedMfaForAdp = p.AllowFederatedMfaForAdp,
				AllowResponderApproval = p.AllowResponderApproval,
				AcceptRecentLoginMfaForAdp = p.AcceptRecentLoginMfaForAdp,
				AcceptRecentUnlockMfaForAdp = p.AcceptRecentUnlockMfaForAdp,
				SharedIdleLockMinutes = p.SharedIdleLockMinutes,
				SharedShiftHours = p.SharedShiftHours,
				SharedModeRequiredApps = p.SharedModeRequiredApps,
				MfaPolicyVersion = p.MfaPolicyVersion,
				CreatedOn = p.CreatedOn,
				UpdatedOn = p.UpdatedOn
			};
	}
}

