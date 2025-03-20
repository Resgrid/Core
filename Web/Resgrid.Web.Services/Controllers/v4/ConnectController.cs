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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

		public ConnectController(
			IUsersService usersService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService,
			SignInManager<Model.Identity.IdentityUser> signInManager,
			UserManager<Model.Identity.IdentityUser> userManager,
			ISystemAuditsService systemAuditsService
			)
		{
			_usersService = usersService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
			_signInManager = signInManager;
			_userManager = userManager;
			_systemAuditsService = systemAuditsService;
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

		private IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
		{
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
