using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Newtonsoft.Json;
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

				var message = new MimeMessage();
				message.From.Add(MailboxAddress.Parse(cred.FromAddress));
				foreach (var to in config.To.Split(',', StringSplitOptions.RemoveEmptyEntries))
					message.To.Add(MailboxAddress.Parse(to.Trim()));
				if (!string.IsNullOrWhiteSpace(config.Cc))
					foreach (var cc in config.Cc.Split(',', StringSplitOptions.RemoveEmptyEntries))
						message.Cc.Add(MailboxAddress.Parse(cc.Trim()));
				message.Subject = config.Subject ?? string.Empty;

				var bodyBuilder = new BodyBuilder { HtmlBody = context.RenderedContent ?? string.Empty };
				message.Body = bodyBuilder.ToMessageBody();

				using var smtp = new SmtpClient();
				var secureOption = cred.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
				await smtp.ConnectAsync(cred.Host, cred.Port > 0 ? cred.Port : 587, secureOption, cancellationToken);
				if (!string.IsNullOrWhiteSpace(cred.Username))
					await smtp.AuthenticateAsync(cred.Username, cred.Password, cancellationToken);
				await smtp.SendAsync(message, cancellationToken);
				await smtp.DisconnectAsync(true, cancellationToken);

				return WorkflowActionResult.Succeeded($"Email sent to {config.To}");
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

