using System;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Resgrid.Framework;

namespace Resgrid.Web.Services.ApplicationCore.Tokens
{
	public class AuthToken
	{
		public string UserName { get; private set; }
		public int DepartmentId { get; private set; }
		public DateTime TokenExpiry { get; private set; }
		public string UserId { get; set; }

		public AuthToken(string userName, int departmentId, DateTime tokenExpiry)
		{
			UserName = userName;
			DepartmentId = departmentId;
			TokenExpiry = tokenExpiry;
		}

		public static AuthToken Decode(string authHeader)
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
				//throw new HttpResponseException(HttpStatusCode.Unauthorized);
			}

			//if (rows.Length != 3)
				//throw new HttpResponseException(HttpStatusCode.Unauthorized);

			string username = rows[0];
			int departmentId = 0;
			DateTime tokenExpiry = DateTime.UtcNow;

			//if (string.IsNullOrEmpty(username))
			//	throw new HttpResponseException(HttpStatusCode.Unauthorized);

			//if (!int.TryParse(rows[1], out departmentId))
			//	throw new HttpResponseException(HttpStatusCode.Unauthorized);

			//if (!DateTime.TryParse(rows[2], out tokenExpiry))
			//	throw new HttpResponseException(HttpStatusCode.Unauthorized);

			//if (tokenExpiry <= DateTime.UtcNow)
			//	throw new HttpResponseException(HttpStatusCode.UpgradeRequired);

			return new AuthToken(username, departmentId, tokenExpiry);
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
}
