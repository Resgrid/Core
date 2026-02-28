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
	public class TeamsMessageExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.SendTeamsMessage;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<TeamsActionConfig>(context.ActionConfigJson ?? "{}") ?? new TeamsActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("Teams message failed.", "No Teams credential is attached to this workflow step. Please assign a Teams credential.");

				var cred = JsonConvert.DeserializeObject<WebhookCredential>(context.DecryptedCredentialJson) ?? new WebhookCredential();

				if (string.IsNullOrWhiteSpace(cred.WebhookUrl))
					return WorkflowActionResult.Failed("Teams message failed.", "Teams credential is missing a 'WebhookUrl'. Please update the credential with a valid Teams Incoming Webhook URL.");

				if (!Uri.TryCreate(cred.WebhookUrl, UriKind.Absolute, out _))
					return WorkflowActionResult.Failed("Teams message failed.", $"The configured WebhookUrl '{cred.WebhookUrl}' is not a valid absolute URI.");

				// ── Webhook domain validation ────────────────────────────────────────
				var (webhookValid, webhookReason) = WebhookUrlValidator.Validate(cred.WebhookUrl, "teams");
				if (!webhookValid)
					return WorkflowActionResult.Failed("Teams message blocked.", webhookReason);
				// ── End webhook domain validation ────────────────────────────────────

				// Try to detect if rendered content is Adaptive Card JSON
				object payload;
				var trimmed = context.RenderedContent?.Trim();
				if (trimmed?.StartsWith("{") == true && trimmed.Contains("\"type\""))
				{
					// Adaptive Card wrapper
					payload = new
					{
						type = "message",
						attachments = new[] { new { contentType = "application/vnd.microsoft.card.adaptive", content = JsonConvert.DeserializeObject(trimmed) } }
					};
				}
				else
				{
					// Simple MessageCard: Teams collapses single \n in the text field, so
					// normalise line endings to \n\n paragraph breaks so that newlines
					// entered in the template editor are visible in the delivered message.
					var rawContent = context.RenderedContent ?? string.Empty;
					var teamsText = rawContent
						.Replace("\r\n", "\n")
						.Replace("\r", "\n")
						.Replace("\n", "\n\n");

					payload = new
					{
						type = "MessageCard",
						context = "https://schema.org/extensions",
						summary = config.Title ?? "Resgrid Workflow Notification",
						themeColor = config.ThemeColor ?? "0076D7",
						title = config.Title ?? string.Empty,
						text = teamsText
					};
				}

				using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
				var json = JsonConvert.SerializeObject(payload);
				var response = await client.PostAsync(cred.WebhookUrl, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
				var body = await response.Content.ReadAsStringAsync(cancellationToken);

				if (response.IsSuccessStatusCode)
					return WorkflowActionResult.Succeeded($"Teams message sent. Response: {body}");
				return WorkflowActionResult.Failed($"Teams webhook returned {(int)response.StatusCode}", body);
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("Teams message failed.", ex.Message);
			}
		}
	}

	public class TeamsActionConfig { public string Title { get; set; } public string ThemeColor { get; set; } = "0076D7"; }
	public class WebhookCredential { public string WebhookUrl { get; set; } }
}

