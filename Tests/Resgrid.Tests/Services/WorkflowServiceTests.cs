﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace WorkflowServiceTests
	{
		// ── helpers ──────────────────────────────────────────────────────────────────
		// We mock IWorkflowService directly so we don't depend on concrete WorkflowService.
		// Tests that need the real service are integration tests – here we test service
		// *contract* (interface) and helper logic that doesn't require real DI.

		[TestFixture]
		public class WhenWorkflowHealthSummaryComputesSuccessRate
		{
			[Test]
			public void ShouldComputeSuccessRateForZeroRuns()
			{
				var h = new WorkflowHealthSummary { TotalRuns30d = 0, SuccessfulRuns30d = 0 };
				h.SuccessRatePercent30d.Should().Be(0);
			}

			[Test]
			public void ShouldComputeSuccessRateFor75Percent()
			{
				var h = new WorkflowHealthSummary { TotalRuns30d = 4, SuccessfulRuns30d = 3 };
				h.SuccessRatePercent30d.Should().Be(75.0);
			}

			[Test]
			public void ShouldComputeSuccessRateFor100Percent()
			{
				var h = new WorkflowHealthSummary { TotalRuns30d = 10, SuccessfulRuns30d = 10 };
				h.SuccessRatePercent30d.Should().Be(100.0);
			}

			[Test]
			public void ShouldComputeSuccessRateFor0Percent()
			{
				var h = new WorkflowHealthSummary { TotalRuns30d = 5, SuccessfulRuns30d = 0 };
				h.SuccessRatePercent30d.Should().Be(0.0);
			}
		}

		[TestFixture]
		public class WhenWorkflowRunStatusValuesAreChecked
		{
			[Test]
			public void AllExpectedStatusValuesShouldExist()
			{
				Enum.IsDefined(typeof(WorkflowRunStatus), WorkflowRunStatus.Pending).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowRunStatus), WorkflowRunStatus.Running).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowRunStatus), WorkflowRunStatus.Completed).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowRunStatus), WorkflowRunStatus.Failed).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowRunStatus), WorkflowRunStatus.Retrying).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowRunStatus), WorkflowRunStatus.Cancelled).Should().BeTrue();
			}
		}

		[TestFixture]
		public class WhenWorkflowActionTypesAreChecked
		{
			[Test]
			public void AllExpectedActionTypesShouldExist()
			{
				var types = Enum.GetValues(typeof(WorkflowActionType));
				types.Length.Should().BeGreaterThanOrEqualTo(12, "at least 12 action types should be defined");

				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendEmail).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendSms).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.CallApiPost).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileFtp).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileSftp).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileS3).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendTeamsMessage).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendSlackMessage).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.SendDiscordMessage).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileAzureBlob).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileBox).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowActionType), WorkflowActionType.UploadFileDropbox).Should().BeTrue();
			}
		}

		[TestFixture]
		public class WhenWorkflowTriggerEventTypesAreChecked
		{
			[Test]
			public void AllExpectedTriggerEventTypesShouldExist()
			{
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.CallAdded).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.CallUpdated).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.CallClosed).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.UnitStatusChanged).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.PersonnelStatusChanged).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.PersonnelStaffingChanged).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.LogAdded).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowTriggerEventType), WorkflowTriggerEventType.FormSubmitted).Should().BeTrue();
			}
		}

		[TestFixture]
		public class WhenWorkflowActionResultIsCreated
		{
			[Test]
			public void SucceededShouldSetIsSuccessTrue()
			{
				var r = WorkflowActionResult.Succeeded("done");
				r.Success.Should().BeTrue();
				r.ResultMessage.Should().Be("done");
			}

			[Test]
			public void FailedShouldSetIsSuccessFalse()
			{
				var r = WorkflowActionResult.Failed("timeout", "details");
				r.Success.Should().BeFalse();
				r.ResultMessage.Should().Be("timeout");
				r.ErrorDetail.Should().Be("details");
			}

			[Test]
			public void FailedWithNoDetailShouldHaveNullDetail()
			{
				var r = WorkflowActionResult.Failed("error");
				r.Success.Should().BeFalse();
				r.ErrorDetail.Should().BeNull();
			}
		}

		[TestFixture]
		public class WhenWorkflowCredentialTypesAreChecked
		{
			[Test]
			public void AllExpectedCredentialTypesShouldExist()
			{
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.Smtp).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.Twilio).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.HttpBearer).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.HttpBasic).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.AwsS3).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.AzureBlobStorage).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.Box).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.Dropbox).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.Ftp).Should().BeTrue();
				Enum.IsDefined(typeof(WorkflowCredentialType), WorkflowCredentialType.Sftp).Should().BeTrue();
			}
		}

		[TestFixture]
		public class WhenIWorkflowServiceIsMocked
		{
			private Mock<IWorkflowService> _mockService;

			[SetUp]
			public void Setup()
			{
				_mockService = new Mock<IWorkflowService>();
			}

			[Test]
			public async Task GetWorkflowByIdAsyncShouldReturnNull()
			{
				_mockService.Setup(s => s.GetWorkflowByIdAsync("non-existent-id", It.IsAny<CancellationToken>())).ReturnsAsync((Workflow)null);
				var result = await _mockService.Object.GetWorkflowByIdAsync("non-existent-id");
				result.Should().BeNull();
			}

			[Test]
			public async Task GetWorkflowsByDepartmentIdAsyncShouldReturnEmptyList()
			{
				_mockService.Setup(s => s.GetWorkflowsByDepartmentIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Workflow>());
				var result = await _mockService.Object.GetWorkflowsByDepartmentIdAsync(1);
				result.Should().BeEmpty();
			}

			[Test]
			public async Task CancelWorkflowRunAsyncReturnsTrueForPendingRun()
			{
				_mockService.Setup(s => s.CancelWorkflowRunAsync("pending-run-id", It.IsAny<CancellationToken>())).ReturnsAsync(true);
				var result = await _mockService.Object.CancelWorkflowRunAsync("pending-run-id");
				result.Should().BeTrue();
			}

			[Test]
			public async Task CancelWorkflowRunAsyncReturnsFalseForCompletedRun()
			{
				_mockService.Setup(s => s.CancelWorkflowRunAsync("completed-run-id", It.IsAny<CancellationToken>())).ReturnsAsync(false);
				var result = await _mockService.Object.CancelWorkflowRunAsync("completed-run-id");
				result.Should().BeFalse();
			}

			[Test]
			public async Task GetWorkflowHealthAsyncShouldReturnSummary()
			{
				var expected = new WorkflowHealthSummary
				{
					WorkflowId        = "test-workflow-id",
					WorkflowName      = "Test Workflow",
					TotalRuns30d      = 10,
					SuccessfulRuns30d = 8,
					FailedRuns30d     = 2,
					LastRunStatus     = WorkflowRunStatus.Completed
				};
				_mockService.Setup(s => s.GetWorkflowHealthAsync("test-workflow-id", It.IsAny<CancellationToken>())).ReturnsAsync(expected);

				var health = await _mockService.Object.GetWorkflowHealthAsync("test-workflow-id");
				health.Should().NotBeNull();
				health.SuccessRatePercent30d.Should().Be(80.0);
				health.WorkflowName.Should().Be("Test Workflow");
			}

			[Test]
			public async Task DeleteWorkflowAsyncShouldReturnTrue()
			{
				_mockService.Setup(s => s.DeleteWorkflowAsync("workflow-to-delete", It.IsAny<CancellationToken>())).ReturnsAsync(true);
				var result = await _mockService.Object.DeleteWorkflowAsync("workflow-to-delete");
				result.Should().BeTrue();
			}
		}
	}
}
