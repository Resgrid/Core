using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace Resgrid.Providers.Workflow.Executors
{
	public class TwilioSmsExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.SendSms;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<SmsActionConfig>(context.ActionConfigJson ?? "{}") ?? new SmsActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("Twilio SMS failed.", "No Twilio credential is attached to this workflow step. Please assign a Twilio credential.");

				var cred = JsonConvert.DeserializeObject<TwilioCredential>(context.DecryptedCredentialJson) ?? new TwilioCredential();

				if (string.IsNullOrWhiteSpace(cred.AccountSid))
					return WorkflowActionResult.Failed("Twilio SMS failed.", "Twilio credential is missing 'AccountSid'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.AuthToken))
					return WorkflowActionResult.Failed("Twilio SMS failed.", "Twilio credential is missing 'AuthToken'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.FromNumber))
					return WorkflowActionResult.Failed("Twilio SMS failed.", "Twilio credential is missing 'FromNumber'. Please update the credential with a valid Twilio phone number.");

				if (string.IsNullOrWhiteSpace(config.To))
					return WorkflowActionResult.Failed("Twilio SMS failed.", "No recipient phone number configured in the workflow step action config. Please set the 'To' field.");

				if (string.IsNullOrWhiteSpace(context.RenderedContent))
					return WorkflowActionResult.Failed("Twilio SMS failed.", "The message body is empty. Please ensure the workflow step has a message template that produces content.");

				// ── Recipient cap enforcement ────────────────────────────────────────
				var recipients = config.To
					.Split(',', StringSplitOptions.RemoveEmptyEntries)
					.Select(n => n.Trim())
					.Where(n => !string.IsNullOrWhiteSpace(n))
					.ToList();

				int maxRecipients = context.IsFreePlanDepartment ? 1 : WorkflowConfig.MaxSmsRecipients;
				if (recipients.Count > maxRecipients)
					recipients = recipients.Take(maxRecipients).ToList();
				// ── End recipient cap ────────────────────────────────────────────────

				TwilioClient.Init(cred.AccountSid, cred.AuthToken);

				string lastSid = null;
				foreach (var recipient in recipients)
				{
					var message = await MessageResource.CreateAsync(
						body: context.RenderedContent,
						from: new Twilio.Types.PhoneNumber(cred.FromNumber),
						to: new Twilio.Types.PhoneNumber(recipient));
					lastSid = message.Sid;
				}

				return WorkflowActionResult.Succeeded($"SMS sent to {recipients.Count} recipient(s). Last SID: {lastSid}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("Twilio SMS failed.", ex.Message);
			}
		}
	}

	public class SmsActionConfig { public string To { get; set; } }

	public class TwilioCredential
	{
		public string AccountSid { get; set; }
		public string AuthToken { get; set; }
		public string FromNumber { get; set; }
	}
}
