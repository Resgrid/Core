using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class AzureBlobExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.UploadFileAzureBlob;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<AzureBlobActionConfig>(context.ActionConfigJson ?? "{}") ?? new AzureBlobActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("Azure Blob upload failed.", "No Azure Blob credential is attached to this workflow step. Please assign an Azure Blob credential.");

				var cred = JsonConvert.DeserializeObject<AzureBlobCredential>(context.DecryptedCredentialJson) ?? new AzureBlobCredential();

				if (string.IsNullOrWhiteSpace(cred.ConnectionString))
					return WorkflowActionResult.Failed("Azure Blob upload failed.", "Azure Blob credential is missing a 'ConnectionString'. Please update the credential with a valid Azure Storage connection string.");

				if (string.IsNullOrWhiteSpace(cred.ContainerName))
					return WorkflowActionResult.Failed("Azure Blob upload failed.", "Azure Blob credential is missing a 'ContainerName'. Please update the credential with the target container name.");

				var blobName = string.IsNullOrWhiteSpace(config.BlobName)
					? $"workflow/{DateTime.UtcNow:yyyy/MM/dd}/{DateTime.UtcNow:HHmmss}.txt"
					: config.BlobName;

				var serviceClient = new BlobServiceClient(cred.ConnectionString);
				var containerClient = serviceClient.GetBlobContainerClient(cred.ContainerName);
				await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

				var blobClient = containerClient.GetBlobClient(blobName);
				var bytes = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
				using var stream = new MemoryStream(bytes);

				var headers = new BlobHttpHeaders
				{
					ContentType = string.IsNullOrWhiteSpace(config.ContentType) ? "text/plain" : config.ContentType
				};
				await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken);

				return WorkflowActionResult.Succeeded($"Azure Blob upload succeeded: {blobClient.Uri}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("Azure Blob upload failed.", ex.Message);
			}
		}
	}

	public class AzureBlobActionConfig { public string BlobName { get; set; } public string ContentType { get; set; } }
	public class AzureBlobCredential { public string ConnectionString { get; set; } public string ContainerName { get; set; } }
}

