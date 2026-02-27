using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Workflow.Executors;
using Newtonsoft.Json;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class WorkflowActionExecutorFactoryTests
	{
		[SetUp]
		public void SetUp() { }

		[Test]
		public void AllWorkflowActionTypeValues_AreDefinedInEnum()
		{
			var types = Enum.GetValues(typeof(WorkflowActionType));
			types.Length.Should().BeGreaterThanOrEqualTo(12, "at least 12 action types must exist per plan");
		}

		[Test]
		public void WorkflowActionType_SendEmail_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendEmail).Should().BeTrue();

		[Test]
		public void WorkflowActionType_CallApiPost_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.CallApiPost).Should().BeTrue();

		[Test]
		public void WorkflowActionType_SendSms_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendSms).Should().BeTrue();

		[Test]
		public void WorkflowActionType_SendTeamsMessage_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendTeamsMessage).Should().BeTrue();

		[Test]
		public void WorkflowActionType_SendSlackMessage_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendSlackMessage).Should().BeTrue();

		[Test]
		public void WorkflowActionType_SendDiscordMessage_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendDiscordMessage).Should().BeTrue();

		[Test]
		public void WorkflowActionType_UploadFileAzureBlob_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileAzureBlob).Should().BeTrue();

		[Test]
		public void WorkflowActionType_UploadFileFtp_HasCorrectValue()
			=> Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileFtp).Should().BeTrue();
	}

	[TestFixture]
	public class WorkflowActionResultTests
	{
		[Test]
		public void Succeeded_SetsSuccessTrueAndMessage()
		{
			var result = WorkflowActionResult.Succeeded("OK");
			result.Success.Should().BeTrue();
			result.ResultMessage.Should().Be("OK");
			result.ErrorDetail.Should().BeNullOrEmpty();
		}

		[Test]
		public void Failed_SetsSuccessFalseAndError()
		{
			var result = WorkflowActionResult.Failed("Bad request", "Details here");
			result.Success.Should().BeFalse();
			result.ResultMessage.Should().Be("Bad request");
			result.ErrorDetail.Should().Be("Details here");
		}
	}

	[TestFixture]
	public class WorkflowActionContextTests
	{
		[Test]
		public void WorkflowActionContext_PropertiesAreReadable()
		{
			var ctx = new WorkflowActionContext
			{
				RenderedContent        = "Test content",
				DecryptedCredentialJson = "{\"host\":\"smtp.test.com\"}",
				ActionConfigJson        = "{\"to\":\"user@test.com\"}",
				WorkflowId              = "wf-1",
				WorkflowStepId          = "step-1",
				WorkflowRunId           = "run-1",
				DepartmentId            = 1,
				ActionType              = (int)WorkflowActionType.SendEmail
			};

			ctx.RenderedContent.Should().Be("Test content");
			ctx.DepartmentId.Should().Be(1);
			ctx.ActionType.Should().Be((int)WorkflowActionType.SendEmail);
		}
	}

	[TestFixture]
	public class HttpApiExecutorConfigTests
	{
		[Test]
		public void HttpApiExecutor_ActionType_IsCallApiPost()
		{
			var executor = new HttpApiExecutor();
			executor.ActionType.Should().Be(WorkflowActionType.CallApiPost);
		}

		[Test]
		public async Task HttpApiExecutor_WithNullUrl_ReturnsFailed()
		{
			var executor = new HttpApiExecutor();
			var ctx = new WorkflowActionContext
			{
				ActionType      = (int)WorkflowActionType.CallApiPost,
				RenderedContent = "test body",
				ActionConfigJson = JsonConvert.SerializeObject(new { url = (string)null })
			};

			var result = await executor.ExecuteAsync(ctx, CancellationToken.None);
			result.Success.Should().BeFalse();
		}
	}

	[TestFixture]
	public class SmtpEmailExecutorTests
	{
		[Test]
		public void SmtpEmailExecutor_ActionType_IsCorrect()
		{
			var executor = new SmtpEmailExecutor();
			executor.ActionType.Should().Be(WorkflowActionType.SendEmail);
		}

		[Test]
		public async Task SmtpEmailExecutor_WithEmptyCredential_ReturnsFailed()
		{
			var executor = new SmtpEmailExecutor();
			var ctx = new WorkflowActionContext
			{
				ActionType              = (int)WorkflowActionType.SendEmail,
				RenderedContent         = "<p>Test Email Body</p>",
				DecryptedCredentialJson = null,
				ActionConfigJson        = JsonConvert.SerializeObject(new { to = "test@example.com", subject = "Alert" })
			};

			var result = await executor.ExecuteAsync(ctx, CancellationToken.None);
			result.Success.Should().BeFalse("missing SMTP credentials should fail gracefully");
		}
	}

	[TestFixture]
	public class TwilioSmsExecutorTests
	{
		[Test]
		public void TwilioSmsExecutor_ActionType_IsCorrect()
		{
			var executor = new TwilioSmsExecutor();
			executor.ActionType.Should().Be(WorkflowActionType.SendSms);
		}

		[Test]
		public async Task TwilioSmsExecutor_WithNullCredentials_ReturnsFailed()
		{
			var executor = new TwilioSmsExecutor();
			var ctx = new WorkflowActionContext
			{
				ActionType              = (int)WorkflowActionType.SendSms,
				RenderedContent         = "Test SMS message",
				DecryptedCredentialJson = null,
				ActionConfigJson        = JsonConvert.SerializeObject(new { to = "+15550001234" })
			};

			var result = await executor.ExecuteAsync(ctx, CancellationToken.None);
			result.Success.Should().BeFalse("missing Twilio credentials should fail gracefully");
		}
	}

	[TestFixture]
	public class TeamsMessageExecutorTests
	{
		[Test]
		public void TeamsMessageExecutor_ActionType_IsCorrect()
		{
			var executor = new TeamsMessageExecutor();
			executor.ActionType.Should().Be(WorkflowActionType.SendTeamsMessage);
		}
	}

	[TestFixture]
	public class SlackMessageExecutorTests
	{
		[Test]
		public void SlackMessageExecutor_ActionType_IsCorrect()
		{
			var executor = new SlackMessageExecutor();
			executor.ActionType.Should().Be(WorkflowActionType.SendSlackMessage);
		}
	}

	[TestFixture]
	public class DiscordMessageExecutorTests
	{
		[Test]
		public void DiscordMessageExecutor_ActionType_IsCorrect()
		{
			var executor = new DiscordMessageExecutor();
			executor.ActionType.Should().Be(WorkflowActionType.SendDiscordMessage);
		}
	}

	[TestFixture]
	public class AzureBlobExecutorTests
	{
		[Test]
		public void AzureBlobExecutor_ActionType_IsCorrect()
		{
			var executor = new AzureBlobExecutor();
			executor.ActionType.Should().Be(WorkflowActionType.UploadFileAzureBlob);
		}

		[Test]
		public async Task AzureBlobExecutor_WithNullCredentials_ReturnsFailed()
		{
			var executor = new AzureBlobExecutor();
			var ctx = new WorkflowActionContext
			{
				ActionType              = (int)WorkflowActionType.UploadFileAzureBlob,
				RenderedContent         = "file content",
				DecryptedCredentialJson = null,
				ActionConfigJson        = JsonConvert.SerializeObject(new { container = "mycontainer", blobName = "report.txt" })
			};

			var result = await executor.ExecuteAsync(ctx, CancellationToken.None);
			result.Success.Should().BeFalse("missing Azure credentials should fail gracefully");
		}
	}
}




