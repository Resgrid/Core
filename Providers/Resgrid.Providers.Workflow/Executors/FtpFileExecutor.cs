using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class FtpFileExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.UploadFileFtp;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)		{
			try
			{
				var config = JsonConvert.DeserializeObject<FtpActionConfig>(context.ActionConfigJson ?? "{}") ?? new FtpActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("FTP upload failed.", "No FTP credential is attached to this workflow step. Please assign an FTP credential.");

				var cred = JsonConvert.DeserializeObject<FtpCredential>(context.DecryptedCredentialJson) ?? new FtpCredential();

				if (string.IsNullOrWhiteSpace(cred.Host))
					return WorkflowActionResult.Failed("FTP upload failed.", "FTP credential is missing a 'Host'. Please update the credential with a valid FTP server hostname or IP address.");

				if (string.IsNullOrWhiteSpace(cred.Username))
					return WorkflowActionResult.Failed("FTP upload failed.", "FTP credential is missing a 'Username'. Please update the credential.");

				// ── SSRF protection ──────────────────────────────────────────────────
				var (ftpAllowed, ftpReason) = await SsrfGuard.ValidateHostAsync(cred.Host);
				if (!ftpAllowed)
					return WorkflowActionResult.Failed("FTP upload blocked.", ftpReason);
				// ── End SSRF protection ──────────────────────────────────────────────

				var remotePath = $"{config.RemotePath?.TrimEnd('/')}/{config.Filename ?? $"workflow_{DateTime.UtcNow:yyyyMMddHHmmss}.txt"}";

				using var client = new AsyncFtpClient(cred.Host, cred.Username, cred.Password, cred.Port > 0 ? cred.Port : 21);
				await client.Connect(cancellationToken);

				var bytes = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
				using var stream = new MemoryStream(bytes);
				await client.UploadStream(stream, remotePath, FtpRemoteExists.Overwrite, true,null, cancellationToken);
				await client.Disconnect(cancellationToken);

				return WorkflowActionResult.Succeeded($"FTP upload succeeded: {remotePath}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("FTP upload failed.", ex.Message);
			}
		}
	}

	public class FtpActionConfig { public string RemotePath { get; set; } = "/"; public string Filename { get; set; } }
	public class FtpCredential { public string Host { get; set; } public int Port { get; set; } = 21; public string Username { get; set; } public string Password { get; set; } }
}

