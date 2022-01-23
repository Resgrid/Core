using Resgrid.Framework;
using Resgrid.Model.Repositories;
using System;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Custom;
using Resgrid.Model.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Linq;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.ServicesCore.Middleware
{
	public class AuthTokenMiddleware
	{
		private static string ValidateUserInfoCacheKey = "ValidateUserInfo_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly RequestDelegate _next;
		private readonly ILogger _logger;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentsRepository _departmentRepository;


		public AuthTokenMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, IDepartmentsRepository departmentRepository, ICacheProvider cacheProvider)
		{
			_next = next;
			_logger = loggerFactory.CreateLogger<AuthTokenMiddleware>();
			_departmentRepository = departmentRepository;
			_cacheProvider = cacheProvider;
		}

		public async Task Invoke(HttpContext context)
		{
			_logger.LogInformation("Handling API key for: " + context.Request.Path);

			if (!context.Request.Path.Value.ToLower().Contains("v3/auth/validate"))
				await AuthAndSetPrinciple(_cacheProvider, _departmentRepository, context, context.Request.Path.Value.Contains("v3"));


			await _next.Invoke(context);

			_logger.LogInformation("Finished handling api key.");
		}

		public static async Task<bool> AuthAndSetPrinciple(ICacheProvider cacheProvider, IDepartmentsRepository departmentsRepository, HttpContext context, bool v3)
		{
			StringValues authHeader;
			if (context.Request.Headers.TryGetValue("Authorization", out authHeader))
			{
				if (authHeader.Count <= 0)
					return false;

				if (!authHeader[0].Contains("Basic"))
					return false;

				return await AuthAndSetPrinciple(cacheProvider, departmentsRepository, authHeader[0].Replace("Basic", "").Trim(), context, v3);
			}

			return false;
		}

		public static async Task<bool> AuthAndSetPrinciple(ICacheProvider cacheProvider, IDepartmentsRepository departmentsRepository, string authTokenString, HttpContext context, bool v3)
		{
			if (string.IsNullOrWhiteSpace(authTokenString))
				return false;

			var encodedUserPass = authTokenString.Trim();

			if (v3)
			{
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

					var principal = new ResgridPrincipleV3(authToken);
					Thread.CurrentPrincipal = principal;
					if (context != null)
					{
						context.User = new System.Security.Claims.ClaimsPrincipal(principal);
					}
				}
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

	public class V3AuthToken
	{
		public string UserName { get; private set; }
		public int DepartmentId { get; private set; }
		public DateTime TokenExpiry { get; private set; }
		public string UserId { get; set; }

		public V3AuthToken(string userName, int departmentId, DateTime tokenExpiry)
		{
			UserName = userName;
			DepartmentId = departmentId;
			TokenExpiry = tokenExpiry;
		}

		public static V3AuthToken Decode(string authHeader)
		{
			if (string.IsNullOrEmpty(authHeader))
				throw new ArgumentException("value cannot be null or empty", "authHeader");

			string[] rows = null;

			byte[] authBytes = null;
			string cypherText = null;
			string plainText = null;

			try
			{
				authBytes = Convert.FromBase64String(authHeader);
				cypherText = Encoding.ASCII.GetString(authBytes);
				plainText = SymmetricEncryption.Decrypt(cypherText, Config.SystemBehaviorConfig.ApiTokenEncryptionPassphrase);

				rows = plainText.Split('|');
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"{cypherText} {plainText}");
				//TODO: log exception here? with metada used in authHeader?
				return null;
			}

			if (rows.Length != 3)
			{
				return null;
			}

			string username = rows[0];
			int departmentId;
			DateTime tokenExpiry;

			if (string.IsNullOrEmpty(username))
			{
				return null;
			}

			if (!int.TryParse(rows[1], out departmentId))
			{
				return null;
			}

			if (!DateTime.TryParse(rows[2], out tokenExpiry))
			{
				return null;
			}

			if (tokenExpiry <= DateTime.UtcNow)
			{
				return null;
			}

			return new V3AuthToken(username, departmentId, tokenExpiry);
		}

		public static string Create(string userName, int departmentId)
		{
			var painText = string.Join("|", new[] { userName, departmentId.ToString(), DateTime.UtcNow.AddMonths(Config.SystemBehaviorConfig.APITokenMonthsTTL).ToShortDateString() });
			var encryptedText = SymmetricEncryption.Encrypt(painText, Config.SystemBehaviorConfig.ApiTokenEncryptionPassphrase);
			var buffer = Encoding.ASCII.GetBytes(encryptedText);
			var authHeader = Convert.ToBase64String(buffer);

			return authHeader;
		}

		public static AuthenticationHeaderValue GetAuthHeaderValue(V3AuthToken authToken)
		{
			var authString = Create(authToken.UserName, authToken.DepartmentId);
			return new AuthenticationHeaderValue("Basic", authString);
		}
	}

	public class ResgridPrincipleV3 : IPrincipal
	{
		private IIdentity _identity;
		public ResgridPrincipleV3(V3AuthToken authToken)
		{
			AuthToken = authToken;
			IsSystem = false;

			_identity = new GenericIdentity(authToken.UserName, "Basic");
		}

		public V3AuthToken AuthToken { get; private set; }

		public IIdentity Identity
		{
			get { return _identity; }
		}

		public bool IsInRole(string role)
		{
			throw new NotImplementedException();
		}

		public bool IsSystem { get; set; }
	}
}
