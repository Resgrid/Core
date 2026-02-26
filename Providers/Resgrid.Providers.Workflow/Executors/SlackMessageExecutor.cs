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
	public class SlackMessageExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.SendSlackMessage;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<SlackActionConfig>(context.ActionConfigJson ?? "{}") ?? new SlackActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("Slack message failed.", "No Slack credential is attached to this workflow step. Please assign a Slack credential.");

				var cred = JsonConvert.DeserializeObject<WebhookCredential>(context.DecryptedCredentialJson) ?? new WebhookCredential();

				if (string.IsNullOrWhiteSpace(cred.WebhookUrl))
					return WorkflowActionResult.Failed("Slack message failed.", "Slack credential is missing a 'WebhookUrl'. Please update the credential with a valid Slack Incoming Webhook URL.");

				if (!Uri.TryCreate(cred.WebhookUrl, UriKind.Absolute, out _))
					return WorkflowActionResult.Failed("Slack message failed.", $"The configured WebhookUrl '{cred.WebhookUrl}' is not a valid absolute URI.");

				// ── Webhook domain validation ────────────────────────────────────────
				var (webhookValid, webhookReason) = WebhookUrlValidator.Validate(cred.WebhookUrl, "slack");
				if (!webhookValid)
					return WorkflowActionResult.Failed("Slack message blocked.", webhookReason);
				// ── End webhook domain validation ────────────────────────────────────

				var payload = new System.Collections.Generic.Dictionary<string, object>
				{
					["text"] = context.RenderedContent ?? string.Empty
				};
				if (!string.IsNullOrWhiteSpace(config.Channel)) payload["channel"] = config.Channel;
				if (!string.IsNullOrWhiteSpace(config.Username)) payload["username"] = config.Username;
				if (!string.IsNullOrWhiteSpace(config.IconEmoji)) payload["icon_emoji"] = config.IconEmoji;

				using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
				var json = JsonConvert.SerializeObject(payload);
				var response = await client.PostAsync(cred.WebhookUrl, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
				var body = await response.Content.ReadAsStringAsync(cancellationToken);

				if (response.IsSuccessStatusCode && body == "ok")
					return WorkflowActionResult.Succeeded("Slack message sent.");
				if (response.IsSuccessStatusCode)
					return WorkflowActionResult.Succeeded($"Slack response: {body}");
				return WorkflowActionResult.Failed($"Slack webhook error: {body}", $"HTTP {(int)response.StatusCode}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("Slack message failed.", ex.Message);
			}
		}
	}

	public class SlackActionConfig { public string Channel { get; set; } public string Username { get; set; } public string IconEmoji { get; set; } }
}

