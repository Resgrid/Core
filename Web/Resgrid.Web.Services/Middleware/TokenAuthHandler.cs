using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Framework;
using Resgrid.Model.Custom;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.ServicesCore.Middleware;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Services.Middleware
{
	public class ResgridTokenAuthHandler : AuthenticationHandler<ResgridAuthenticationOptions>
	{
		private static string ValidateUserInfoCacheKey = "ValidateUserInfo_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentsRepository _departmentRepository;
		private readonly IUserClaimsPrincipalFactory<IdentityUser> _claimsPrincipalFactory;
		private readonly IUsersService _usersService;
		private readonly ILoggerFactory _logger;

		public ResgridTokenAuthHandler(IOptionsMonitor<ResgridAuthenticationOptions> options, ILoggerFactory logger,
			UrlEncoder encoder, ISystemClock clock, ICacheProvider cacheProvider, IDepartmentsRepository departmentRepository,
			IUserClaimsPrincipalFactory<IdentityUser> claimsPrincipalFactory, IUsersService usersService)
			: base(options, logger, encoder, clock)
		{
			_cacheProvider = cacheProvider;
			_departmentRepository = departmentRepository;
			_claimsPrincipalFactory = claimsPrincipalFactory;
			_usersService = usersService;
			_logger = logger;
		}

		protected new ResgridAuthenticationEvents Events
		{
			get { return (ResgridAuthenticationEvents)base.Events; }
			set { base.Events = value; }
		}

		protected override Task<object> CreateEventsAsync() => Task.FromResult<object>(new ResgridAuthenticationEvents());

		protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			var endpoint = Context.GetEndpoint();
			if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
				return AuthenticateResult.NoResult();

			if (!Request.Headers.ContainsKey("Authorization"))
				return AuthenticateResult.Fail("Missing Authorization Header");

			try
			{
				//var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]);
				var authHeaderValue = Request.Headers["Authorization"].ToString();

				if (string.IsNullOrWhiteSpace(authHeaderValue))
					return AuthenticateResult.Fail("Missing Authorization Header value, blank");

				authHeaderValue = authHeaderValue.Replace("Basic", "", StringComparison.InvariantCultureIgnoreCase).Trim();

				if (string.IsNullOrWhiteSpace(authHeaderValue))
					return AuthenticateResult.Fail("Missing Authorization Header value, no data with auth type");

				var result = await AuthAndSetPrinciple(_cacheProvider, _departmentRepository, authHeaderValue);

				if (!result)
					return AuthenticateResult.Fail($"Invalid Authorization Header: {authHeaderValue}");

				var authToken = V3AuthToken.Decode(authHeaderValue);

				if (authToken == null)
					return AuthenticateResult.Fail($"Invalid Authorization Header, null auth token: {authHeaderValue}");

				var user = await _usersService.GetUserByNameAsync(authToken.UserName);
				var principal = await _claimsPrincipalFactory.CreateAsync(user);

				Thread.CurrentPrincipal = principal;
				Context.User = principal;

				var ticket = new AuthenticationTicket(principal, Scheme.Name);
				return AuthenticateResult.Success(ticket);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return AuthenticateResult.Fail($"Invalid Authorization Header: {ex}");
			}
		}

		public static async Task<bool> AuthAndSetPrinciple(ICacheProvider cacheProvider, IDepartmentsRepository departmentsRepository, string authTokenString)
		{
			if (string.IsNullOrWhiteSpace(authTokenString))
				return false;

			var encodedUserPass = authTokenString.Trim();

			var authToken = V3AuthToken.Decode(encodedUserPass);

			if (authToken != null)
			{
				string userId;

				if (Config.SecurityConfig.SystemLoginCredentials.ContainsKey(authToken.UserName))
				{
					if (Config.SecurityConfig.SystemLoginCredentials[authToken.UserName] != encodedUserPass)
						return false;

					authToken.UserId = authToken.UserName;
				}
				else
				{
					var result = await ValidateUserAndDepartmentByUser(cacheProvider, departmentsRepository, authToken.UserName, authToken.DepartmentId, null);
					if (!result.IsValid)
						return false;

					authToken.UserId = result.UserId;
				}

				//var principal = new ResgridPrincipleV3(authToken);
				//Thread.CurrentPrincipal = principal;
				//if (context != null)
				//{
				//	context.User = new System.Security.Claims.ClaimsPrincipal(principal);
				//}
			}

			return true;
		}

		private static async Task<AuthValidationResult> ValidateUserAndDepartmentByUser(ICacheProvider cacheProvider, IDepartmentsRepository departmentRepository, string userName, int departmentId, string departmentCode)
		{
			var result = new AuthValidationResult();

			var data = await GetValidateUserForDepartmentInfo(cacheProvider, departmentRepository, userName, false);
			result.UserId = string.Empty;

			result.IsValid = true;

			if (data == null)
				result.IsValid = false;

			result.UserId = data.UserId;

			if (data.DepartmentId != departmentId)
				result.IsValid = false;

			if (data.IsDisabled.GetValueOrDefault())
				result.IsValid = false;

			if (data.IsDeleted.GetValueOrDefault())
				result.IsValid = false;

			if (departmentCode != null)
				if (!data.Code.Equals(departmentCode, StringComparison.InvariantCultureIgnoreCase))
					result.IsValid = false;

			return result;
		}

		private static async Task<ValidateUserForDepartmentResult> GetValidateUserForDepartmentInfo(ICacheProvider cacheProvider, IDepartmentsRepository departmentRepository, string userName, bool bypassCache = true)
		{
			async Task<ValidateUserForDepartmentResult> validateForDepartment()
			{
				return await departmentRepository.GetValidateUserForDepartmentDataAsync(userName);
			}

			if (!bypassCache)
			{
				return await cacheProvider.RetrieveAsync(string.Format(ValidateUserInfoCacheKey, userName), validateForDepartment, CacheLength);
			}

			return await validateForDepartment();
		}
	}
}
