using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class HttpApiExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.CallApiPost;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = string.IsNullOrWhiteSpace(context.ActionConfigJson)
					? new HttpActionConfig()
					: JsonConvert.DeserializeObject<HttpActionConfig>(context.ActionConfigJson) ?? new HttpActionConfig();

				if (string.IsNullOrWhiteSpace(config.Url))
					return WorkflowActionResult.Failed("HTTP request failed.", "No URL is configured for this workflow step. Please set the 'Url' field in the action config.");

				if (!Uri.TryCreate(config.Url, UriKind.Absolute, out _))
					return WorkflowActionResult.Failed("HTTP request failed.", $"The configured URL '{config.Url}' is not a valid absolute URI.");

				var cred = string.IsNullOrWhiteSpace(context.DecryptedCredentialJson)
					? null
					: JsonConvert.DeserializeObject<HttpCredential>(context.DecryptedCredentialJson);

				if (cred != null)
				{
					switch (cred.AuthType?.ToLowerInvariant())
					{
						case "bearer" when string.IsNullOrWhiteSpace(cred.Token):
							return WorkflowActionResult.Failed("HTTP request failed.", "Auth type is 'bearer' but no token is set in the credential. Please update the credential with a valid token.");
						case "basic" when string.IsNullOrWhiteSpace(cred.Username):
							return WorkflowActionResult.Failed("HTTP request failed.", "Auth type is 'basic' but no username is set in the credential.");
						case "apikey" when string.IsNullOrWhiteSpace(cred.ApiKey):
							return WorkflowActionResult.Failed("HTTP request failed.", "Auth type is 'apikey' but no API key is set in the credential.");
					}
				}

				using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 30) };

				ApplyAuth(client, cred);

				if (config.Headers != null)
					foreach (var h in config.Headers)
						client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

				var method = (WorkflowActionType)context.ActionType switch
				{
					WorkflowActionType.CallApiGet    => HttpMethod.Get,
					WorkflowActionType.CallApiPut    => HttpMethod.Put,
					WorkflowActionType.CallApiDelete => HttpMethod.Delete,
					_                                => HttpMethod.Post
				};

				var request = new HttpRequestMessage(method, config.Url);

				if (method != HttpMethod.Get && method != HttpMethod.Delete)
				{
					var contentType = string.IsNullOrWhiteSpace(config.ContentType) ? "application/json" : config.ContentType;
					request.Content = new StringContent(context.RenderedContent ?? string.Empty, Encoding.UTF8, contentType);
				}

				var response = await client.SendAsync(request, cancellationToken);
				var body = await response.Content.ReadAsStringAsync(cancellationToken);
				var snippet = body?.Length > 4000 ? body.Substring(0, 4000) : body;

				if (response.IsSuccessStatusCode)
					return WorkflowActionResult.Succeeded($"HTTP {(int)response.StatusCode}: {snippet}");

				return WorkflowActionResult.Failed($"HTTP {(int)response.StatusCode}", snippet);
			}
			catch (TaskCanceledException)
			{
				return WorkflowActionResult.Failed("Request timed out.", "The HTTP request exceeded the configured timeout.");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("HTTP request failed.", ex.Message);
			}
		}

		private static void ApplyAuth(HttpClient client, HttpCredential cred)
		{
			if (cred == null) return;
			switch (cred.AuthType?.ToLowerInvariant())
			{
				case "bearer":
					client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cred.Token);
					break;
				case "basic":
					var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.Username}:{cred.Password}"));
					client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
					break;
				case "apikey":
					client.DefaultRequestHeaders.TryAddWithoutValidation(cred.ApiKeyHeader ?? "X-Api-Key", cred.ApiKey);
					break;
			}
		}
	}

	public class HttpActionConfig
	{
		public string Url { get; set; }
		public string ContentType { get; set; }
		public int TimeoutSeconds { get; set; } = 30;
		public System.Collections.Generic.Dictionary<string, string> Headers { get; set; }
	}

	public class HttpCredential
	{
		public string AuthType { get; set; }
		public string Token { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
		public string ApiKeyHeader { get; set; }
		public string ApiKey { get; set; }
	}
}

