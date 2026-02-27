using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class DropboxFileExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.UploadFileDropbox;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<DropboxActionConfig>(context.ActionConfigJson ?? "{}") ?? new DropboxActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("Dropbox upload failed.", "No Dropbox credential is attached to this workflow step. Please assign a Dropbox credential.");

				var cred = JsonConvert.DeserializeObject<DropboxCredential>(context.DecryptedCredentialJson) ?? new DropboxCredential();

				if (string.IsNullOrWhiteSpace(cred.RefreshToken))
					return WorkflowActionResult.Failed("Dropbox upload failed.", "Dropbox credential is missing 'RefreshToken'. Please update the credential with a valid OAuth2 refresh token.");

				if (string.IsNullOrWhiteSpace(cred.AppKey))
					return WorkflowActionResult.Failed("Dropbox upload failed.", "Dropbox credential is missing 'AppKey'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.AppSecret))
					return WorkflowActionResult.Failed("Dropbox upload failed.", "Dropbox credential is missing 'AppSecret'. Please update the credential.");

				var filename = string.IsNullOrWhiteSpace(config.Filename)
					? $"workflow_{DateTime.UtcNow:yyyyMMddHHmmss}.txt"
					: config.Filename;
				var targetPath = $"{config.TargetPath?.TrimEnd('/')}/{filename}";

				WriteMode writeMode = config.WriteMode?.ToLowerInvariant() == "overwrite"
					? WriteMode.Overwrite.Instance
					: WriteMode.Add.Instance;

				using var client = new DropboxClient(cred.RefreshToken,
					appKey: cred.AppKey, appSecret: cred.AppSecret,
					new DropboxClientConfig("ResgridWorkflow/1.0"));

				var bytes = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
				using var stream = new MemoryStream(bytes);
				var metadata = await client.Files.UploadAsync(targetPath, writeMode, body: stream);

				return WorkflowActionResult.Succeeded($"Dropbox upload succeeded: {metadata.PathDisplay} (rev: {metadata.Rev})");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("Dropbox upload failed.", ex.Message);
			}
		}
	}

	public class DropboxActionConfig { public string TargetPath { get; set; } = "/"; public string Filename { get; set; } public string WriteMode { get; set; } = "overwrite"; }
	public class DropboxCredential
	{
		[JsonProperty("refreshToken")]
		public string RefreshToken { get; set; }

		[JsonProperty("appKey")]
		public string AppKey { get; set; }

		[JsonProperty("appSecret")]
		public string AppSecret { get; set; }
	}
}

