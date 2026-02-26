using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ganss.Xss;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class SmtpEmailExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.SendEmail;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<SmtpActionConfig>(context.ActionConfigJson ?? "{}") ?? new SmtpActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("SMTP send failed.", "No SMTP credential is attached to this workflow step. Please assign an SMTP credential.");

				var cred = JsonConvert.DeserializeObject<SmtpCredential>(context.DecryptedCredentialJson) ?? new SmtpCredential();

				if (string.IsNullOrWhiteSpace(cred.FromAddress))
					return WorkflowActionResult.Failed("SMTP send failed.", "SMTP credential is missing a 'FromAddress'. Please update the credential with a valid sender email address.");

				if (string.IsNullOrWhiteSpace(cred.Host))
					return WorkflowActionResult.Failed("SMTP send failed.", "SMTP credential is missing a 'Host'. Please update the credential with a valid SMTP server hostname.");

				if (string.IsNullOrWhiteSpace(config.To))
					return WorkflowActionResult.Failed("SMTP send failed.", "No recipient address configured in the workflow step action config. Please set the 'To' field.");

				// ── Recipient cap enforcement ────────────────────────────────────────
				var toAddresses = config.To
					.Split(',', StringSplitOptions.RemoveEmptyEntries)
					.Select(a => a.Trim())
					.Where(a => !string.IsNullOrWhiteSpace(a))
					.ToList();

				var ccAddresses = (config.Cc ?? string.Empty)
					.Split(',', StringSplitOptions.RemoveEmptyEntries)
					.Select(a => a.Trim())
					.Where(a => !string.IsNullOrWhiteSpace(a))
					.ToList();

				if (context.IsFreePlanDepartment)
				{
					// Free plan: exactly 1 To recipient, no Cc allowed
					if (toAddresses.Count > 1)
						toAddresses = toAddresses.Take(1).ToList();
					ccAddresses.Clear();
				}
				else
				{
					// Paid plan: cap total recipients at configured maximum
					var maxRecipients = WorkflowConfig.MaxEmailRecipients;
					if (toAddresses.Count + ccAddresses.Count > maxRecipients)
					{
						// Trim to fit within cap, prioritising To over Cc
						var remainingForCc = Math.Max(0, maxRecipients - toAddresses.Count);
						toAddresses = toAddresses.Take(maxRecipients).ToList();
						ccAddresses = ccAddresses.Take(remainingForCc).ToList();
					}
				}
				// ── End recipient cap ────────────────────────────────────────────────

				var message = new MimeMessage();
				message.From.Add(MailboxAddress.Parse(cred.FromAddress));
				foreach (var to in toAddresses)
					message.To.Add(MailboxAddress.Parse(to));
				foreach (var cc in ccAddresses)
					message.Cc.Add(MailboxAddress.Parse(cc));
				message.Subject = config.Subject ?? string.Empty;

				// ── HTML sanitization ────────────────────────────────────────────────
				var sanitizer = new HtmlSanitizer();
				var sanitizedHtml = sanitizer.Sanitize(context.RenderedContent ?? string.Empty);
				// ── End HTML sanitization ────────────────────────────────────────────

				var bodyBuilder = new BodyBuilder { HtmlBody = sanitizedHtml };
				message.Body = bodyBuilder.ToMessageBody();

				using var smtp = new SmtpClient();
				var secureOption = cred.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
				await smtp.ConnectAsync(cred.Host, cred.Port > 0 ? cred.Port : 587, secureOption, cancellationToken);
				if (!string.IsNullOrWhiteSpace(cred.Username))
					await smtp.AuthenticateAsync(cred.Username, cred.Password, cancellationToken);
				await smtp.SendAsync(message, cancellationToken);
				await smtp.DisconnectAsync(true, cancellationToken);

				return WorkflowActionResult.Succeeded($"Email sent to {string.Join(", ", toAddresses)}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("SMTP send failed.", ex.Message);
			}
		}
	}

	public class SmtpActionConfig
	{
		public string To { get; set; }
		public string Cc { get; set; }
		public string Subject { get; set; }
	}

	public class SmtpCredential
	{
		[JsonProperty("host")]
		public string Host { get; set; }

		[JsonProperty("port")]
		public int Port { get; set; } = 587;

		[JsonProperty("username")]
		public string Username { get; set; }

		[JsonProperty("password")]
		public string Password { get; set; }

		[JsonProperty("useSsl")]
		public bool UseSsl { get; set; } = true;

		[JsonProperty("fromAddress")]
		public string FromAddress { get; set; }
	}
}

