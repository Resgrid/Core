using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Providers.Chatbot.Services
{
	/// <summary>Bounded HTTP transport. Provider bodies, URLs and credentials never enter exceptions.</summary>
	public class ChatbotHttpClient
	{
		private static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false })
		{ Timeout = TimeSpan.FromSeconds(15) };
		private readonly HttpClient _http;
		public ChatbotHttpClient() : this(Shared) { }
		public ChatbotHttpClient(HttpClient http) { _http = http; }

		public Task<JObject> PostAsync(string url, object payload, string authorization = null, string tokenHeader = null, string token = null)
			=> SendAsync(url, new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"), authorization, tokenHeader, token);

		public Task<JToken> PostJsonAsync(string url, object payload, string authorization)
			=> RequestAsync(HttpMethod.Post, url, new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"), authorization, null, null);

		public Task<JObject> GetAsync(string url, string authorization)
			=> RequireObjectAsync(RequestAsync(HttpMethod.Get, url, null, authorization, null, null));

		public Task<JObject> SendAsync(string url, HttpContent content, string authorization = null, string tokenHeader = null, string token = null)
			=> RequireObjectAsync(RequestAsync(HttpMethod.Post, url, content, authorization, tokenHeader, token));

		private static async Task<JObject> RequireObjectAsync(Task<JToken> request)
			=> await request as JObject ?? throw new InvalidOperationException("Messaging provider returned an invalid response.");

		private async Task<JToken> RequestAsync(HttpMethod method, string url, HttpContent content, string authorization, string tokenHeader, string token)
		{
			using var request = new HttpRequestMessage(method, url) { Content = content };
			if (authorization != null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
			if (tokenHeader != null) request.Headers.TryAddWithoutValidation(tokenHeader, token);
			try
			{
				using var response = await _http.SendAsync(request);
				if (!response.IsSuccessStatusCode)
					throw new InvalidOperationException($"Messaging provider rejected the request (HTTP {(int)response.StatusCode}).");
				var body = await response.Content.ReadAsStringAsync();
				return string.IsNullOrWhiteSpace(body) ? new JObject() : JToken.Parse(body);
			}
			catch (HttpRequestException) { throw new InvalidOperationException("Messaging provider connection failed."); }
			catch (TaskCanceledException) { throw new InvalidOperationException("Messaging provider request timed out."); }
			catch (JsonException) { throw new InvalidOperationException("Messaging provider returned an invalid response."); }
		}
	}
}
