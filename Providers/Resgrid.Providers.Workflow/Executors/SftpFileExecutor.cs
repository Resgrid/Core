using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Renci.SshNet;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class SftpFileExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.UploadFileSftp;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<SftpActionConfig>(context.ActionConfigJson ?? "{}") ?? new SftpActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("SFTP upload failed.", "No SFTP credential is attached to this workflow step. Please assign an SFTP credential.");

				var cred = JsonConvert.DeserializeObject<SftpCredential>(context.DecryptedCredentialJson) ?? new SftpCredential();

				if (string.IsNullOrWhiteSpace(cred.Host))
					return WorkflowActionResult.Failed("SFTP upload failed.", "SFTP credential is missing a 'Host'. Please update the credential with a valid SFTP server hostname or IP address.");

				if (string.IsNullOrWhiteSpace(cred.Username))
					return WorkflowActionResult.Failed("SFTP upload failed.", "SFTP credential is missing a 'Username'. Please update the credential.");

				var filename = config.Filename ?? $"workflow_{DateTime.UtcNow:yyyyMMddHHmmss}.txt";
				var remotePath = $"{config.RemotePath?.TrimEnd('/')}/{filename}";

				ConnectionInfo connectionInfo;
				if (!string.IsNullOrWhiteSpace(cred.PrivateKey))
				{
					var keyBytes = Encoding.UTF8.GetBytes(cred.PrivateKey);
					using var keyStream = new MemoryStream(keyBytes);
					var privateKeyFile = string.IsNullOrWhiteSpace(cred.Password)
						? new PrivateKeyFile(keyStream)
						: new PrivateKeyFile(keyStream, cred.Password);

					connectionInfo = new ConnectionInfo(cred.Host, cred.Port > 0 ? cred.Port : 22, cred.Username,
						new PrivateKeyAuthenticationMethod(cred.Username, privateKeyFile));
				}
				else
				{
					connectionInfo = new ConnectionInfo(cred.Host, cred.Port > 0 ? cred.Port : 22, cred.Username,
						new PasswordAuthenticationMethod(cred.Username, cred.Password));
				}

				await Task.Run(() =>
				{
					using var client = new SftpClient(connectionInfo);
					client.Connect();
					var bytes = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
					using var stream = new MemoryStream(bytes);
					client.UploadFile(stream, remotePath, true);
					client.Disconnect();
				}, cancellationToken);

				return WorkflowActionResult.Succeeded($"SFTP upload succeeded: {remotePath}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("SFTP upload failed.", ex.Message);
			}
		}
	}

	public class SftpActionConfig { public string RemotePath { get; set; } = "/"; public string Filename { get; set; } }
	public class SftpCredential { public string Host { get; set; } public int Port { get; set; } = 22; public string Username { get; set; } public string Password { get; set; } public string PrivateKey { get; set; } }
}

