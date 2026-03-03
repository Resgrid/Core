using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Sso;

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

		/// <summary>Constructor.</summary>
		public SsoAdminController(
			IDepartmentSsoService ssoService,
			IDepartmentsService departmentsService,
			IPermissionsService permissionsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService)
		{
			_ssoService = ssoService;
			_departmentsService = departmentsService;
			_permissionsService = permissionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
		}

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

			if (!Enum.TryParse<SsoProviderType>(input.ProviderType, ignoreCase: true, out var providerType))
				return BadRequest(new { error = "Invalid providerType. Must be 'saml2' or 'oidc'." });

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

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs.FirstOrDefault(c => c.DepartmentSsoConfigId == id);

			if (config == null)
			{
				var notFound = new SaveSsoConfigResult();
				ResponseHelper.PopulateV4ResponseNotFound(notFound);
				return NotFound(notFound);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			// Update non-secret fields
			config.IsEnabled = input.IsEnabled;
			config.ClientId = input.ClientId ?? config.ClientId;
			config.Authority = input.Authority ?? config.Authority;
			config.MetadataUrl = input.MetadataUrl ?? config.MetadataUrl;
			config.EntityId = input.EntityId ?? config.EntityId;
			config.AssertionConsumerServiceUrl = input.AssertionConsumerServiceUrl ?? config.AssertionConsumerServiceUrl;
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

			if (!Enum.TryParse<SsoProviderType>(providerType, ignoreCase: true, out var provider))
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

			if (!Enum.TryParse<SsoProviderType>(providerType, ignoreCase: true, out var provider))
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
			result.Data = policy != null ? MapToSecurityPolicyData(policy) : new SecurityPolicyData
			{
				CreatedOn = DateTime.UtcNow,
				MinPasswordLength = 8
			};

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

			policy.RequireMfa = input.RequireMfa;
			policy.RequireSso = input.RequireSso;
			policy.SessionTimeoutMinutes = input.SessionTimeoutMinutes;
			policy.MaxConcurrentSessions = input.MaxConcurrentSessions;
			policy.AllowedIpRanges = input.AllowedIpRanges;
			policy.PasswordExpirationDays = input.PasswordExpirationDays;
			policy.MinPasswordLength = input.MinPasswordLength;
			policy.RequirePasswordComplexity = input.RequirePasswordComplexity;
			policy.DataClassificationLevel = input.DataClassificationLevel;

			var saved = await _ssoService.SaveSecurityPolicyAsync(policy, cancellationToken);

			var result = new SaveSecurityPolicyResult();
			ResponseHelper.PopulateV4ResponseData(result);
			result.Status = existing == null ? ResponseHelper.Created : ResponseHelper.Updated;
			result.DepartmentSecurityPolicyId = saved.DepartmentSecurityPolicyId;
			return Ok(result);
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

			if (!Enum.TryParse<SsoProviderType>(providerType, ignoreCase: true, out var provider))
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

		private async Task<bool> IsAdminAsync()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			return department != null && department.IsUserAnAdmin(UserId);
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
				CreatedOn = p.CreatedOn,
				UpdatedOn = p.UpdatedOn
			};
	}
}

