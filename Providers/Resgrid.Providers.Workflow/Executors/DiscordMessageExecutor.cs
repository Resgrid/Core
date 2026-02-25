using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class DiscordMessageExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.SendDiscordMessage;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<DiscordActionConfig>(context.ActionConfigJson ?? "{}") ?? new DiscordActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("Discord message failed.", "No Discord credential is attached to this workflow step. Please assign a Discord credential.");

				var cred = JsonConvert.DeserializeObject<WebhookCredential>(context.DecryptedCredentialJson) ?? new WebhookCredential();

				if (string.IsNullOrWhiteSpace(cred.WebhookUrl))
					return WorkflowActionResult.Failed("Discord message failed.", "Discord credential is missing a 'WebhookUrl'. Please update the credential with a valid Discord Webhook URL.");

				if (!Uri.TryCreate(cred.WebhookUrl, UriKind.Absolute, out _))
					return WorkflowActionResult.Failed("Discord message failed.", $"The configured WebhookUrl '{cred.WebhookUrl}' is not a valid absolute URI.");

				var payload = new System.Collections.Generic.Dictionary<string, object>();
				var trimmed = context.RenderedContent?.Trim();

				// If rendered content looks like an embed JSON array, use it as embeds
				if (trimmed?.StartsWith("[") == true)
				{
					try { payload["embeds"] = JsonConvert.DeserializeObject(trimmed); }
					catch { payload["content"] = context.RenderedContent; }
				}
				else
				{
					payload["content"] = context.RenderedContent ?? string.Empty;
				}

				if (!string.IsNullOrWhiteSpace(config.Username)) payload["username"] = config.Username;
				if (!string.IsNullOrWhiteSpace(config.AvatarUrl)) payload["avatar_url"] = config.AvatarUrl;

				using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
				var json = JsonConvert.SerializeObject(payload);
				var response = await client.PostAsync(cred.WebhookUrl, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);

				// Discord returns 204 No Content on success
				if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.IsSuccessStatusCode)
					return WorkflowActionResult.Succeeded($"Discord message sent. HTTP {(int)response.StatusCode}");

				var body = await response.Content.ReadAsStringAsync(cancellationToken);
				if ((int)response.StatusCode == 429)
				{
					var rateLimitInfo = TryParseRetryAfter(body);
					return WorkflowActionResult.Failed($"Discord rate limited. Retry after {rateLimitInfo}s", body);
				}
				return WorkflowActionResult.Failed($"Discord webhook error HTTP {(int)response.StatusCode}", body);
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("Discord message failed.", ex.Message);
			}
		}

		private static string TryParseRetryAfter(string body)
		{
			try { return JsonConvert.DeserializeObject<dynamic>(body)?.retry_after?.ToString() ?? "unknown"; }
			catch { return "unknown"; }
		}
	}

	public class DiscordActionConfig { public string Username { get; set; } public string AvatarUrl { get; set; } }
}

