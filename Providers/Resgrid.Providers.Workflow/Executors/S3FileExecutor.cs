using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Transfer;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class S3FileExecutor : IWorkflowActionExecutor
	{
		public WorkflowActionType ActionType => WorkflowActionType.UploadFileS3;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			try
			{
				var config = JsonConvert.DeserializeObject<S3ActionConfig>(context.ActionConfigJson ?? "{}") ?? new S3ActionConfig();

				if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
					return WorkflowActionResult.Failed("S3 upload failed.", "No S3 credential is attached to this workflow step. Please assign an S3 credential.");

				var cred = JsonConvert.DeserializeObject<S3Credential>(context.DecryptedCredentialJson) ?? new S3Credential();

				if (string.IsNullOrWhiteSpace(cred.AccessKey))
					return WorkflowActionResult.Failed("S3 upload failed.", "S3 credential is missing 'AccessKey'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.SecretKey))
					return WorkflowActionResult.Failed("S3 upload failed.", "S3 credential is missing 'SecretKey'. Please update the credential.");

				if (string.IsNullOrWhiteSpace(cred.BucketName))
					return WorkflowActionResult.Failed("S3 upload failed.", "S3 credential is missing 'BucketName'. Please update the credential with the target S3 bucket name.");

				var key = string.IsNullOrWhiteSpace(config.S3Key)
					? $"workflow/{DateTime.UtcNow:yyyy/MM/dd}/{DateTime.UtcNow:HHmmss}.txt"
					: config.S3Key;

				var awsCreds = new BasicAWSCredentials(cred.AccessKey, cred.SecretKey);
				var region = RegionEndpoint.GetBySystemName(string.IsNullOrWhiteSpace(cred.Region) ? "us-east-1" : cred.Region);

				using var s3Client = new AmazonS3Client(awsCreds, region);
				var bytes = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
				using var stream = new MemoryStream(bytes);

				var uploadRequest = new TransferUtilityUploadRequest
				{
					BucketName = cred.BucketName,
					Key = key,
					InputStream = stream,
					ContentType = string.IsNullOrWhiteSpace(config.ContentType) ? "text/plain" : config.ContentType
				};

				var transferUtility = new TransferUtility(s3Client);
				await transferUtility.UploadAsync(uploadRequest, cancellationToken);

				return WorkflowActionResult.Succeeded($"S3 upload succeeded: s3://{cred.BucketName}/{key}");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("S3 upload failed.", ex.Message);
			}
		}
	}

	public class S3ActionConfig { public string S3Key { get; set; } public string ContentType { get; set; } }
	public class S3Credential { public string AccessKey { get; set; } public string SecretKey { get; set; } public string Region { get; set; } = "us-east-1"; public string BucketName { get; set; } }
}

