using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Middleware
{
	/// <summary>
	/// Authenticates requests that carry a department API key in the X-Resgrid-ApiKey header. The scheme only runs on
	/// v4 actions marked with <see cref="Attributes.DepartmentApiKeyScopeAttribute"/>, so a key cannot reach any other
	/// endpoint, and the attribute's policy requires the key to hold that action's scope.
	///
	/// The principal is department-scoped: PrimaryGroupSid is the key's department, and PrimarySid is the department's
	/// managing user, so per-user checks in the existing actions evaluate as the department owner (narrowed by the scope).
	/// It is never a service account: the cross-department behaviour of the system key does not apply. It carries only the
	/// resource claims its scopes need, one scope claim per scope, and the key id.
	/// </summary>
	public class DepartmentApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
	{
		public const string SchemeName = "DepartmentApiKey";
		public const string AuthenticationType = "DepartmentApiKey";
		public const string HeaderName = "X-Resgrid-ApiKey";

		private readonly IDepartmentApiKeysService _departmentApiKeysService;
		private readonly IMemoryCache _memoryCache;

		public DepartmentApiKeyAuthHandler(
			IOptionsMonitor<AuthenticationSchemeOptions> options,
			ILoggerFactory logger,
			UrlEncoder encoder,
			ISystemClock clock,
			IDepartmentApiKeysService departmentApiKeysService,
			IMemoryCache memoryCache)
			: base(options, logger, encoder, clock)
		{
			_departmentApiKeysService = departmentApiKeysService;
			_memoryCache = memoryCache;
		}

		protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			var endpoint = Context.GetEndpoint();
			if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
				return AuthenticateResult.NoResult();

			if (!Request.Headers.TryGetValue(HeaderName, out var headerValue))
				return AuthenticateResult.NoResult();

			var key = headerValue.ToString().Trim();
			if (string.IsNullOrWhiteSpace(key))
				return AuthenticateResult.NoResult();

			// One caller, one identity: a key next to a bearer token or the system key would merge two principals.
			if (Request.Headers.ContainsKey("Authorization") || Request.Headers.ContainsKey("X-Resgrid-SystemApiKey"))
				return AuthenticateResult.Fail("Send either an API key or a bearer token, not both.");

			var remoteIp = Context.Connection.RemoteIpAddress;
			if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
				remoteIp = remoteIp.MapToIPv4();
			var remoteAddress = remoteIp?.ToString();

			// Failed keys are refused before the rate limiter runs, so guessing is throttled here, per source address.
			var failureKey = "DepartmentApiKeyFailures_" + (remoteAddress ?? "unknown");
			var maxFailures = Math.Max(1, Config.SecurityConfig.DepartmentApiKeyMaxFailuresPerAddress);
			if (_memoryCache.TryGetValue(failureKey, out int failures) && failures >= maxFailures)
				return AuthenticateResult.Fail("Too many invalid API keys from this address. Try again later.");

			var result = await _departmentApiKeysService.AuthenticateAsync(key, remoteAddress, Context.RequestAborted);

			if (result == null || !result.Success)
			{
				_memoryCache.Set(failureKey, failures + 1,
					TimeSpan.FromMinutes(Math.Max(1, Config.SecurityConfig.DepartmentApiKeyFailureWindowMinutes)));

				return AuthenticateResult.Fail(DescribeFailure(result?.Status ?? DepartmentApiKeyAuthenticationStatus.Invalid));
			}

			var principal = new ClaimsPrincipal(BuildIdentity(result));
			return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
		}

		public static ClaimsIdentity BuildIdentity(DepartmentApiKeyAuthenticationResult result)
		{
			var apiKey = result.ApiKey;
			var department = result.Department;
			var displayName = $"{apiKey.Name} (API key)";

			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.Name, displayName),
				new Claim(ClaimTypes.PrimarySid, department.ManagingUserId),
				new Claim(ClaimTypes.PrimaryGroupSid, department.DepartmentId.ToString()),
				new Claim(ClaimTypes.Actor, department.Name ?? string.Empty),
				new Claim(ResgridClaimTypes.Data.UserId, department.ManagingUserId),
				new Claim(ResgridClaimTypes.Data.DisplayName, displayName),
				new Claim(ResgridClaimTypes.Data.TimeZone, string.IsNullOrWhiteSpace(department.TimeZone) ? "UTC" : department.TimeZone),
				new Claim(DepartmentApiKeyScopes.KeyIdClaimType, apiKey.DepartmentApiKeyId)
			};

			var resourceClaims = new HashSet<(string Resource, string Action)>();
			foreach (var scope in result.Scopes ?? new List<string>())
			{
				claims.Add(new Claim(DepartmentApiKeyScopes.ScopeClaimType, scope));

				foreach (var resourceClaim in ResourceClaimsFor(scope))
					resourceClaims.Add(resourceClaim);
			}

			claims.AddRange(resourceClaims.Select(x => new Claim(x.Resource, x.Action)));

			return new ClaimsIdentity(claims, AuthenticationType);
		}

		/// <summary>The resource claims the existing policies on a scope's endpoints require.</summary>
		public static IEnumerable<(string Resource, string Action)> ResourceClaimsFor(string scope)
		{
			switch (scope)
			{
				case DepartmentApiKeyScopes.CallsRead:
					yield return (ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View);
					break;
				case DepartmentApiKeyScopes.CallsCreate:
					yield return (ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create);
					break;
				case DepartmentApiKeyScopes.CallsUpdate:
				case DepartmentApiKeyScopes.CallsClose:
					yield return (ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update);
					break;
				case DepartmentApiKeyScopes.ReferenceRead:
					// Call types, priorities and templates sit behind Call_View; the scope policy still keeps the key off the call endpoints.
					yield return (ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View);
					yield return (ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.View);
					yield return (ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.View);
					break;
				case DepartmentApiKeyScopes.UnitsRead:
					yield return (ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.View);
					break;
			}
		}

		/// <summary>True when the principal (or one of its identities) came from a department API key.</summary>
		public static bool IsDepartmentApiKeyPrincipal(ClaimsPrincipal user) =>
			user?.Identities?.Any(i => i.IsAuthenticated && i.AuthenticationType == AuthenticationType) == true;

		private static string DescribeFailure(DepartmentApiKeyAuthenticationStatus status)
		{
			switch (status)
			{
				case DepartmentApiKeyAuthenticationStatus.Expired:
					return "The API key has expired.";
				case DepartmentApiKeyAuthenticationStatus.Revoked:
					return "The API key has been revoked.";
				case DepartmentApiKeyAuthenticationStatus.AddressNotAllowed:
					return "The API key cannot be used from this address.";
				case DepartmentApiKeyAuthenticationStatus.DepartmentUnavailable:
					return "The API key's department is not available.";
				default:
					return "Invalid API key.";
			}
		}
	}
}
