using CommonServiceLocator;
using IdentityModel.Client;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Dynamic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Resgrid.WebCore.Helpers
{
	public class ApiAuthHelper
	{
		private static IHttpClientFactory _httpClientFactory;

		public static async Task<string> GetBearerApiTokenAsync(string username, string password)
		{
			if (_httpClientFactory == null)
				_httpClientFactory = ServiceLocator.Current.GetInstance<IHttpClientFactory>();

			HttpClient client = _httpClientFactory.CreateClient("ByPassSSLHttpClient");
			// Retrieve the OpenIddict server configuration document containing the endpoint URLs.
			var configuration = await client.GetDiscoveryDocumentAsync(Config.SystemBehaviorConfig.ResgridApiBaseUrl);
			if (configuration.IsError)
			{
				throw new Exception($"An error occurred while retrieving the configuration document: {configuration.Error}");
			}

			var response = await client.RequestPasswordTokenAsync(new PasswordTokenRequest
			{
				Address = configuration.TokenEndpoint,
				UserName = username,
				Password = password,
				Scope = "openid profile offline_access"
			});

			if (response.IsError)
			{
				throw new Exception($"An error occurred while retrieving an access token: {response.Error}");
			}
			
			DateTime expireDate = DateTime.Now.AddSeconds(response.ExpiresIn + 1000);

			dynamic tokenResult = new ExpandoObject();
			tokenResult.access_token = response.AccessToken;
			tokenResult.refresh_token = response.RefreshToken;
			tokenResult.id_token = response.IdentityToken;
			tokenResult.expires_in = response.ExpiresIn;
			tokenResult.token_type = response.TokenType;
			tokenResult.expiration_date = expireDate;//.ToString("dd/MM/yyyy hh:mm");

			//var settings = new Newtonsoft.Json.JsonSerializerSettings();

			var serializer = new JsonSerializer();
			var stringWriter = new StringWriter();
			using (var writer = new JsonTextWriter(stringWriter))
			{
				//writer.QuoteName = false;
				serializer.Serialize(writer, tokenResult);
			}
			var json = stringWriter.ToString();

			//string json = Newtonsoft.Json.JsonConvert.SerializeObject(tokenResult, Newtonsoft.Json.Formatting.None, settings);

			return json;
		}
	}
}
