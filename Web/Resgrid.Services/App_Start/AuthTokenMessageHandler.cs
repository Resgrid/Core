using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Providers;
using Resgrid.Providers.Cache;

namespace Resgrid.Web.Services.App_Start
{
	public class AuthTokenMessageHandler : DelegatingHandler
	{
		private static string ValidateUserInfoCacheKey = "ValidateUserInfo_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentsRepository _departmentRepository;

		public AuthTokenMessageHandler()
		{
			_departmentRepository = new DepartmentsRepository(new DataContext(), new StandardIsolation());
			_cacheProvider = new AzureRedisCacheProvider();
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var headers = request.Headers;

			var context = (HttpContextBase)request.Properties["MS_HttpContext"];

			if (!request.RequestUri.AbsoluteUri.ToLower().Contains("v3/auth/validate"))
				AuthAndSetPrinciple(_cacheProvider, _departmentRepository, headers, context, request.RequestUri.AbsoluteUri.Contains("v3"));

			return base.SendAsync(request, cancellationToken);
		}

		// For SignalR
		public static bool AuthAndSetPrinciple(ICacheProvider cacheProvider, IDepartmentsRepository departmentsRepository, NameValueCollection headers, HttpContextBase context)
		{
			var authToken = headers["Authentication"];
			if (string.IsNullOrEmpty(authToken))
				return false;

			if (authToken.IndexOf("Basic ") != 0)
				return false;

			authToken = authToken.Substring(5);

			return AuthAndSetPrinciple(cacheProvider, departmentsRepository, authToken, context, false);
		}

		public static bool AuthAndSetPrinciple(ICacheProvider cacheProvider, IDepartmentsRepository departmentsRepository, HttpRequestHeaders headers, HttpContextBase context, bool v3)
		{
			var authHeader = headers.Authorization;
			if (authHeader == null)
				return false;

			if (authHeader.Scheme != "Basic")
				return false;

			return AuthAndSetPrinciple(cacheProvider, departmentsRepository, authHeader.Parameter, context, v3);
		}

		public static bool AuthAndSetPrinciple(ICacheProvider cacheProvider, IDepartmentsRepository departmentsRepository, string authTokenString, HttpContextBase context, bool v3)
		{
			if (String.IsNullOrWhiteSpace(authTokenString))
				return false;

			var encodedUserPass = authTokenString.Trim();

			if (v3)
			{
				var authToken = V3AuthToken.Decode(encodedUserPass);
				string userId;

				if (Config.SecurityConfig.SystemLoginCredentials.ContainsKey(authToken.UserName))
				{
					if (Config.SecurityConfig.SystemLoginCredentials[authToken.UserName] != encodedUserPass)
						return false;

					authToken.UserId = authToken.UserName;
				}
				else
				{
					if (!ValidateUserAndDepartmentByUser(cacheProvider, departmentsRepository, authToken.UserName, authToken.DepartmentId, null, out userId))
						return false;

					authToken.UserId = userId;
				}

				var principal = new ResgridPrincipleV3(authToken);
				Thread.CurrentPrincipal = principal;
				if (context != null)
				{
					context.User = principal;
				}
			}
			else
			{
				var authToken = AuthToken.Decode(encodedUserPass);
				string userId;

				if (!ValidateUserAndDepartmentByUser(cacheProvider, departmentsRepository, authToken.UserName, authToken.DepartmentId, authToken.DepartmentCode, out userId))
					return false;

				var principal = new ResgridPrinciple(authToken);
				Thread.CurrentPrincipal = principal;
				if (context != null)
				{
					context.User = principal;
				}

			}

			return true;
		}

		private static bool ValidateUserAndDepartmentByUser(ICacheProvider cacheProvider, IDepartmentsRepository departmentRepository, string userName, int departmentId, string departmentCode, out string userId)
		{
			var data = GetValidateUserForDepartmentInfo(cacheProvider, departmentRepository, userName, false);
			userId = String.Empty;

			if (data == null)
				return false;

			userId = data.UserId;

			if (data.DepartmentId != departmentId)
				return false;

			if (data.IsDisabled.GetValueOrDefault())
				return false;

			if (data.IsDeleted.GetValueOrDefault())
				return false;

			if (departmentCode != null)
				if (!data.Code.Equals(departmentCode, StringComparison.InvariantCultureIgnoreCase))
					return false;

			return true;
		}

		private static ValidateUserForDepartmentResult GetValidateUserForDepartmentInfo(ICacheProvider cacheProvider, IDepartmentsRepository departmentRepository, string userName, bool bypassCache = true)
		{
			if (!bypassCache)
			{
				Func<ValidateUserForDepartmentResult> validateForDepartment = delegate ()
				{
					return departmentRepository.GetValidateUserForDepartmentData(userName);
				};

				return cacheProvider.Retrieve<ValidateUserForDepartmentResult>(string.Format(ValidateUserInfoCacheKey, userName), validateForDepartment, CacheLength);
			}
			else
			{
				return departmentRepository.GetValidateUserForDepartmentData(userName);
			}
		}
	}

	public class AuthToken
	{
		public AuthToken(string userName, int departmentId, string departmentCode)
		{
			this.UserName = userName;
			this.DepartmentId = departmentId;
			this.DepartmentCode = departmentCode;
		}

		public string UserName { get; private set; }

		public int DepartmentId { get; private set; }

		public string DepartmentCode { get; private set; }



		/// <summary>
		/// Decodes the base64 encoded auth token
		/// 
		/// EX: "VXNlck5hbWV8MXxBQkNE" turns into "UserName|1|ABCD" which is a pipe delimeted list of "UserName|DepartmentId|DepartmentCode" and returns an AuthToken object with this metadata parsed out.
		/// </summary>
		/// <param name="authHeader"></param>
		/// <returns></returns>
		public static AuthToken Decode(string authHeader)
		{
			if (string.IsNullOrEmpty(authHeader))
				throw new ArgumentException("value cannot be null or empty", "authHeader");

			string[] rows = null;

			try
			{
				var authBytes = Convert.FromBase64String(authHeader);
				var authStr = Encoding.ASCII.GetString(authBytes);
				rows = authStr.Split('|');
			}
			catch (Exception ex)
			{
				//TODO: log exception here? with metada used in authHeader?
				throw new HttpResponseException(HttpStatusCode.Unauthorized);
			}

			if (rows.Length != 3)
				throw new HttpResponseException(HttpStatusCode.Unauthorized);

			string username = rows[0];
			int departmentId;
			string departmentCode = rows[2];

			if (string.IsNullOrEmpty(username))
				throw new HttpResponseException(HttpStatusCode.Unauthorized);

			if (!int.TryParse(rows[1], out departmentId))
				throw new HttpResponseException(HttpStatusCode.Unauthorized);

			if (string.IsNullOrEmpty(departmentCode))
				throw new HttpResponseException(HttpStatusCode.Unauthorized);


			return new AuthToken(username, departmentId, departmentCode);
		}

		public static string Encode(string userName, int departmentId, string departmentCode)
		{
			var buffer = Encoding.ASCII.GetBytes(string.Join("|", new[] { userName, departmentId.ToString(), departmentCode }));
			var authHeader = Convert.ToBase64String(buffer);
			return authHeader;
		}

		public static AuthenticationHeaderValue GetAuthHeaderValue(AuthToken authToken)
		{
			var authString = Encode(authToken.UserName, authToken.DepartmentId, authToken.DepartmentCode);
			return new AuthenticationHeaderValue("Basic", authString);
		}
	}

	public class ResgridPrinciple : IPrincipal
	{
		private IIdentity _identity;
		public ResgridPrinciple(AuthToken authToken)
		{
			AuthToken = authToken;
			_identity = new GenericIdentity(authToken.UserName, "Basic");
		}

		public AuthToken AuthToken { get; private set; }

		public IIdentity Identity
		{
			get { return _identity; }
		}

		public bool IsInRole(string role)
		{
			throw new NotImplementedException();
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

			try
			{
				var authBytes = Convert.FromBase64String(authHeader);
				var cypherText = Encoding.ASCII.GetString(authBytes);
				var plainText = SymmetricEncryption.Decrypt(cypherText, Config.SystemBehaviorConfig.ApiTokenEncryptionPassphrase);

				rows = plainText.Split('|');
			}
			catch (Exception ex)
			{
				//TODO: log exception here? with metada used in authHeader?
				throw new HttpResponseException(HttpStatusCode.Unauthorized);
			}

			if (rows.Length != 3)
				throw new HttpResponseException(HttpStatusCode.Unauthorized);

			string username = rows[0];
			int departmentId;
			DateTime tokenExpiry;

			if (string.IsNullOrEmpty(username))
				throw new HttpResponseException(HttpStatusCode.Unauthorized);

			if (!int.TryParse(rows[1], out departmentId))
				throw new HttpResponseException(HttpStatusCode.Unauthorized);

			if (!DateTime.TryParse(rows[2], out tokenExpiry))
				throw new HttpResponseException(HttpStatusCode.Unauthorized);

			if (tokenExpiry <= DateTime.UtcNow)
				throw new HttpResponseException(HttpStatusCode.UpgradeRequired);

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

		public static AuthenticationHeaderValue GetAuthHeaderValue(AuthToken authToken)
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
