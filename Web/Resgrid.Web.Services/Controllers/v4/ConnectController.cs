using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Sso;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Service to generate an authentication token that is required to communicate with all other v4 services
	/// </summary>
#if (!DEBUG && !DOCKER)
	//[RequireHttps]
#endif
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiController]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ConnectController : ControllerBase
	{
		private readonly SignInManager<Model.Identity.IdentityUser> _signInManager;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly IDepartmentSsoService _departmentSsoService;
		private readonly IEncryptionService _encryptionService;

		public ConnectController(
			IUsersService usersService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService,
			SignInManager<Model.Identity.IdentityUser> signInManager,
			UserManager<Model.Identity.IdentityUser> userManager,
			ISystemAuditsService systemAuditsService,
			IDepartmentSsoService departmentSsoService,
			IEncryptionService encryptionService
			)
		{
			_usersService = usersService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
			_signInManager = signInManager;
			_userManager = userManager;
			_systemAuditsService = systemAuditsService;
			_departmentSsoService = departmentSsoService;
			_encryptionService = encryptionService;
		}

		/// <summary>
		/// Generates a token that is then used for subsquent requests to the API.
		/// </summary>
		/// <returns>ValidateResult object, with IsValid set if the settings are correct</returns>
		[HttpPost("token")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Produces("application/json")]
		public async Task<IActionResult> Token()
		{
			var request = HttpContext.GetOpenIddictServerRequest();
			if (request != null && request.IsPasswordGrantType())
			{
				SystemAudit audit = new SystemAudit();
				audit.System = (int)SystemAuditSystems.Api;
				audit.Type = (int)SystemAuditTypes.Login;
				audit.Username = request.Username;
				audit.Successful = false;
				audit.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				audit.ServerName = Environment.MachineName;
				audit.Data = $"V4 Token, {Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

				var user = await _userManager.FindByNameAsync(request.Username);
				if (user == null)
				{
					await _systemAuditsService.SaveSystemAuditAsync(audit);

					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
							"The username or password is invalid."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				// Validate the username/password parameters and ensure the account is not locked out.
				var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

				audit.UserId = user.Id;
				audit.Successful = result.Succeeded;
				await _systemAuditsService.SaveSystemAuditAsync(audit);

				if (!result.Succeeded)
				{
					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
							"The username or password is invalid."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				// SSO-only guard — only enforced when the user's department has explicitly
				// configured RequireSso=true AND has at least one active SSO configuration.
				// Departments that have NOT configured SSO are completely unaffected.
				var userDepartment = await _departmentsService.GetDepartmentByUserIdAsync(user.Id);
				if (userDepartment != null)
				{
					var requiresSso = await _departmentSsoService.IsRequireSsoPolicyActiveAsync(userDepartment.DepartmentId, CancellationToken.None);
					var hasSso = requiresSso && await _departmentSsoService.IsSsoEnabledForDepartmentAsync(userDepartment.DepartmentId, CancellationToken.None);
					if (requiresSso && hasSso)
					{
						var ssoProps = new AuthenticationProperties(new Dictionary<string, string>
						{
							[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
							[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
								"This department requires SSO login. Password-based login is disabled."
						});
						return Forbid(ssoProps, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
					}
				}

				// Create a new ClaimsPrincipal containing the claims that
				// will be used to create an id_token, a token or a code.
				var principal = await _signInManager.CreateUserPrincipalAsync(user);

				// Set the list of scopes granted to the client application.
				// Note: the offline_access scope must be granted
				// to allow OpenIddict to return a refresh token.
				principal.SetScopes(new[]
				{
					Scopes.OpenId,
					Scopes.Email,
					Scopes.Profile,
					Scopes.OfflineAccess,
					Scopes.Roles
				}.Intersect(request.GetScopes()));

				foreach (var claim in principal.Claims)
				{
					claim.SetDestinations(GetDestinations(claim, principal));
				}

				if (request.GetScopes() != null && request.GetScopes().Contains("mobile"))
				{
					principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
					principal.SetRefreshTokenLifetime(TimeSpan.FromDays(OidcConfig.RefreshTokenExpiryDays));
				}
				else
				{
					principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
					principal.SetRefreshTokenLifetime(TimeSpan.FromDays(OidcConfig.NonMobileRefreshTokenExpiryDays));
				}

				principal.SetResources(JwtConfig.EventsClientId);

				return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
			}

			else if (request != null && request.IsRefreshTokenGrantType())
			{
				// Retrieve the claims principal stored in the refresh token.
				var info = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

				// Retrieve the user profile corresponding to the refresh token.
				// Note: if you want to automatically invalidate the refresh token
				// when the user password/roles change, use the following line instead:
				// var user = _signInManager.ValidateSecurityStampAsync(info.Principal);
				var user = await _userManager.GetUserAsync(info.Principal);
				if (user == null)
				{
					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token is no longer valid."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				// Ensure the user is still allowed to sign in.
				if (!await _signInManager.CanSignInAsync(user))
				{
					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				// Create a new ClaimsPrincipal containing the claims that
				// will be used to create an id_token, a token or a code.
				var principal = await _signInManager.CreateUserPrincipalAsync(user);

				foreach (var claim in principal.Claims)
				{
					claim.SetDestinations(GetDestinations(claim, principal));
				}

				return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
			}

			throw new NotImplementedException("The specified grant type is not implemented.");
		}

		/// <summary>
		/// Returns the SSO configuration for a department so the mobile app can determine
		/// whether to show the SSO login button, which flow to use (OIDC/SAML), and which
		/// parameters to pass. Call this before showing the login screen.
		/// </summary>
		/// <param name="departmentCode">The department code shown on the department's settings page.</param>
		/// <param name="cancellationToken"></param>
		[HttpGet("sso-config")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Produces("application/json")]
		public async Task<ActionResult<GetDepartmentSsoConfigResult>> GetSsoConfig(
			[FromQuery] string departmentToken,
			[FromQuery] string departmentCode,
			CancellationToken cancellationToken)
		{
			var result = new GetDepartmentSsoConfigResult();
			ResponseHelper.PopulateV4ResponseData(result);

			if (string.IsNullOrWhiteSpace(departmentToken) && string.IsNullOrWhiteSpace(departmentCode))
			{
				result.Status = ResponseHelper.Failure;
				result.PageSize = 0;
				return BadRequest(result);
			}

			var department = await ResolveDepartmentAsync(departmentToken, departmentCode);
			if (department == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			// Load all SSO configs for the department
			var ssoConfigs = await _departmentSsoService.GetSsoConfigsForDepartmentAsync(department.DepartmentId, cancellationToken);
			var activeConfig = ssoConfigs?.FirstOrDefault(c => c.IsEnabled);

			// Load security policy for RequireSso / RequireMfa flags
			var policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(department.DepartmentId, cancellationToken);

			result.Status = ResponseHelper.Success;
			result.PageSize = 1;

			if (activeConfig == null)
			{
				// No SSO — local login only
				result.Data.SsoEnabled = false;
				result.Data.AllowLocalLogin = true;
				result.Data.RequireSso = false;
				result.Data.RequireMfa = policy?.RequireMfa ?? false;
				return Ok(result);
			}

			var providerType = (SsoProviderType)activeConfig.SsoProviderType;

			result.Data.SsoEnabled = true;
			result.Data.ProviderType = providerType.ToString().ToLowerInvariant();
			result.Data.AllowLocalLogin = activeConfig.AllowLocalLogin;
			result.Data.RequireSso = policy?.RequireSso ?? false;
			result.Data.RequireMfa = policy?.RequireMfa ?? false;

			if (providerType == SsoProviderType.Oidc)
			{
				result.Data.Authority = activeConfig.Authority;
				result.Data.ClientId = activeConfig.ClientId; // public client ID — safe to expose
				result.Data.OidcRedirectUri = "resgrid://auth/callback";
				result.Data.OidcScopes = "openid email profile offline_access";
			}
			else if (providerType == SsoProviderType.Saml2)
			{
				result.Data.MetadataUrl = activeConfig.MetadataUrl;
				result.Data.EntityId = activeConfig.EntityId;
			}

			return Ok(result);
		}

		/// <summary>
		/// Exchanges an external SSO token (OIDC id_token or base64-encoded SAMLResponse) for a
		/// Resgrid access token. Supports grant_type=external_token with fields:
		///   provider (saml2|oidc), external_token, department_code, scope (optional),
		///   totp_code (required when the user has Resgrid 2FA enrolled).
		/// SSO authentication does NOT bypass Resgrid's own Two-Factor Authentication.
		/// When the user has 2FA enabled in Resgrid, a valid totp_code must be supplied
		/// in addition to the IdP token.
		/// </summary>
		[HttpPost("external-token")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Produces("application/json")]
		public async Task<IActionResult> ExternalToken(
			[FromForm] string provider,
			[FromForm] string external_token,
			[FromForm] string department_code,
			[FromForm] string scope,
			[FromForm] string totp_code,
			CancellationToken cancellationToken)
		{
			var audit = new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.SsoLogin,
				Username = department_code,
				Successful = false,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"ExternalToken provider={provider}, {Request.Headers["User-Agent"]}"
			};

			if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(external_token) || string.IsNullOrWhiteSpace(department_code))
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return BadRequest(new { error = "invalid_request", error_description = "provider, external_token, and department_code are required." });
			}

			// Resolve department by department code
			var department = await _departmentsService.GetDepartmentByNameAsync(department_code);
			if (department == null)
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return Unauthorized(new { error = "invalid_grant", error_description = "Invalid department_code." });
			}

			// Parse provider type
			if (!Enum.TryParse<SsoProviderType>(provider, ignoreCase: true, out var providerType))
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return BadRequest(new { error = "invalid_request", error_description = "provider must be 'saml2' or 'oidc'." });
			}

			// Validate the external token against the department's SSO config
			var externalPrincipal = await _departmentSsoService.ValidateExternalTokenAsync(
				department.DepartmentId, providerType, external_token, department.Code, cancellationToken);

			if (externalPrincipal == null)
			{
				audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return Unauthorized(new { error = "invalid_grant", error_description = "The external token could not be validated." });
			}

			// Get the SSO config to pass to provisioning
			var ssoConfig = await _departmentSsoService.GetSsoConfigForDepartmentAsync(department.DepartmentId, providerType, cancellationToken);

			// Provision or link the user
			var user = await _departmentSsoService.ProvisionOrLinkUserAsync(
				department.DepartmentId, externalPrincipal, ssoConfig, department.Code, cancellationToken);

			if (user == null)
			{
				audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return Unauthorized(new { error = "invalid_grant", error_description = "No matching user found and auto-provisioning is disabled." });
			}

			// ── Resgrid 2FA check ────────────────────────────────────────────────────
			// SSO does NOT bypass Resgrid's own Two-Factor Authentication.
			// If the user has 2FA enrolled in Resgrid, they MUST supply a valid TOTP code
			// alongside their IdP token. This is independent of the department security
			// policy's RequireMfa flag — it applies to every user with 2FA enabled.
			var resgridTwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
			bool mfaCompleted = false;

			if (resgridTwoFactorEnabled)
			{
				if (string.IsNullOrWhiteSpace(totp_code))
				{
					audit.UserId = user.Id;
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new
					{
						error = "mfa_required",
						error_description = "Your Resgrid account has Two-Factor Authentication enabled. Please include your current totp_code with this request."
					});
				}

				// Verify the TOTP code against the Resgrid authenticator
				var totpValid = await _userManager.VerifyTwoFactorTokenAsync(
					user,
					_userManager.Options.Tokens.AuthenticatorTokenProvider,
					totp_code);

				if (!totpValid)
				{
					audit.UserId = user.Id;
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new
					{
						error = "invalid_totp",
						error_description = "The provided Two-Factor Authentication code is invalid or has expired."
					});
				}

				mfaCompleted = true;
			}

			// Enforce security policy (IP ranges, RequireMfa, RequireSso).
			// mfaCompleted reflects whether Resgrid 2FA was satisfied above.
			// This is only enforced for departments that have an explicit policy saved —
			// departments without any policy are completely unaffected (returns null).
			var policyViolation = await _departmentSsoService.EnforceSecurityPolicyAsync(
				department.DepartmentId, user.Id,
				IpAddressHelper.GetRequestIP(Request, true),
				mfaCompleted: mfaCompleted,
				loginViaSso: true,
				cancellationToken);

			if (!string.IsNullOrWhiteSpace(policyViolation))
			{
				audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
				audit.UserId = user.Id;
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return Unauthorized(new { error = "access_denied", error_description = policyViolation });
			}

			// Issue an OpenIddict access token
			var principal = await _signInManager.CreateUserPrincipalAsync(user);

			principal.SetScopes(new[]
			{
				Scopes.OpenId,
				Scopes.Email,
				Scopes.Profile,
				Scopes.OfflineAccess,
				Scopes.Roles
			});

			foreach (var claim in principal.Claims)
				claim.SetDestinations(GetDestinations(claim, principal));

			// Mirror the mobile-scope lifetime logic from the password-grant Token endpoint
			var isMobile = !string.IsNullOrWhiteSpace(scope) &&
				scope.Split(' ').Any(s => string.Equals(s, "mobile", StringComparison.OrdinalIgnoreCase));

			principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
			principal.SetRefreshTokenLifetime(isMobile
				? TimeSpan.FromDays(OidcConfig.RefreshTokenExpiryDays)
				: TimeSpan.FromDays(OidcConfig.NonMobileRefreshTokenExpiryDays));

			principal.SetResources(JwtConfig.EventsClientId);

			audit.Successful = true;
			audit.UserId = user.Id;
			await _systemAuditsService.SaveSystemAuditAsync(audit);

			return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
		}

		/// <summary>
		/// SAML 2.0 Assertion Consumer Service (ACS) relay for mobile apps.
		/// Receives the SAMLResponse POST from the IdP, then redirects to the
		/// resgrid:// deep-link scheme so the mobile app can complete authentication
		/// via the external-token endpoint.
		/// Configure your IdP's ACS URL to point here:
		///   POST /api/v4/connect/saml-mobile-callback?departmentCode=DEPT
		/// </summary>
		[HttpPost("saml-mobile-callback")]
		[HttpGet("saml-mobile-callback")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status302Found)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<IActionResult> SamlMobileCallback(
			[FromQuery] string departmentToken,
			[FromQuery] string departmentCode,
			[FromForm] string SAMLResponse,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(SAMLResponse))
				return BadRequest(new { error = "invalid_request", error_description = "SAMLResponse is required." });

			if (string.IsNullOrWhiteSpace(departmentToken) && string.IsNullOrWhiteSpace(departmentCode))
				return BadRequest(new { error = "invalid_request", error_description = "departmentToken or departmentCode query parameter is required." });

			var department = await ResolveDepartmentAsync(departmentToken, departmentCode);
			if (department == null)
				return BadRequest(new { error = "invalid_request", error_description = "Unknown or invalid department token." });

			var encodedResponse = Uri.EscapeDataString(SAMLResponse);
			// Pass the encrypted token to the deep link so the mobile app can use it with external-token
			var encodedToken = Uri.EscapeDataString(departmentToken ?? Uri.EscapeDataString(department.Code));

			var deepLink = $"resgrid://auth/callback?saml_response={encodedResponse}&department_token={encodedToken}";
			return Redirect(deepLink);
		}

		/// <summary>
		/// Decrypts a departmentToken (format: {departmentId}:{departmentCode}) produced by the
		/// web UI and returns the resolved Department, or null if the token is invalid.
		/// Falls back to a plain departmentCode name-lookup when departmentToken is absent.
		/// </summary>
		private async Task<Model.Department> ResolveDepartmentAsync(string departmentToken, string departmentCodeFallback)
		{
			if (!string.IsNullOrWhiteSpace(departmentToken))
			{
				try
				{
					// First pass: decrypt using just the system key to obtain the plain payload
					var plain = _encryptionService.Decrypt(departmentToken);
					var parts = plain.Split(':');
					if (parts.Length >= 2 && int.TryParse(parts[0], out var deptId))
					{
						var deptCode = string.Join(":", parts.Skip(1));
						// Second pass: verify with the department-specific key
						var verify = _encryptionService.DecryptForDepartment(
							_encryptionService.EncryptForDepartment(plain, deptId, deptCode),
							deptId, deptCode);
						if (verify == plain)
							return await _departmentsService.GetDepartmentByIdAsync(deptId);
					}
				}
				catch
				{
					// Fall through to name-based lookup
				}
			}

			if (!string.IsNullOrWhiteSpace(departmentCodeFallback))
				return await _departmentsService.GetDepartmentByNameAsync(departmentCodeFallback);

			return null;
		}

		private IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)		{
			// Note: by default, claims are NOT automatically included in the access and identity tokens.
			// To allow OpenIddict to serialize them, you must attach them a destination, that specifies
			// whether they should be included in access tokens, in identity tokens or in both.

			switch (claim.Type)
			{
				case Claims.Name:
					yield return Destinations.AccessToken;

					if (principal.HasScope(Scopes.Profile))
						yield return Destinations.IdentityToken;

					yield break;

				case Claims.Email:
					yield return Destinations.AccessToken;

					if (principal.HasScope(Scopes.Email))
						yield return Destinations.IdentityToken;

					yield break;

				case Claims.Role:
					yield return Destinations.AccessToken;

					if (principal.HasScope(Scopes.Roles))
						yield return Destinations.IdentityToken;

					yield break;

				// Never include the security stamp in the access and identity tokens, as it's a secret value.
				case "AspNet.Identity.SecurityStamp": yield break;

				default:
					yield return Destinations.AccessToken;
					yield break;
			}
		}
	}
}
