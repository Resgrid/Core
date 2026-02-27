using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Box.V2;
using Box.V2.Config;
using Box.V2.JWTAuth;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class BoxFileExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.UploadFileBox;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<BoxActionConfig>(context.ActionConfigJson ?? "{}") ?? new BoxActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("Box upload failed.", "No Box credential is attached to this workflow step. Please assign a Box credential.");

				var cred = JsonConvert.DeserializeObject<BoxCredential>(context.DecryptedCredentialJson) ?? new BoxCredential();

				if (string.IsNullOrWhiteSpace(cred.ClientId))
					return WorkflowActionResult.Failed("Box upload failed.", "Box credential is missing 'ClientId'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.ClientSecret))
					return WorkflowActionResult.Failed("Box upload failed.", "Box credential is missing 'ClientSecret'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.EnterpriseId))
					return WorkflowActionResult.Failed("Box upload failed.", "Box credential is missing 'EnterpriseId'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.PrivateKey))
					return WorkflowActionResult.Failed("Box upload failed.", "Box credential is missing 'PrivateKey'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.PublicKeyId))
					return WorkflowActionResult.Failed("Box upload failed.", "Box credential is missing 'PublicKeyId'. Please update the credential.");

				var filename = string.IsNullOrWhiteSpace(config.Filename)
					? $"workflow_{DateTime.UtcNow:yyyyMMddHHmmss}.txt"
					: config.Filename;
				var folderId = string.IsNullOrWhiteSpace(config.FolderId) ? "0" : config.FolderId;

				var boxConfig = new BoxConfigBuilder(cred.ClientId, cred.ClientSecret, cred.EnterpriseId,
					cred.PrivateKey, cred.PrivateKeyPassword, cred.PublicKeyId).Build();
				var session = new BoxJWTAuth(boxConfig);
				var adminToken = await session.AdminTokenAsync();
				var client = session.AdminClient(adminToken);

				var bytes = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
				using var stream = new MemoryStream(bytes);
				var file = await client.FilesManager.UploadAsync(
					new Box.V2.Models.BoxFileRequest { Name = filename, Parent = new Box.V2.Models.BoxRequestEntity { Id = folderId } },
					stream);

				return WorkflowActionResult.Succeeded($"Box upload succeeded. File ID: {file.Id}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("Box upload failed.", ex.Message);
			}
		}
	}

	public class BoxActionConfig { public string FolderId { get; set; } = "0"; public string Filename { get; set; } }
	public class BoxCredential
	{
		[JsonProperty("clientId")]
		public string ClientId { get; set; }

		[JsonProperty("clientSecret")]
		public string ClientSecret { get; set; }

		[JsonProperty("enterpriseId")]
		public string EnterpriseId { get; set; }

		[JsonProperty("privateKey")]
		public string PrivateKey { get; set; }

		[JsonProperty("privateKeyPassword")]
		public string PrivateKeyPassword { get; set; }

		[JsonProperty("publicKeyId")]
		public string PublicKeyId { get; set; }
	}
}

