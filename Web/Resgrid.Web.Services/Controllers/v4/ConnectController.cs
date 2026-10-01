using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Model.Security;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Sso;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using Resgrid.Providers.Claims;
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
		private const string SamlRelayTokenPrefix = "saml-relay:";
		private static readonly TimeSpan SamlRelayLifetime = TimeSpan.FromMinutes(5);

		private readonly SignInManager<Model.Identity.IdentityUser> _signInManager;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly IDepartmentSsoService _departmentSsoService;
		private readonly IEncryptionService _encryptionService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IUserSessionService _userSessionService;
		private readonly IExternalIdentityLinkService _externalIdentityLinkService;

		public ConnectController(
			IUsersService usersService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService,
			SignInManager<Model.Identity.IdentityUser> signInManager,
			UserManager<Model.Identity.IdentityUser> userManager,
			ISystemAuditsService systemAuditsService,
			IDepartmentSsoService departmentSsoService,
			IEncryptionService encryptionService,
			ICacheProvider cacheProvider,
			IUserSessionService userSessionService,
			IExternalIdentityLinkService externalIdentityLinkService,
			IMfaPolicyService mfaPolicyService,
			IMfaEvidenceService mfaEvidenceService,
			IMfaLoginTransactionService loginTransactions,
			ISsoBrokerService ssoBroker
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
			_cacheProvider = cacheProvider;
			_userSessionService = userSessionService;
			_externalIdentityLinkService = externalIdentityLinkService;
			_mfaPolicyService = mfaPolicyService;
			_mfaEvidenceService = mfaEvidenceService;
			_loginTransactions = loginTransactions;
			_ssoBroker = ssoBroker;
		}

		private readonly ISsoBrokerService _ssoBroker;

		private readonly IMfaPolicyService _mfaPolicyService;
		private readonly IMfaEvidenceService _mfaEvidenceService;
		private readonly IMfaLoginTransactionService _loginTransactions;

		/// <summary>Where a member whose department requires MFA sets up an authenticator (plan section 7.6 rollout).</summary>
		private static string MfaEnrollmentUri => $"{SystemBehaviorConfig.ResgridBaseUrl?.TrimEnd('/')}/User/TwoFactor";

		private const string MfaEnrollmentRequiredDescription =
			"Your department requires multi-factor authentication. Set up an authenticator app in Resgrid on the web (Account, Two-Factor Authentication), then sign in again.";

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

				var userDepartment = await _departmentsService.GetDepartmentByUserIdAsync(user.Id);
				if (userDepartment == null)
				{
					audit.UserId = user.Id;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return InvalidGrant("The username or password is invalid.");
				}
				var activeMembership = await _departmentsService.GetDepartmentMemberAsync(user.Id,
					userDepartment.DepartmentId, bypassCache: true);
				if (activeMembership == null || activeMembership.IsDeleted || activeMembership.IsDisabled == true)
				{
					audit.UserId = user.Id;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return InvalidGrant("The username or password is invalid.");
				}

				if (!await IsLocalLoginAllowedAsync(user.Id, userDepartment.DepartmentId))
				{
					audit.UserId = user.Id;
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

				// ── Resgrid 2FA challenge ────────────────────────────────────────────────
				// A password alone is insufficient for an account with Two-Factor enabled: the
				// current authenticator code must accompany the request as totp_code. This closes
				// the gap where only the SSO exchange enforced 2FA. The code is checked AFTER the
				// password so this endpoint never becomes a TOTP oracle for unauthenticated callers.
				DateTime? totpVerifiedOnUtc = null;
				if (await _userManager.GetTwoFactorEnabledAsync(user))
				{
					// A client that asked for the login transaction completes its second factor there, with any accepted
					// method, and never resends the password (plan section 7.5 rules 3-4). Everyone else keeps totp_code.
					if (_loginTransactions.IsEnabled && string.Equals((string)request.GetParameter(MfaLoginTransactions.FlowParameter),
							MfaLoginTransactions.FlowValue, StringComparison.Ordinal))
					{
						var started = await BeginLoginTransactionAsync(user, userDepartment.DepartmentId, request, audit);
						if (started != null)
							return started;
					}

					var totpCode = (string)request.GetParameter("totp_code");
					if (string.IsNullOrWhiteSpace(totpCode))
					{
						audit.Successful = false;
						audit.Data += " (mfa_required)";
						await _systemAuditsService.SaveSystemAuditAsync(audit);

						return Forbid(new AuthenticationProperties(new Dictionary<string, string>
						{
							[OpenIddictServerAspNetCoreConstants.Properties.Error] = "mfa_required",
							[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
								"Two-factor authentication is enabled for this account. Include your current totp_code with this request."
						}), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
					}

					var totpValid = await _userManager.VerifyTwoFactorTokenAsync(user,
						_userManager.Options.Tokens.AuthenticatorTokenProvider, totpCode.Trim());
					if (!totpValid)
					{
						// A caller who already holds the password must not get unlimited code
						// guesses: count the failure against the same Identity lockout the
						// password check uses.
						await _userManager.AccessFailedAsync(user);

						audit.Successful = false;
						audit.Data += " (invalid_totp)";
						await _systemAuditsService.SaveSystemAuditAsync(audit);

						return Forbid(new AuthenticationProperties(new Dictionary<string, string>
						{
							[OpenIddictServerAspNetCoreConstants.Properties.Error] = "invalid_totp",
							[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
								"The two-factor authentication code is invalid or has expired."
						}), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
					}

					totpVerifiedOnUtc = DateTime.UtcNow;
				}
				else if (await _mfaPolicyService.IsRequireMfaEnforcedAsync(userDepartment.DepartmentId))
				{
					// A member with no Resgrid factor can still meet RequireMfa through the department's provider step-up, where
					// it accepts that for sign-in (plan sections 7.6 row 3 and 7.8), but only through the login transaction.
					if (_loginTransactions.IsEnabled && string.Equals((string)request.GetParameter(MfaLoginTransactions.FlowParameter),
							MfaLoginTransactions.FlowValue, StringComparison.Ordinal) &&
						await _mfaPolicyService.IsMethodAcceptedAsync(userDepartment.DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated) &&
						await _departmentSsoService.IsFederatedMfaAvailableAsync(userDepartment.DepartmentId, user.Id))
					{
						var started = await BeginLoginTransactionAsync(user, userDepartment.DepartmentId, request, audit, totpEnrolled: false);
						if (started != null)
							return started;
					}

					// The department requires MFA and this member has none enrolled (plan section 7.6 row 3): no tokens,
					// and a specific error older app builds can show, with where to enroll. A client using the login
					// transaction also gets a setup transaction to enroll right here (plan section 6.2); it permits nothing else.
					audit.Successful = false;
					audit.Data += " (mfa_enrollment_required)";
					await _systemAuditsService.SaveSystemAuditAsync(audit);

					var enrollment = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = "mfa_enrollment_required",
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = MfaEnrollmentRequiredDescription,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorUri] = MfaEnrollmentUri
					});
					if (_loginTransactions.IsEnabled && string.Equals((string)request.GetParameter(MfaLoginTransactions.FlowParameter),
							MfaLoginTransactions.FlowValue, StringComparison.Ordinal))
					{
						var setup = await BeginSetupTransactionAsync(user, userDepartment.DepartmentId, request);
						if (setup != null)
						{
							enrollment.Parameters["mfa_setup_transaction"] = setup.Secret;
							enrollment.Parameters["mfa_expires_in"] = (long)setup.ExpiresInSeconds;
						}
					}

					return Forbid(enrollment, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
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

				var refreshTokenLifetime = GetRefreshTokenLifetime(request);
				if (SessionSecurityConfig.TrackingEnabled)
				{
					try
					{
						var session = await CreateApiSessionAsync(user, userDepartment?.DepartmentId,
							UserSessionAuthenticationMethod.LocalPassword, refreshTokenLifetime, CancellationToken.None,
							loginMfaMethod: totpVerifiedOnUtc == null ? null : MfaEvidenceMethod.Totp);
						AddSessionClaims(principal, session);
						await RecordLoginEvidenceAsync(user, session, MfaEvidenceMethod.Password, totpVerifiedOnUtc, CancellationToken.None);
					}
					catch (SessionCreationDeniedException ex)
					{
						return InvalidGrant(ex.FailureCode == "maximum_sessions"
							? "The department's maximum number of active sessions has been reached."
							: "The user is no longer allowed to sign in to this department.");
					}
				}

				foreach (var claim in principal.Claims)
				{
					claim.SetDestinations(GetDestinations(claim, principal));
				}

				principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
				principal.SetRefreshTokenLifetime(refreshTokenLifetime);

				principal.SetResources(JwtConfig.EventsClientId);

				return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
			}

			else if (request != null && request.IsRefreshTokenGrantType())
			{
				// Retrieve the claims principal stored in the refresh token.
				var info = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				var user = info.Principal == null ? null : await _signInManager.ValidateSecurityStampAsync(info.Principal);
				if (user == null)
				{
					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token is no longer valid."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				int? departmentId = null;
				if (int.TryParse(info.Principal.FindFirstValue(ClaimTypes.PrimaryGroupSid), out var parsedDepartmentId))
					departmentId = parsedDepartmentId;

				long? authenticationGeneration = null;
				if (long.TryParse(info.Principal.FindFirstValue(SessionClaimTypes.AuthenticationGeneration), out var parsedGeneration))
					authenticationGeneration = parsedGeneration;

				var validation = await _userSessionService.ValidateAsync(new SessionPrincipalContext
				{
					UserId = user.Id,
					SessionId = info.Principal.FindFirstValue(SessionClaimTypes.SessionId),
					AuthenticationGeneration = authenticationGeneration,
					DepartmentId = departmentId,
					CredentialIssuedOn = GetCredentialIssuedOn(info.Principal)
				}, CancellationToken.None);

				if (!validation.IsValid)
				{
					// A locked shared session cannot refresh its way back to active (plan section 12.5.3). The tokens are
					// still what unlocks it, so the client is told to unlock, not to discard them.
					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = validation.IsLocked
							? "The shared session is locked. Unlock it, then refresh."
							: "The refresh token is no longer valid."
					});
					if (validation.IsLocked)
						properties.Parameters["shared_session_locked"] = true;
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
				principal.SetScopes(info.Principal.GetScopes());

				var refreshTokenLifetime = GetRefreshTokenLifetime(request);
				var session = validation.Session;
				if (session == null && SessionSecurityConfig.TrackingEnabled)
				{
					try
					{
						session = await _userSessionService.AdoptLegacyAsync(new LegacySessionContext
						{
							UserId = user.Id,
							DepartmentId = departmentId,
							AuthenticationGeneration = user.AuthenticationGeneration,
							ClientApplication = ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]),
							DeviceName = Request.Headers["X-Resgrid-Device-Name"],
							DeviceType = Request.Headers["X-Resgrid-Device-Type"],
							OperatingSystem = Request.Headers["X-Resgrid-Operating-System"],
							Browser = Request.Headers["X-Resgrid-Browser"],
							ApplicationVersion = Request.Headers["X-Resgrid-App-Version"],
							ExpiresOn = DateTime.UtcNow.Add(refreshTokenLifetime),
							IpAddress = IpAddressHelper.GetRequestIP(Request, true),
							UserAgent = Request.Headers["User-Agent"]
						}, CancellationToken.None);
					}
					catch (SessionCreationDeniedException ex)
					{
						return InvalidGrant(ex.FailureCode == "maximum_sessions"
							? "The department's maximum number of active sessions has been reached."
							: "The user is no longer allowed to sign in to this department.");
					}
				}

				if (session != null)
				{
					AddSessionClaims(principal, session);
					await _userSessionService.TouchAsync(session.UserSessionId, new RequestActivity
					{
						OccurredOn = DateTime.UtcNow,
						IpAddress = IpAddressHelper.GetRequestIP(Request, true),
						UserAgent = Request.Headers["User-Agent"]
					}, CancellationToken.None);
				}

				foreach (var claim in principal.Claims)
				{
					claim.SetDestinations(GetDestinations(claim, principal));
				}

				principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
				principal.SetRefreshTokenLifetime(refreshTokenLifetime);
				principal.SetResources(JwtConfig.EventsClientId);

				return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
			}

			else if (request != null && string.Equals(request.GrantType, "web_session", StringComparison.Ordinal))
			{
				var suppliedKey = Request.Headers["X-Resgrid-Internal-Key"].ToString();
				var userId = request.GetParameter("user_id").ToString();
				var sessionId = request.GetParameter("session_id").ToString();
				var generationValue = request.GetParameter("auth_ver").ToString();
				var departmentValue = request.GetParameter("department_id").ToString();
				var eventingOnly = string.Equals(request.GetParameter("token_use").ToString(),
					"eventing", StringComparison.Ordinal);
				var credentialIssuedOn = ParseUnixSeconds(request.GetParameter("credential_issued_on").ToString());

				if (string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey) ||
					!FixedTimeSecretEquals(ApiConfig.BackendInternalApikey, suppliedKey) ||
					string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId) ||
					!long.TryParse(generationValue, out var generation) ||
					!int.TryParse(departmentValue, out var departmentId))
					return InvalidGrant("The Web session could not be validated.");

				var validation = await _userSessionService.ValidateAsync(new SessionPrincipalContext
				{
					UserId = userId,
					SessionId = sessionId,
					AuthenticationGeneration = generation,
					DepartmentId = departmentId,
					// The caller's web authentication cookie is the credential here. Null when the caller
					// did not supply its issue time, so the session service applies its own policy rather
					// than being handed a value that trivially passes the freshness comparison.
					CredentialIssuedOn = credentialIssuedOn
				}, CancellationToken.None);
				if (!validation.IsValid || validation.Session == null)
					return InvalidGrant("The Web session could not be validated.");

				var user = await _userManager.FindByIdAsync(userId);
				if (user == null || !await _signInManager.CanSignInAsync(user))
					return InvalidGrant("The Web session could not be validated.");

				var principal = await _signInManager.CreateUserPrincipalAsync(user);
				principal.SetScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles);
				AddSessionClaims(principal, validation.Session);
				if (eventingOnly && principal.Identity is ClaimsIdentity eventingIdentity)
					eventingIdentity.AddClaim(new Claim(SessionClaimTypes.WebEventingOnly, "true"));
				foreach (var claim in principal.Claims)
					claim.SetDestinations(GetDestinations(claim, principal));
				principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(eventingOnly
					? Math.Max(1, SessionSecurityConfig.WebEventingAccessTokenLifetimeMinutes)
					: Math.Max(1, SessionSecurityConfig.WebBffAccessTokenLifetimeMinutes)));
				principal.SetResources(JwtConfig.EventsClientId);

				return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
			}

			else if (request != null && request.IsClientCredentialsGrantType())
			{
				// Client Credentials grant_type for SMTP Relay single-department deployments.
				// Validates client_id and client_secret against department credentials or system-level config.

				SystemAudit audit = new SystemAudit();
				audit.System = (int)SystemAuditSystems.Api;
				audit.Type = (int)SystemAuditTypes.Login;
				audit.Username = request.ClientId;
				audit.Successful = false;
				audit.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				audit.ServerName = Environment.MachineName;
				audit.Data = $"V4 Token (client_credentials), {Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

				// Dedicated server-to-server credential for the legacy direct eventing publisher.
				// It can publish to SignalR but is explicitly excluded from user-session handling.
				if (string.Equals(request.ClientId, "resgrid_eventing", StringComparison.Ordinal) &&
					!string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey) &&
					FixedTimeSecretEquals(ApiConfig.BackendInternalApikey, request.ClientSecret))
				{
					audit.Successful = true;
					await _systemAuditsService.SaveSystemAuditAsync(audit);

					var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
						Claims.Name, Claims.Role);
					identity.AddClaim(new Claim(Claims.Subject, "system_eventing")
						.SetDestinations(Destinations.AccessToken));
					identity.AddClaim(new Claim(ClaimTypes.PrimarySid, "system_eventing")
						.SetDestinations(Destinations.AccessToken));
					identity.AddClaim(new Claim(Claims.Name, "Resgrid Eventing Publisher")
						.SetDestinations(Destinations.AccessToken));
					var principal = new ClaimsPrincipal(identity);
					principal.SetScopes(Scopes.OpenId, Scopes.Profile);
					principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(5));
					principal.SetResources(JwtConfig.EventsClientId);
					return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
				{
					await _systemAuditsService.SaveSystemAuditAsync(audit);

					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
							"The client_id and client_secret are required for client_credentials grant."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				// First, check system-level credentials (timing-safe comparison)
			if (Config.SecurityConfig.SystemLoginCredentials.ContainsKey(request.ClientId) &&
				FixedTimeSecretEquals(Config.SecurityConfig.SystemLoginCredentials[request.ClientId], request.ClientSecret))
			{
				audit.Successful = true;
				await _systemAuditsService.SaveSystemAuditAsync(audit);

				// Create a system-level service principal with all claims
				var identity = new ClaimsIdentity(
					OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
					Claims.Name,
					Claims.Role);

				identity.AddClaim(new Claim(Claims.Subject, $"system_{request.ClientId}")
					.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
				identity.AddClaim(new Claim(Claims.Name, $"System Account ({request.ClientId})")
					.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
				identity.AddClaim(new Claim(ClaimTypes.PrimarySid, $"system_{request.ClientId}")
					.SetDestinations(Destinations.AccessToken));
				identity.AddClaim(new Claim(ClaimTypes.PrimaryGroupSid, "0")
					.SetDestinations(Destinations.AccessToken));
				identity.AddClaim(new Claim(ClaimTypes.GivenName, "SMTP Relay System")
					.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
				identity.AddClaim(new Claim(ResgridClaimTypes.Data.DisplayName, "SMTP Relay System")
					.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
				identity.AddClaim(new Claim(ResgridClaimTypes.Data.ServiceAccount, "true")
					.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));

				// Add all resource claims for full access
				AddAllResourceClaims(identity);
				AddSystemRecordViewClaim(identity, 0);

				var principal = new ClaimsPrincipal(identity);

				principal.SetScopes(new[]
				{
					Scopes.OpenId,
					Scopes.Email,
					Scopes.Profile
				}.Intersect(request.GetScopes()));

				principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));

				return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
			}

			// Second, try department-level credentials via department code + shared secret
			var department = await _departmentsService.GetDepartmentByNameAsync(request.ClientId);

			if (department == null)
				{
					await _systemAuditsService.SaveSystemAuditAsync(audit);

					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
							"The client_id or client_secret is invalid."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				if (string.IsNullOrWhiteSpace(department.SharedSecret) ||
					!FixedTimeSecretEquals(department.SharedSecret, request.ClientSecret))
				{
					await _systemAuditsService.SaveSystemAuditAsync(audit);

					var properties = new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
							"The client_id or client_secret is invalid."
					});

					return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
				}

				audit.Successful = true;
				audit.UserId = department.ManagingUserId;
				await _systemAuditsService.SaveSystemAuditAsync(audit);

				// Create a department-scoped service principal
				var deptIdentity = new ClaimsIdentity(
					OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
					Claims.Name,
					Claims.Role);

				deptIdentity.AddClaim(new Claim(Claims.Subject, $"dept_{department.DepartmentId}_svc")
					.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
				deptIdentity.AddClaim(new Claim(Claims.Name, $"svc_{request.ClientId}")
					.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
				deptIdentity.AddClaim(new Claim(ClaimTypes.PrimarySid, $"dept_{department.DepartmentId}_svc")
					.SetDestinations(Destinations.AccessToken));
				deptIdentity.AddClaim(new Claim(ClaimTypes.PrimaryGroupSid, department.DepartmentId.ToString())
					.SetDestinations(Destinations.AccessToken));
				deptIdentity.AddClaim(new Claim(ClaimTypes.Actor, department.Name)
					.SetDestinations(Destinations.AccessToken));

				// Add all resource claims for full department access
				AddAllResourceClaims(deptIdentity);
				AddSystemRecordViewClaim(deptIdentity, department.DepartmentId);

				var deptPrincipal = new ClaimsPrincipal(deptIdentity);

				deptPrincipal.SetScopes(new[]
				{
					Scopes.OpenId,
					Scopes.Email,
					Scopes.Profile
				}.Intersect(request.GetScopes()));

				if (request.GetScopes() != null && request.GetScopes().Contains("mobile"))
				{
					deptPrincipal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
				}
				else
				{
					deptPrincipal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
				}

				return SignIn(deptPrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
			}

			else if (request != null && string.Equals(request.GrantType, MfaLoginTransactions.CompletionGrantType, StringComparison.Ordinal))
			{
				return await RedeemLoginTransactionAsync(request);
			}

			throw new NotImplementedException("The specified grant type is not implemented.");
		}

		/// <summary>
		/// Starts the restricted login transaction after a verified password (workbook section 7.1): no tokens, only the
		/// transaction secret and the methods it accepts. Null when it could not start, so the caller falls back to the
		/// legacy <c>totp_code</c> response, which still requires the second factor.
		/// </summary>
		private async Task<IActionResult> BeginLoginTransactionAsync(Model.Identity.IdentityUser user, int departmentId, OpenIddictRequest request,
			SystemAudit audit, bool totpEnrolled = true)
		{
			MfaLoginTransactionStart start;
			try
			{
				start = await _loginTransactions.BeginAsync(new MfaLoginTransactionRequest
				{
					UserId = user.Id,
					DepartmentId = departmentId,
					ClientApplication = ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]),
					ClientId = request.ClientId,
					FirstFactorMethod = MfaEvidenceMethod.Password,
					FirstFactorVerifiedOnUtc = DateTime.UtcNow,
					AuthenticationGeneration = user.AuthenticationGeneration,
					Scopes = GrantableScopes(request.GetScopes()),
					SharedModeRequested = SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]),
					InstallationLabel = Request.Headers["X-Resgrid-Device-Name"],
					TotpEnrolled = totpEnrolled
				}, CancellationToken.None);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex, "A login MFA transaction could not be started; the legacy TOTP response was used.");
				return null;
			}

			audit.Successful = false;
			audit.Data += " (mfa_required, transaction)";
			await _systemAuditsService.SaveSystemAuditAsync(audit);

			var properties = new AuthenticationProperties(new Dictionary<string, string>
			{
				[OpenIddictServerAspNetCoreConstants.Properties.Error] = "mfa_required",
				[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Additional verification is required."
			});
			properties.Parameters["mfa_transaction"] = start.Secret;
			properties.Parameters["mfa_methods"] = string.Join(" ", start.Choice.AllowedMethods);
			properties.Parameters["mfa_enrolled"] = string.Join(" ", start.Choice.EnrolledMethods);
			if (start.Choice.Preferred != null)
				properties.Parameters["mfa_preferred"] = start.Choice.Preferred;
			properties.Parameters["mfa_expires_in"] = (long)start.ExpiresInSeconds;
			return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
		}

		/// <summary>
		/// A setup transaction (plan section 6.2): a login transaction for an account with no factor, which can only set up an
		/// authenticator and then complete with it. Null when it could not start; the enrollment error stands on its own.
		/// </summary>
		private async Task<MfaLoginTransactionStart> BeginSetupTransactionAsync(Model.Identity.IdentityUser user, int departmentId, OpenIddictRequest request)
		{
			try
			{
				return await _loginTransactions.BeginAsync(new MfaLoginTransactionRequest
				{
					UserId = user.Id,
					DepartmentId = departmentId,
					ClientApplication = ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]),
					ClientId = request.ClientId,
					FirstFactorMethod = MfaEvidenceMethod.Password,
					FirstFactorVerifiedOnUtc = DateTime.UtcNow,
					AuthenticationGeneration = user.AuthenticationGeneration,
					Scopes = GrantableScopes(request.GetScopes()),
					SharedModeRequested = SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]),
					InstallationLabel = Request.Headers["X-Resgrid-Device-Name"],
					TotpEnrolled = false
				}, CancellationToken.None);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex, "An MFA setup transaction could not be started.");
				return null;
			}
		}

		/// <summary>
		/// Redeems a login transaction's one-use completion code for the normal token response (workbook section 7.1). The
		/// account, department policy and first-factor rules are rechecked; the session records the original first-factor
		/// time and the second factor actually verified. A lost response means signing in again: nothing is issued twice.
		/// </summary>
		private async Task<IActionResult> RedeemLoginTransactionAsync(OpenIddictRequest request)
		{
			var audit = new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.Login,
				Successful = false,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"V4 Token (MFA completion), {Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			};

			var redeemed = await _loginTransactions.RedeemAsync((string)request.GetParameter("transaction"),
				(string)request.GetParameter("completion_code"), ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]),
				CancellationToken.None);
			if (!redeemed.IsUsable)
			{
				audit.Data += $" ({MfaLoginTransactions.ErrorCode(redeemed.Outcome)})";
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return LoginTransactionError(redeemed.Outcome);
			}

			var transaction = redeemed.Transaction;
			var user = await _userManager.FindByIdAsync(transaction.UserId);
			audit.UserId = transaction.UserId;
			audit.Username = user?.UserName;
			if (user == null || !await _signInManager.CanSignInAsync(user) || await _userManager.IsLockedOutAsync(user))
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return InvalidGrant("The username or password is invalid.");
			}

			// What allowed the first factor must still hold: an active membership, and password sign-in still permitted, or
			// the department's SSO configuration still the one that authenticated the user.
			var authenticationMethod = UserSessionAuthenticationMethod.LocalPassword;
			if (transaction.DepartmentId is int departmentId)
			{
				var membership = await _departmentsService.GetDepartmentMemberAsync(user.Id, departmentId, bypassCache: true);
				bool firstFactorStillAllowed;
				if (transaction.FirstFactorMethod == (int)MfaEvidenceMethod.Password)
				{
					firstFactorStillAllowed = await IsLocalLoginAllowedAsync(user.Id, departmentId);
				}
				else
				{
					authenticationMethod = await SsoAuthenticationMethodAsync(departmentId, transaction.DepartmentSsoConfigId);
					firstFactorStillAllowed = authenticationMethod != UserSessionAuthenticationMethod.LocalPassword;
				}

				if (membership == null || membership.IsDeleted || membership.IsDisabled == true || !firstFactorStillAllowed)
				{
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return InvalidGrant("The username or password is invalid.");
				}
			}
			else if (transaction.FirstFactorMethod != (int)MfaEvidenceMethod.Password)
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return InvalidGrant("The username or password is invalid.");
			}

			var principal = await _signInManager.CreateUserPrincipalAsync(user);
			var granted = (transaction.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var requested = request.GetScopes();
			principal.SetScopes(requested.IsDefaultOrEmpty ? granted : granted.Intersect(requested));

			// A brokered SSO login for an account without MFA completes with no second factor, and records none.
			var method = transaction.CompletionMethod == null ? (MfaEvidenceMethod?)null : (MfaEvidenceMethod)transaction.CompletionMethod.Value;
			var refreshTokenLifetime = GetRefreshTokenLifetime(transaction.ClientId);
			if (SessionSecurityConfig.TrackingEnabled)
			{
				try
				{
					var session = await CreateApiSessionAsync(user, transaction.DepartmentId, authenticationMethod,
						refreshTokenLifetime, CancellationToken.None, transaction.DepartmentSsoConfigId,
						method, transaction.CompletionFactorReference);
					AddSessionClaims(principal, session);
					await RecordLoginEvidenceAsync(user, session, (MfaEvidenceMethod)transaction.FirstFactorMethod,
						method == null ? null : transaction.CompletionVerifiedOnUtc, CancellationToken.None, transaction.FirstFactorVerifiedOnUtc,
						method ?? MfaEvidenceMethod.Totp, transaction.CompletionFactorReference);
				}
				catch (SessionCreationDeniedException ex)
				{
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return InvalidGrant(ex.FailureCode == "maximum_sessions"
						? "The department's maximum number of active sessions has been reached."
						: "The user is no longer allowed to sign in to this department.");
				}
			}

			foreach (var claim in principal.Claims)
				claim.SetDestinations(GetDestinations(claim, principal));

			principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
			principal.SetRefreshTokenLifetime(refreshTokenLifetime);
			principal.SetResources(JwtConfig.EventsClientId);

			audit.Successful = true;
			audit.Data += method switch
			{
				null => " (SSO, no second factor required)",
				MfaEvidenceMethod.RecoveryCode => " (recovery code)",
				_ => $" ({MfaMethodNames.From(method.Value)})"
			};
			await _systemAuditsService.SaveSystemAuditAsync(audit);

			return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
		}

		/// <summary>
		/// The session's first-factor method for an SSO login, from the department configuration that authenticated it;
		/// <see cref="UserSessionAuthenticationMethod.LocalPassword"/> when that configuration is gone or disabled.
		/// </summary>
		private async Task<UserSessionAuthenticationMethod> SsoAuthenticationMethodAsync(int departmentId, string departmentSsoConfigId)
		{
			var config = (await _departmentSsoService.GetSsoConfigsForDepartmentAsync(departmentId, CancellationToken.None))?
				.FirstOrDefault(c => c.IsEnabled && string.Equals(c.DepartmentSsoConfigId, departmentSsoConfigId, StringComparison.Ordinal));
			return (SsoProviderType?)config?.SsoProviderType switch
			{
				SsoProviderType.Oidc => UserSessionAuthenticationMethod.OidcSso,
				SsoProviderType.Saml2 => UserSessionAuthenticationMethod.SamlSso,
				_ => UserSessionAuthenticationMethod.LocalPassword
			};
		}

		private IActionResult LoginTransactionError(MfaLoginTransactionOutcome outcome) =>
			Forbid(new AuthenticationProperties(new Dictionary<string, string>
			{
				[OpenIddictServerAspNetCoreConstants.Properties.Error] = MfaLoginTransactions.ErrorCode(outcome) ?? Errors.InvalidGrant,
				[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = outcome switch
				{
					MfaLoginTransactionOutcome.Expired => "The sign-in took too long. Sign in again.",
					MfaLoginTransactionOutcome.PolicyChanged => "Your department's sign-in policy changed. Sign in again.",
					MfaLoginTransactionOutcome.SessionRevoked => "Your account's sign-in state changed. Sign in again.",
					MfaLoginTransactionOutcome.Unavailable => "Sign-in is temporarily unavailable. Try again.",
					_ => "The sign-in could not be completed. Sign in again."
				}
			}), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

		/// <summary>The scopes a password sign-in grants, limited to what the client asked for.</summary>
		private static IReadOnlyCollection<string> GrantableScopes(IEnumerable<string> requested) =>
			new[] { Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, Scopes.Roles }.Intersect(requested ?? Array.Empty<string>()).ToList();

		/// <summary>
		/// Whether a password may sign this user in to the department: no SSO-only account link, and the department does
		/// not require SSO. Checked at the first factor and again when a login transaction is redeemed.
		/// </summary>
		private async Task<bool> IsLocalLoginAllowedAsync(string userId, int departmentId)
		{
			if (!await _externalIdentityLinkService.IsLocalLoginAllowedAsync(userId, CancellationToken.None))
				return false;
			if (!await _externalIdentityLinkService.IsLocalLoginAllowedAsync(userId, departmentId, CancellationToken.None))
				return false;

			return !(await _departmentSsoService.IsRequireSsoPolicyActiveAsync(departmentId, CancellationToken.None) &&
				await _departmentSsoService.IsSsoEnabledForDepartmentAsync(departmentId, CancellationToken.None));
		}

		/// <summary>
		/// The discovery fields every client needs to continue without a department code (plan section 7.7.4): the
		/// system-encrypted department token, the department id, and whether brokered SSO can run for this department.
		/// </summary>
		private void AddDepartmentIdentity(Resgrid.Web.Services.Models.v4.Sso.GetDepartmentSsoConfigResultData data, Model.Department department,
			DepartmentSsoConfig activeConfig)
		{
			data.DepartmentId = department.DepartmentId;
			data.DepartmentToken = _ssoBroker.DepartmentTokenFor(department);
			data.BrokeredSsoAvailable = activeConfig != null && _ssoBroker.IsEnabled && _ssoBroker.SupportsBrokered(activeConfig);
		}

		private IActionResult InvalidGrant(string description)
		{
			var properties = new AuthenticationProperties(new Dictionary<string, string>
			{
				[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
				[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
			});
			return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
		}

		/// <summary>
		/// Returns the SSO configuration for a department so the mobile app can determine
		/// whether to show the SSO login button, which flow to use (OIDC/SAML), and which
		/// parameters to pass. Call this before showing the login screen.
		/// </summary>
		/// <param name="departmentToken">An encrypted department token produced by the web UI.</param>
		/// <param name="departmentCode">The department code (name) shown on the department's settings page.</param>
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
				AddDepartmentIdentity(result.Data, department, null);
				return Ok(result);
			}

			var providerType = (SsoProviderType)activeConfig.SsoProviderType;

			result.Data.SsoEnabled = true;
			result.Data.ProviderType = providerType.ToString().ToLowerInvariant();
			result.Data.AllowLocalLogin = activeConfig.AllowLocalLogin;
			result.Data.RequireSso = policy?.RequireSso ?? false;
			result.Data.RequireMfa = policy?.RequireMfa ?? false;
			AddDepartmentIdentity(result.Data, department, activeConfig);

			if (providerType == SsoProviderType.Oidc)
			{
				result.Data.Authority = activeConfig.Authority;
				result.Data.ClientId = activeConfig.ClientId; // public client ID — safe to expose
				result.Data.OidcRedirectUri = LegacyOidcRedirectUri();
				result.Data.OidcScopes = "openid email profile offline_access";
			}
			else if (providerType == SsoProviderType.Saml2)
			{
				result.Data.MetadataUrl = activeConfig.MetadataUrl;
				result.Data.SamlLoginUrl = LegacySamlLoginUrl(department, activeConfig);
				result.Data.EntityId = activeConfig.EntityId;
			}

			return Ok(result);
		}

		/// <summary>
		/// Returns the SSO configuration for a user's department, resolved by username.
		/// Intended for the mobile app pre-login screen — call this with just a username
		/// to discover whether SSO is required before the user enters credentials.
		/// If <paramref name="departmentId"/> is supplied the lookup is scoped to that
		/// specific department; otherwise the user's default/active department is used.
		/// No credentials are required — this endpoint is public (anonymous).
		/// </summary>
		/// <param name="username">The user's Resgrid username (or email address).</param>
		/// <param name="departmentId">
		/// Optional. When the user belongs to multiple departments, pass this to select
		/// the specific department whose SSO configuration should be returned.
		/// </param>
		/// <param name="cancellationToken"></param>
		[HttpGet("sso-config-for-user")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Produces("application/json")]
		public async Task<ActionResult<GetDepartmentSsoConfigResult>> GetSsoConfigForUser(
			[FromQuery] string username,
			[FromQuery] int? departmentId,
			CancellationToken cancellationToken)
		{
			var result = new GetDepartmentSsoConfigResult();
			ResponseHelper.PopulateV4ResponseData(result);

			if (string.IsNullOrWhiteSpace(username))
			{
				result.Status = ResponseHelper.Failure;
				result.PageSize = 0;
				return BadRequest(result);
			}

			// Resolve user by username (also supports email as username)
			var user = await _userManager.FindByNameAsync(username)
				?? await _userManager.FindByEmailAsync(username);

			if (user == null)
			{
				// Return a "no SSO" response without leaking whether the account exists
				result.Status = ResponseHelper.Success;
				result.PageSize = 1;
				result.Data.SsoEnabled = false;
				result.Data.AllowLocalLogin = true;
				return Ok(result);
			}

			Model.Department department;

			if (departmentId.HasValue)
			{
				// Caller specified a department — verify the user is actually a member
				var allMemberships = await _departmentsService.GetAllDepartmentsForUserAsync(user.Id);
				var membership = allMemberships?.FirstOrDefault(m => m.DepartmentId == departmentId.Value);

				department = membership != null
					? await _departmentsService.GetDepartmentByIdAsync(departmentId.Value)
					: null;
			}
			else
			{
				// No department hint — use the user's default/active department
				department = await _departmentsService.GetDepartmentByUserIdAsync(user.Id);
			}

			if (department == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			// Load SSO configs and security policy — reuse same logic as GetSsoConfig
			var ssoConfigs = await _departmentSsoService.GetSsoConfigsForDepartmentAsync(department.DepartmentId, cancellationToken);
			var activeConfig = ssoConfigs?.FirstOrDefault(c => c.IsEnabled);
			var policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(department.DepartmentId, cancellationToken);

			result.Status = ResponseHelper.Success;
			result.PageSize = 1;

			if (activeConfig == null)
			{
				result.Data.SsoEnabled = false;
				result.Data.AllowLocalLogin = true;
				result.Data.RequireSso = false;
				result.Data.RequireMfa = policy?.RequireMfa ?? false;
				AddDepartmentIdentity(result.Data, department, null);
				return Ok(result);
			}

			var providerType = (SsoProviderType)activeConfig.SsoProviderType;

			result.Data.SsoEnabled = true;
			result.Data.ProviderType = providerType.ToString().ToLowerInvariant();
			result.Data.AllowLocalLogin = activeConfig.AllowLocalLogin;
			result.Data.RequireSso = policy?.RequireSso ?? false;
			result.Data.RequireMfa = policy?.RequireMfa ?? false;
			AddDepartmentIdentity(result.Data, department, activeConfig);

			if (providerType == SsoProviderType.Oidc)
			{
				result.Data.Authority = activeConfig.Authority;
				result.Data.ClientId = activeConfig.ClientId;
				result.Data.OidcRedirectUri = LegacyOidcRedirectUri();
				result.Data.OidcScopes = "openid email profile offline_access";
			}
			else if (providerType == SsoProviderType.Saml2)
			{
				result.Data.MetadataUrl = activeConfig.MetadataUrl;
				result.Data.SamlLoginUrl = LegacySamlLoginUrl(department, activeConfig);
				result.Data.EntityId = activeConfig.EntityId;
			}

			return Ok(result);
		}

		/// <summary>
		/// Exchanges an external SSO token (OIDC id_token or base64-encoded SAMLResponse) for a
		/// Resgrid access token. Supports grant_type=external_token with fields:
		///   provider (saml2|oidc), external_token, department_code or department_token, scope (optional),
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
			[FromForm] string department_token,
			[FromForm] string scope,
			[FromForm] string totp_code,
			CancellationToken cancellationToken)
		{
			var audit = new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.SsoLogin,
				Username = department_code ?? "encrypted-department-token",
				Successful = false,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"ExternalToken provider={provider}, {Request.Headers["User-Agent"]}"
			};

			if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(external_token) ||
				(string.IsNullOrWhiteSpace(department_code) && string.IsNullOrWhiteSpace(department_token)))
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return BadRequest(new { error = "invalid_request", error_description = "provider, external_token, and either department_code or department_token are required." });
			}

			var department = await ResolveDepartmentAsync(department_token, department_code);
			if (department == null)
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return Unauthorized(new { error = "invalid_grant", error_description = "Invalid department identifier." });
			}

			// Parse provider type
			if (!Enum.TryParse<SsoProviderType>(provider, ignoreCase: true, out var providerType) || !Enum.IsDefined(providerType))
			{
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return BadRequest(new { error = "invalid_request", error_description = "provider must be 'saml2' or 'oidc'." });
			}

			// A relayed SAML sign-in spends its relay token and assertion on the first exchange. When that exchange stopped at the
			// member's Resgrid code, the retry with the same relay token continues from the identity it validated (the app resends
			// the exchange with the code, as it does for OIDC); otherwise a spent relay token signs nobody in.
			string samlRelayId = null;
			SamlMfaContinuation continuation = null;
			if (providerType == SsoProviderType.Saml2 && external_token.StartsWith(SamlRelayTokenPrefix, StringComparison.Ordinal))
			{
				samlRelayId = SamlRelayId(external_token);
				external_token = await ConsumeSamlRelayAsync(external_token);
				if (string.IsNullOrWhiteSpace(external_token))
					continuation = await ReadSamlMfaContinuationAsync(samlRelayId, department.DepartmentId);
				if (string.IsNullOrWhiteSpace(external_token) && continuation == null)
				{
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new { error = "invalid_grant", error_description = "The SAML relay token is invalid, expired, or has already been used." });
				}
			}

			DepartmentSsoConfig ssoConfig;
			Model.Identity.IdentityUser user;
			if (continuation != null)
			{
				// Nothing that allowed the first exchange may have changed: the account, its generation, the membership, the SSO
				// configuration, and the Resgrid factor the retry is for.
				ssoConfig = await _departmentSsoService.GetSsoConfigForDepartmentAsync(department.DepartmentId, providerType, cancellationToken);
				user = await _userManager.FindByIdAsync(continuation.UserId);
				var member = user == null ? null : await _departmentsService.GetDepartmentMemberAsync(user.Id, department.DepartmentId, bypassCache: true);
				if (user == null || user.AuthenticationGeneration != continuation.AuthenticationGeneration || ssoConfig?.IsEnabled != true ||
					ssoConfig.DepartmentSsoConfigId != continuation.DepartmentSsoConfigId || member == null || member.IsDeleted || member.IsDisabled == true ||
					!await _userManager.GetTwoFactorEnabledAsync(user))
				{
					await ForgetSamlMfaContinuationAsync(samlRelayId);
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new { error = "invalid_grant", error_description = "The sign-in can no longer be finished. Sign in again." });
				}
			}
			else
			{
				// Validate the external token against the department's SSO config
				var externalPrincipal = await _departmentSsoService.ValidateExternalTokenAsync(
					department.DepartmentId, providerType, external_token, department.Code, cancellationToken);

				if (externalPrincipal == null)
				{
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new { error = "invalid_grant", error_description = "The external token could not be validated." });
				}

				// A shared installation's sign-in must be a fresh provider sign-in: an older one may be the last operator's
				// provider session, still in the installation's browser (plan section 12.5.2). The app asks the provider for one
				// (OIDC prompt=login with max_age=0, SAML ForceAuthn); this refuses a provider that answered from its session.
				if (await SharedInstallationAsync(department.DepartmentId, cancellationToken) && !ProviderSignInTime.IsFresh(
						ProviderSignInTime.Read(externalPrincipal), DateTime.UtcNow, SharedSignInMaxAge, ProviderClockSkew))
				{
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new
					{
						error = "login_required",
						error_description = "This is a shared installation: sign in at your identity provider again, not with a sign-in it remembered."
					});
				}

				// Get the SSO config to pass to provisioning
				ssoConfig = await _departmentSsoService.GetSsoConfigForDepartmentAsync(department.DepartmentId, providerType, cancellationToken);

				// Provision or link the user
				user = await _departmentSsoService.ProvisionOrLinkUserAsync(
					department.DepartmentId, externalPrincipal, ssoConfig, department.Code, cancellationToken);

				if (user == null)
				{
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new { error = "invalid_grant", error_description = "No matching user found and auto-provisioning is disabled." });
				}
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
					if (samlRelayId != null && continuation == null)
						await KeepSamlMfaContinuationAsync(samlRelayId, user, department, ssoConfig);
					audit.UserId = user.Id;
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new
					{
						error = "mfa_required",
						error_description = "Your Resgrid account has Two-Factor Authentication enabled. Please include your current totp_code with this request."
					});
				}

				// The SSO path shares the account lockout with every other TOTP surface (plan section 7.5 rule 6): a
				// caller holding a valid IdP token does not get unlimited code guesses.
				var totpValid = !await _userManager.IsLockedOutAsync(user) && await _userManager.VerifyTwoFactorTokenAsync(
					user,
					_userManager.Options.Tokens.AuthenticatorTokenProvider,
					totp_code);

				if (!totpValid)
				{
					await _userManager.AccessFailedAsync(user);
					if (samlRelayId != null && continuation == null)
						await KeepSamlMfaContinuationAsync(samlRelayId, user, department, ssoConfig);
					if (samlRelayId != null)
						await CountSamlMfaFailureAsync(samlRelayId);
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
			else if (await _mfaPolicyService.DepartmentRequiresMfaAsync(department.DepartmentId, cancellationToken))
			{
				// RequireMfa has always been enforced here; say specifically that enrollment is what is missing.
				audit.UserId = user.Id;
				audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return Unauthorized(new
				{
					error = "mfa_enrollment_required",
					error_description = MfaEnrollmentRequiredDescription,
					error_uri = MfaEnrollmentUri
				});
			}
			var mfaCompletedOnUtc = mfaCompleted ? DateTime.UtcNow : (DateTime?)null;

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

			// A continued SAML sign-in finishes once, however many retries race for it.
			if (continuation != null && !await ClaimSamlMfaContinuationAsync(samlRelayId))
			{
				audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
				audit.UserId = user.Id;
				await _systemAuditsService.SaveSystemAuditAsync(audit);
				return Unauthorized(new { error = "invalid_grant", error_description = "The SAML relay token is invalid, expired, or has already been used." });
			}

			// An id_token signs in once (plan section 7.7.2 item 8). It is recorded only now, at a successful completion,
			// so an older build's OIDC + TOTP resend after mfa_required keeps working (section 7.7.4).
			if (providerType == SsoProviderType.Oidc)
			{
				bool firstUse;
				try
				{
					firstUse = await _ssoBroker.TryRecordIdTokenUseAsync(external_token,
						new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(external_token).ValidTo, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Framework.Logging.LogException(ex, "The id_token replay record could not be written; the sign-in was refused.");
					return StatusCode(StatusCodes.Status503ServiceUnavailable,
						new { error = "temporarily_unavailable", error_description = "Sign-in is temporarily unavailable. Try again." });
				}

				if (!firstUse)
				{
					audit.Type = (int)SystemAuditTypes.SsoLoginFailed;
					audit.UserId = user.Id;
					await _systemAuditsService.SaveSystemAuditAsync(audit);
					return Unauthorized(new { error = "invalid_grant", error_description = "The external token has already been used. Sign in again." });
				}
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

			var refreshTokenLifetime = GetRefreshTokenLifetime((string)null);
			if (SessionSecurityConfig.TrackingEnabled)
			{
				try
				{
					var session = await CreateApiSessionAsync(user, department.DepartmentId,
						providerType == SsoProviderType.Oidc ? UserSessionAuthenticationMethod.OidcSso : UserSessionAuthenticationMethod.SamlSso,
						refreshTokenLifetime, cancellationToken, ssoConfig?.DepartmentSsoConfigId,
						mfaCompleted ? MfaEvidenceMethod.Totp : null);
					AddSessionClaims(principal, session);
					await RecordLoginEvidenceAsync(user, session, MfaEvidenceMethod.Sso, mfaCompletedOnUtc, cancellationToken);
				}
				catch (SessionCreationDeniedException ex)
				{
					return Unauthorized(new
					{
						error = ex.FailureCode,
						error_description = ex.FailureCode == "maximum_sessions"
							? "The department's maximum number of active sessions has been reached."
							: "The user is no longer allowed to sign in to this department."
					});
				}
			}

			foreach (var claim in principal.Claims)
				claim.SetDestinations(GetDestinations(claim, principal));

			principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
			principal.SetRefreshTokenLifetime(refreshTokenLifetime);

			principal.SetResources(JwtConfig.EventsClientId);

			audit.Successful = true;
			audit.UserId = user.Id;
			await _systemAuditsService.SaveSystemAuditAsync(audit);

			return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
		}

		/// <summary>
		/// SAML 2.0 Assertion Consumer Service (ACS) relay for mobile apps.
		/// Receives the SAMLResponse POST from the IdP, then redirects to the app that started the sign-in
		/// (named in its RelayState, see <see cref="LegacySamlRelay"/>) so it can complete authentication
		/// via the external-token endpoint. An untagged RelayState returns to Responder, as before.
		/// Configure your IdP's ACS URL to point here:
		///   POST /api/v4/connect/saml-mobile-callback?departmentCode=DEPT
		/// </summary>
		[HttpPost("saml-mobile-callback")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status302Found)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
		public async Task<IActionResult> SamlMobileCallback(
			[FromQuery] string departmentToken,
			[FromQuery] string departmentCode,
			[FromForm] string SAMLResponse,
			[FromForm] string RelayState,
			CancellationToken cancellationToken)
		{
			// A response to a brokered AuthnRequest carries our RelayState; it is validated against that transaction (with
			// InResponseTo) and never relayed. Everything else keeps the legacy relay for older app builds.
			if (_ssoBroker.IsBrokeredRelayState(RelayState))
				return SsoCallbackResponse(await _ssoBroker.CompleteSamlCallbackAsync(RelayState, SAMLResponse,
					IpAddressHelper.GetRequestIP(Request, true), cancellationToken));

			if (string.IsNullOrWhiteSpace(SAMLResponse) || SAMLResponse.Length > 2_800_000)
				return BadRequest(new { error = "invalid_request", error_description = "SAMLResponse is required and must be within the supported size limit." });

			if (string.IsNullOrWhiteSpace(departmentToken) && string.IsNullOrWhiteSpace(departmentCode))
				return BadRequest(new { error = "invalid_request", error_description = "departmentToken or departmentCode query parameter is required." });

			var department = await ResolveDepartmentAsync(departmentToken, departmentCode);
			if (department == null)
				return BadRequest(new { error = "invalid_request", error_description = "Unknown or invalid department token." });

			// Keep the assertion out of the custom-scheme URL. Store it encrypted for five minutes
			// and hand the app a single-use, cryptographically random relay value instead.
			var relayId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
			var stored = await _cacheProvider.SetStringAsync(
				GetSamlRelayCacheKey(relayId), _encryptionService.Encrypt(SAMLResponse), SamlRelayLifetime);
			if (!stored)
				return StatusCode(StatusCodes.Status503ServiceUnavailable,
					new { error = "temporarily_unavailable", error_description = "SAML login relay is temporarily unavailable." });

			var callbackToken = _encryptionService.Encrypt($"{department.DepartmentId}:{department.Code}");

			// Back to the app that started the sign-in, with its RelayState echoed so it can reject a sign-in it did not
			// start. The link carries a single-use relay token, so no cache may keep it.
			var appReturn = LegacySamlRelay.For(RelayState);
			Response.Headers.CacheControl = "no-store";
			Response.Headers.Pragma = "no-cache";
			return Redirect(LegacySamlRelay.DeepLink(appReturn, $"{SamlRelayTokenPrefix}{relayId}", callbackToken));
		}

		/// <summary>
		/// The IdP's OIDC redirect for brokered SSO (passkey plan section 7.7.2 step 2). Departments register
		/// <c>{api}/api/v4/connect/oidc-callback</c> with their IdP. The server exchanges the code and validates the id_token
		/// (state, nonce, PKCE), then sends the browser to the client's registered return target with a one-time
		/// <c>sso_code</c>. No token or assertion is ever in the URL.
		/// </summary>
		[HttpGet("oidc-callback")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status302Found)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<IActionResult> OidcCallback([FromQuery] string state, [FromQuery] string code, [FromQuery] string error,
			CancellationToken cancellationToken) =>
			SsoCallbackResponse(await _ssoBroker.CompleteOidcCallbackAsync(state, code, error, IpAddressHelper.GetRequestIP(Request, true), cancellationToken));

		/// <summary>
		/// Redirects to the registered return target when the transaction is known; otherwise there is nowhere trusted to
		/// send the browser, so the callback answers with the error itself.
		/// </summary>
		private IActionResult SsoCallbackResponse(SsoCallbackResult result)
		{
			Response.Headers.CacheControl = "no-store";
			Response.Headers.Pragma = "no-cache";
			if (result.RedirectUrl != null)
				return Redirect(result.RedirectUrl);

			return StatusCode(result.Outcome == SsoBrokerOutcome.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest,
				new { error = SsoBrokerOutcomes.ErrorCode(result.Outcome) ?? "sso_failed", error_description = "The sign-in could not be completed. Start again from the app." });
		}

		/// <summary>
		/// Starts a legacy (unbrokered) SAML sign-in for an app (workbook section 12, slice 35). An app cannot build a SAML
		/// AuthnRequest, so it opens this page, which sends the browser to the department's IdP with one. The RelayState must be
		/// the app's own tagged value (<c>unit.&lt;nonce&gt;</c>): the IdP returns it with the response, and the relay
		/// (<see cref="SamlMobileCallback"/>) sends the member back to that app and echoes it. Anything else is refused, so
		/// this page never names another destination. Nothing is stored here.
		/// </summary>
		[HttpGet("saml-mobile-login")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status302Found)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<IActionResult> SamlMobileLogin([FromQuery] string departmentToken, [FromQuery] string departmentCode, [FromQuery] string relayState,
			CancellationToken cancellationToken, [FromQuery] bool forceAuthn = false)
		{
			Response.Headers.CacheControl = "no-store";
			Response.Headers.Pragma = "no-cache";
			if (LegacySamlRelay.For(relayState).EchoedRelayState == null)
				return BadRequest(new { error = "invalid_request", error_description = "Start the sign-in from the app." });

			var department = await ResolveDepartmentAsync(departmentToken, departmentCode);
			var config = department == null
				? null
				: await _departmentSsoService.GetSsoConfigForDepartmentAsync(department.DepartmentId, SsoProviderType.Saml2, cancellationToken);
			var signIn = config?.IsEnabled == true ? _ssoBroker.LegacySamlSignInUrl(config, department.Code, relayState, forceAuthn) : null;
			if (signIn == null)
				return BadRequest(new { error = "sso_unavailable", error_description = "Single sign-on is not available for this department." });

			return Redirect(signIn);
		}

		/// <summary>How long ago the provider may have authenticated a shared installation's member (the reauthentication window).</summary>
		private static TimeSpan SharedSignInMaxAge => TimeSpan.FromSeconds(Math.Max(30, SsoConfig.ReauthenticationMaxAgeSeconds));

		private static readonly TimeSpan ProviderClockSkew = TimeSpan.FromMinutes(2);

		/// <summary>
		/// A sign-in from a shared installation: it says so, or the department makes this app's sessions shared (the broker's
		/// rule for its own round trips).
		/// </summary>
		private async Task<bool> SharedInstallationAsync(int departmentId, CancellationToken cancellationToken) =>
			SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]) ||
			SharedSessionRules.IsRequiredFor(await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(departmentId, cancellationToken),
				ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]));

		/// <summary>
		/// This server's legacy SAML start page for the department, when its configuration can start a sign-in. Discovery calls
		/// it only for a SAML configuration.
		/// </summary>
		private string LegacySamlLoginUrl(Model.Department department, DepartmentSsoConfig config) =>
			config != null && _ssoBroker.SupportsBrokered(config)
				? $"{SystemBehaviorConfig.ResgridApiBaseUrl?.TrimEnd('/')}{SsoConfig.SamlLoginPath}?departmentToken={Uri.EscapeDataString(_ssoBroker.DepartmentTokenFor(department))}"
				: null;

		/// <summary>
		/// The legacy OIDC redirect URI for the app asking (<c>X-Resgrid-Client</c>): each app has its own scheme, and the
		/// department's IdP must list each one. A caller that names no app gets Responder's, as every caller did before.
		/// </summary>
		private string LegacyOidcRedirectUri() =>
			LegacyAppCallbacks.For(ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header])).Callback;

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
					var plain = _encryptionService.Decrypt(departmentToken);
					var parts = plain.Split(':');
					if (parts.Length >= 2 && int.TryParse(parts[0], out var deptId))
					{
						var deptCode = string.Join(":", parts.Skip(1));
						var department = await _departmentsService.GetDepartmentByIdAsync(deptId);
						if (department != null && string.Equals(department.Code, deptCode, StringComparison.Ordinal))
							return department;
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

		/// <summary>The 64 hex digits of a relay token, or null when it is not one.</summary>
		private static string SamlRelayId(string relayToken) =>
			relayToken?.Length == SamlRelayTokenPrefix.Length + 64 && relayToken.StartsWith(SamlRelayTokenPrefix, StringComparison.Ordinal) &&
			relayToken[SamlRelayTokenPrefix.Length..].All(Uri.IsHexDigit)
				? relayToken[SamlRelayTokenPrefix.Length..]
				: null;

		/// <summary>What a relayed SAML exchange that stopped at the member's Resgrid code validated, for the code retry.</summary>
		private sealed record SamlMfaContinuation(string UserId, int DepartmentId, string DepartmentSsoConfigId, long AuthenticationGeneration);

		/// <summary>
		/// Keeps what the first exchange validated under its relay token, encrypted, for the relay's own lifetime (not sliding),
		/// so the member's code retry can finish the sign-in the spent assertion can no longer start again.
		/// </summary>
		private async Task KeepSamlMfaContinuationAsync(string relayId, Model.Identity.IdentityUser user, Model.Department department, DepartmentSsoConfig config)
		{
			try
			{
				var value = string.Join("|", user.Id, department.DepartmentId.ToString(CultureInfo.InvariantCulture), config?.DepartmentSsoConfigId ?? "",
					user.AuthenticationGeneration.ToString(CultureInfo.InvariantCulture));
				await _cacheProvider.SetStringAsync(GetSamlRelayMfaCacheKey(relayId), _encryptionService.Encrypt(value), SamlRelayLifetime);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Without it the retry is refused as a spent relay token and the member starts again, as before.
				Logging.LogException(ex, "The SAML code-retry state could not be kept.");
			}
		}

		private async Task<SamlMfaContinuation> ReadSamlMfaContinuationAsync(string relayId, int departmentId)
		{
			if (relayId == null)
				return null;

			try
			{
				var stored = await _cacheProvider.GetStringAsync(GetSamlRelayMfaCacheKey(relayId));
				if (string.IsNullOrWhiteSpace(stored))
					return null;

				var parts = _encryptionService.Decrypt(stored).Split('|');
				return parts.Length == 4 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var storedDepartment) &&
					storedDepartment == departmentId && long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation)
					? new SamlMfaContinuation(parts[0], storedDepartment, parts[2], generation)
					: null;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "The SAML code-retry state could not be read.");
				return null;
			}
		}

		/// <summary>A wrong code on a relayed SAML sign-in; the retry state ends after the transaction attempt limit.</summary>
		private async Task CountSamlMfaFailureAsync(string relayId)
		{
			var failures = await _cacheProvider.IncrementAsync(GetSamlRelayMfaFailuresCacheKey(relayId), SamlRelayLifetime);
			if (failures == 0 || failures >= Math.Max(1, TwoFactorConfig.LoginMfaTransactionMaxAttempts))
				await ForgetSamlMfaContinuationAsync(relayId);
		}

		/// <summary>The one retry that finishes the sign-in: atomic, so racing retries cannot both sign in.</summary>
		private async Task<bool> ClaimSamlMfaContinuationAsync(string relayId)
		{
			var claimed = await _cacheProvider.IncrementAsync(GetSamlRelayMfaUseCacheKey(relayId), SamlRelayLifetime) == 1;
			await ForgetSamlMfaContinuationAsync(relayId);
			return claimed;
		}

		private async Task ForgetSamlMfaContinuationAsync(string relayId)
		{
			if (relayId != null)
				await _cacheProvider.RemoveAsync(GetSamlRelayMfaCacheKey(relayId));
		}

		private async Task<string> ConsumeSamlRelayAsync(string relayToken)
		{
			var relayId = SamlRelayId(relayToken);
			if (relayId == null)
				return null;

			// Increment is atomic in Redis. Only the first exchange is allowed to read the assertion,
			// including when callback and token requests land on different API instances.
			var useCount = await _cacheProvider.IncrementAsync(GetSamlRelayUseCacheKey(relayId), SamlRelayLifetime);
			if (useCount != 1)
				return null;

			var encryptedResponse = await _cacheProvider.GetStringAsync(GetSamlRelayCacheKey(relayId));
			await _cacheProvider.RemoveAsync(GetSamlRelayCacheKey(relayId));
			if (string.IsNullOrWhiteSpace(encryptedResponse))
				return null;

			try
			{
				return _encryptionService.Decrypt(encryptedResponse);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private static string GetSamlRelayCacheKey(string relayId) => $"Sso:SamlRelay:{relayId}";

		private static string GetSamlRelayUseCacheKey(string relayId) => $"Sso:SamlRelayUse:{relayId}";

		private static string GetSamlRelayMfaCacheKey(string relayId) => $"Sso:SamlRelayMfa:{relayId}";

		private static string GetSamlRelayMfaFailuresCacheKey(string relayId) => $"Sso:SamlRelayMfaFailures:{relayId}";

		private static string GetSamlRelayMfaUseCacheKey(string relayId) => $"Sso:SamlRelayMfaUse:{relayId}";

		private TimeSpan GetRefreshTokenLifetime(OpenIddictRequest request) => GetRefreshTokenLifetime(request?.ClientId);

		private TimeSpan GetRefreshTokenLifetime(string clientId)
		{
			var isTrustedLongLivedClient = !string.IsNullOrWhiteSpace(clientId) &&
				(OidcConfig.TrustedLongLivedClientIds ?? string.Empty)
					.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(value => value.Trim())
					.Any(value => string.Equals(value, clientId, StringComparison.Ordinal));

			return TimeSpan.FromDays(isTrustedLongLivedClient
				? OidcConfig.RefreshTokenExpiryDays
				: OidcConfig.NonMobileRefreshTokenExpiryDays);
		}

		private static DateTime? GetCredentialIssuedOn(ClaimsPrincipal principal)
		{
			var value = principal?.FindFirstValue(Claims.IssuedAt);
			if (long.TryParse(value, out var unixSeconds))
			{
				try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime; }
				catch (ArgumentOutOfRangeException) { return null; }
			}

			return DateTime.TryParse(value, CultureInfo.InvariantCulture,
				DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var timestamp)
				? timestamp
				: null;
		}

		private static DateTime? ParseUnixSeconds(string value)
		{
			if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
				return null;

			try { return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime; }
			catch (ArgumentOutOfRangeException) { return null; }
		}

		private async Task<UserSession> CreateApiSessionAsync(Model.Identity.IdentityUser user, int? departmentId,
			UserSessionAuthenticationMethod authenticationMethod, TimeSpan refreshTokenLifetime,
			CancellationToken cancellationToken, string departmentSsoConfigId = null, MfaEvidenceMethod? loginMfaMethod = null,
			string loginMfaFactorReference = null)
		{
			var session = await _userSessionService.CreateSessionAsync(new SessionIssueContext
			{
				UserId = user.Id,
				DepartmentId = departmentId,
				AuthenticationGeneration = user.AuthenticationGeneration,
				ClientApplication = ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]),
				SharedModeRequested = SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]),
				DeviceName = Request.Headers["X-Resgrid-Device-Name"],
				DeviceType = Request.Headers["X-Resgrid-Device-Type"],
				OperatingSystem = Request.Headers["X-Resgrid-Operating-System"],
				Browser = Request.Headers["X-Resgrid-Browser"],
				ApplicationVersion = Request.Headers["X-Resgrid-App-Version"],
				AuthenticationMethod = authenticationMethod,
				DepartmentSsoConfigId = departmentSsoConfigId,
				ExpiresOn = DateTime.UtcNow.Add(refreshTokenLifetime),
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				UserAgent = Request.Headers["User-Agent"],
				LoginMfaMethod = loginMfaMethod,
				LoginMfaFactorReference = loginMfaFactorReference
			}, cancellationToken);

			if (session?.SharedMode == true)
			{
				try
				{
					await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
					{
						System = (int)SystemAuditSystems.Api,
						Type = (int)SystemAuditTypes.SharedSessionStarted,
						DepartmentId = session.DepartmentId,
						UserId = user.Id,
						Username = user.UserName,
						TargetUserId = user.Id,
						SessionId = SharedSessionAudit.SessionSuffix(session.UserSessionId),
						Successful = true,
						IpAddress = IpAddressHelper.GetRequestIP(Request, true),
						ServerName = Environment.MachineName,
						CorrelationId = HttpContext.TraceIdentifier,
						Data = SharedSessionAudit.Describe("started", session,
							(SharedModeSource)session.SharedModeSource == SharedModeSource.DepartmentRequired ? "department required" : "installation requested"),
						LoggedOn = DateTime.UtcNow
					}, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Framework.Logging.LogException(ex, "Shared session start audit failed.");
				}
			}

			return session;
		}

		/// <summary>
		/// Records what this sign-in verified as server-side evidence for the new API session (passkey plan section 5.3), so
		/// a TOTP sign-in satisfies a following step-up window the same way it does on Web. A failure here never blocks the
		/// sign-in; the user is simply asked to verify again for a sensitive operation.
		/// </summary>
		private async Task RecordLoginEvidenceAsync(Model.Identity.IdentityUser user, UserSession session, MfaEvidenceMethod firstFactor,
			DateTime? secondFactorOnUtc, CancellationToken cancellationToken, DateTime? firstFactorOnUtc = null,
			MfaEvidenceMethod secondFactorMethod = MfaEvidenceMethod.Totp, string factorReference = null)
		{
			var sessionKey = MfaEvidence.TrackedSessionKey(session?.UserSessionId);
			if (sessionKey == null)
				return;

			try
			{
				var client = (UserSessionClientApplication)session.ClientApplication;
				// A login transaction carries the original first-factor time, so a completed login is never fresher than
				// the password or SSO verification it began with (plan section 5.2).
				await _mfaEvidenceService.RecordAsync(user.Id, sessionKey, client, MfaEvidenceKind.FirstFactor, firstFactor,
					MfaEvidencePurpose.Login, firstFactorOnUtc ?? DateTime.UtcNow, user.AuthenticationGeneration, session.DepartmentId,
					cancellationToken: cancellationToken);
				if (secondFactorOnUtc != null)
				{
					// A recovery code is recorded as recovery and never satisfies MFA later (plan section 6.1 item 10).
					var recovery = secondFactorMethod == MfaEvidenceMethod.RecoveryCode;
					await _mfaEvidenceService.RecordAsync(user.Id, sessionKey, client, recovery ? MfaEvidenceKind.Recovery : MfaEvidenceKind.SecondFactor,
						secondFactorMethod, MfaEvidencePurpose.Login, secondFactorOnUtc.Value, user.AuthenticationGeneration, session.DepartmentId,
						factorReference, cancellationToken);
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex, "Failed to record API sign-in MFA evidence.");
			}
		}

		private static void AddSessionClaims(ClaimsPrincipal principal, UserSession session)
		{
			if (principal?.Identity is not ClaimsIdentity identity || session == null)
				return;

			foreach (var existing in identity.FindAll(SessionClaimTypes.SessionId).ToList())
				identity.RemoveClaim(existing);
			foreach (var existing in identity.FindAll(SessionClaimTypes.AuthenticationGeneration).ToList())
				identity.RemoveClaim(existing);
			foreach (var existing in identity.FindAll(SessionClaimTypes.ClientApp).ToList())
				identity.RemoveClaim(existing);

			identity.AddClaim(new Claim(SessionClaimTypes.SessionId, session.UserSessionId));
			identity.AddClaim(new Claim(SessionClaimTypes.AuthenticationGeneration,
				session.AuthenticationGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			identity.AddClaim(new Claim(SessionClaimTypes.ClientApp,
				session.ClientApplication.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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

		/// <summary>
		/// Adds all Resgrid resource claims (View, Create, Update, Delete) to the given identity.
		/// Used for system-level and client-credentials service principals that need full access.
		/// </summary>
		private static void AddAllResourceClaims(ClaimsIdentity identity)
		{
			var resources = new[]
			{
				ResgridClaimTypes.Resources.Department,
				ResgridClaimTypes.Resources.Personnel,
				ResgridClaimTypes.Resources.Call,
				ResgridClaimTypes.Resources.Log,
				ResgridClaimTypes.Resources.Action,
				ResgridClaimTypes.Resources.Staffing,
				ResgridClaimTypes.Resources.Unit,
				ResgridClaimTypes.Resources.Group,
				ResgridClaimTypes.Resources.UnitLog,
				ResgridClaimTypes.Resources.Messages,
				ResgridClaimTypes.Resources.Role,
				ResgridClaimTypes.Resources.Profile,
				ResgridClaimTypes.Resources.Reports,
				ResgridClaimTypes.Resources.GenericGroup,
				ResgridClaimTypes.Resources.Documents,
				ResgridClaimTypes.Resources.Notes,
				ResgridClaimTypes.Resources.Schedule,
				ResgridClaimTypes.Resources.Shift,
				ResgridClaimTypes.Resources.Training,
				ResgridClaimTypes.Resources.PersonalInfo,
				ResgridClaimTypes.Resources.Inventory,
				ResgridClaimTypes.Resources.Command,
				ResgridClaimTypes.Resources.Connect,
				ResgridClaimTypes.Resources.Protocols,
				ResgridClaimTypes.Resources.Forms,
				ResgridClaimTypes.Resources.Voice,
				ResgridClaimTypes.Resources.CustomStates,
				ResgridClaimTypes.Resources.Contacts,
				ResgridClaimTypes.Resources.Workflow,
				ResgridClaimTypes.Resources.WorkflowCredential,
				ResgridClaimTypes.Resources.WorkflowRun,
				ResgridClaimTypes.Resources.Sso,
				ResgridClaimTypes.Resources.Scim,
				ResgridClaimTypes.Resources.Udf,
				ResgridClaimTypes.Resources.Route,
				ResgridClaimTypes.Resources.CommunicationTest,
				ResgridClaimTypes.Resources.WeatherAlert
			};

			var actions = new[]
			{
				ResgridClaimTypes.Actions.View,
				ResgridClaimTypes.Actions.Create,
				ResgridClaimTypes.Actions.Update,
				ResgridClaimTypes.Actions.Delete
			};

			foreach (var resource in resources)
			{
				foreach (var action in actions)
				{
					identity.AddClaim(new Claim(resource, action)
						.SetDestinations(Destinations.AccessToken));
				}
			}
		}
		/// <summary>
		/// Records (RMS) is deliberately absent from <see cref="AddAllResourceClaims"/>. A system principal —
		/// the cross-department system account or a department service account — reads Records only under an
		/// explicitly configured department+purpose grant, and then only with <c>Record_View</c>
		/// (Identifier Allocation Registry section 4.4). No configuration produces a mutating or restricted
		/// Record claim here. The purpose rides along on its own claim so the per-request guard can identify
		/// the principal as non-user and write the purpose to the Record access audit.
		/// </summary>
		/// <param name="departmentId">The principal's department, or 0 for the cross-department system account.</param>
		private static void AddSystemRecordViewClaim(ClaimsIdentity identity, int departmentId)
		{
			var grant = departmentId > 0
				? SystemPrincipalRecordGrant.For(departmentId)
				: SystemPrincipalRecordGrant.All().FirstOrDefault();

			if (grant == null)
				return;

			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View)
				.SetDestinations(Destinations.AccessToken));
			identity.AddClaim(new Claim(ResgridClaimTypes.Data.RecordGrantPurpose, grant.Purpose)
				.SetDestinations(Destinations.AccessToken));
		}

		/// <summary>
		/// Performs a timing-safe comparison of two secret strings to prevent timing attacks.
		/// </summary>
		private static bool FixedTimeSecretEquals(string stored, string provided)
		{
			if (stored == null || provided == null)
				return false;

			var storedBytes = Encoding.UTF8.GetBytes(stored);
			var providedBytes = Encoding.UTF8.GetBytes(provided);

			return CryptographicOperations.FixedTimeEquals(storedBytes, providedBytes);
		}
	}
}
