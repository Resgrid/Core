using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// RESGRID-WEBJOBS-76: a failed department deletion wrote the full SQL exception message into
	/// QueueItems.Data (nvarchar(255)), so the retry update itself failed with "String or binary data would
	/// be truncated", the attempt count was never persisted and the item retried forever.
	/// </summary>
	[TestFixture]
	public class DepartmentDeletionRetryStateTests
	{
		private static readonly string LongFailure = "The DELETE statement conflicted with the REFERENCE constraint \"FK_Workshifts_Department\". "
			+ new string('x', 400);

		[TestCase(0, false)]
		[TestCase(4, true)]
		public async Task FailedDeletionPersistsAttemptStateThatFitsTheQueueItemDataColumn(int previousAttempts, bool terminal)
		{
			var item = new QueueItem
			{
				QueueItemId = 21794, SourceId = "11261", QueuedByUserId = "requesting-user",
				QueuedOn = DateTime.UtcNow.AddDays(-31), ToBeCompletedOn = DateTime.UtcNow.AddDays(-1), AttemptCount = previousAttempts
			};
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserDeleteDepartmentAsync("requesting-user", 11261)).ReturnsAsync(true);
			var deletes = new Mock<IDeleteRepository>();
			deletes.Setup(d => d.DeleteDepartmentAndUsersAsync(11261)).ThrowsAsync(new InvalidOperationException(LongFailure));
			var queue = new Mock<IQueueService>();
			string persistedData = null;
			queue.Setup(q => q.UpdateQueueItem(It.IsAny<QueueItem>(), It.IsAny<CancellationToken>()))
				.Callback<QueueItem, CancellationToken>((q, _) => persistedData = q.Data)
				.ReturnsAsync((QueueItem q, CancellationToken _) => q);

			var result = await DeleteService(authorization.Object, queue.Object, deletes.Object).HandlePendingDepartmentDeletionRequestAsync(item);

			result.Should().Be(DeleteDepartmentResults.Failure);
			queue.Verify(q => q.UpdateQueueItem(item, It.IsAny<CancellationToken>()), Times.Once);
			persistedData.Should().NotBeNull().And.StartWith("Department deletion");
			persistedData.Length.Should().BeLessThanOrEqualTo(255);
			item.AttemptCount.Should().Be(previousAttempts + 1);
			(item.CompletedOn != null).Should().Be(terminal);
		}

		[Test]
		public async Task ALegalHoldDefersTheDeletionWithoutSpendingAnAttempt()
		{
			var item = new QueueItem
			{
				QueueItemId = 21795, SourceId = "13275", QueuedByUserId = "requesting-user",
				QueuedOn = DateTime.UtcNow.AddDays(-31), ToBeCompletedOn = DateTime.UtcNow.AddDays(-1), AttemptCount = 4
			};
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserDeleteDepartmentAsync("requesting-user", 13275)).ReturnsAsync(true);
			var deletes = new Mock<IDeleteRepository>();
			deletes.Setup(d => d.DeleteDepartmentAndUsersAsync(13275)).ThrowsAsync(new DepartmentDeletionDeferredException("Department deletion is waiting for an active Records legal hold to be released."));
			var queue = new Mock<IQueueService>();
			queue.Setup(q => q.UpdateQueueItem(It.IsAny<QueueItem>(), It.IsAny<CancellationToken>())).ReturnsAsync((QueueItem q, CancellationToken _) => q);

			var result = await DeleteService(authorization.Object, queue.Object, deletes.Object).HandlePendingDepartmentDeletionRequestAsync(item);

			result.Should().Be(DeleteDepartmentResults.Failure);
			item.AttemptCount.Should().Be(4, "a hold is not a failure; the fifth attempt must stay available for real faults");
			item.CompletedOn.Should().BeNull("the request completes on the first poll after the hold is released");
			item.Data.Should().StartWith("Department deletion deferred").And.Subject.Length.Should().BeLessThanOrEqualTo(255);
			queue.Verify(q => q.UpdateQueueItem(item, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task TheRecordsImpactReportsActivationAndActiveHolds()
		{
			var cutovers = new Mock<IRmsDepartmentCutoversRepository>();
			cutovers.Setup(c => c.GetByDepartmentIdAsync(13275)).ReturnsAsync(new RmsDepartmentCutover { DepartmentId = 13275 });
			var holds = new Mock<IRmsRecordLegalHoldsRepository>();
			holds.Setup(h => h.GetActiveForDepartmentAsync(13275)).ReturnsAsync(new[] { new RmsRecordLegalHold(), new RmsRecordLegalHold() });

			var impact = await DeleteService(Mock.Of<IAuthorizationService>(), Mock.Of<IQueueService>(), Mock.Of<IDeleteRepository>(), cutovers.Object, holds.Object)
				.GetDepartmentDeletionRecordsImpactAsync(13275);

			impact.HasRecords.Should().BeTrue();
			impact.ActiveLegalHolds.Should().Be(2);
		}

		[Test]
		public async Task TheLeftoverSweepPurgesEachDeletedDepartmentOnItsOwnAndCarriesOnPastAFailure()
		{
			var deletes = new Mock<IDeleteRepository>();
			deletes.Setup(d => d.GetDeletedDepartmentIdsWithLeftoversAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { 11, 12, 13 });
			deletes.Setup(d => d.PurgeDeletedDepartmentLeftoversAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync(DeletedDepartmentPurgeOutcome.Purged);
			deletes.Setup(d => d.PurgeDeletedDepartmentLeftoversAsync(12, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Transaction was deadlocked"));
			deletes.Setup(d => d.PurgeDeletedDepartmentLeftoversAsync(13, It.IsAny<CancellationToken>())).ReturnsAsync(DeletedDepartmentPurgeOutcome.LegalHold);

			var result = await DeleteService(Mock.Of<IAuthorizationService>(), Mock.Of<IQueueService>(), deletes.Object).PurgeDeletedDepartmentLeftoversAsync();

			result.DepartmentsFound.Should().Be(3);
			result.DepartmentsPurged.Should().Be(1);
			result.DepartmentsHeld.Should().Be(1, "a hold is not a failure");
			result.Errors.Should().Be(1);
			deletes.Verify(d => d.PurgeDeletedDepartmentLeftoversAsync(13, It.IsAny<CancellationToken>()), Times.Once, "one department's failure must not stop the others");
		}

		[Test]
		public async Task AFailedLeftoverDiscoveryIsReportedRatherThanThrown()
		{
			var deletes = new Mock<IDeleteRepository>();
			deletes.Setup(d => d.GetDeletedDepartmentIdsWithLeftoversAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("Execution Timeout Expired"));

			var result = await DeleteService(Mock.Of<IAuthorizationService>(), Mock.Of<IQueueService>(), deletes.Object).PurgeDeletedDepartmentLeftoversAsync();

			result.Errors.Should().Be(1);
			result.Message.Should().NotBeNullOrEmpty();
			deletes.Verify(d => d.PurgeDeletedDepartmentLeftoversAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private static DeleteService DeleteService(IAuthorizationService authorization, IQueueService queue, IDeleteRepository deletes,
			IRmsDepartmentCutoversRepository cutovers = null, IRmsRecordLegalHoldsRepository holds = null) => new DeleteService(
			authorization, Mock.Of<IDepartmentsService>(), Mock.Of<ICallsService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
			Mock.Of<IMessageService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IWorkLogsService>(), Mock.Of<IUserStateService>(), Mock.Of<IPersonnelRolesService>(),
			Mock.Of<IDistributionListsService>(), Mock.Of<IShiftsService>(), Mock.Of<IUnitsService>(), Mock.Of<ICertificationService>(), Mock.Of<ILogService>(),
			Mock.Of<IInventoryService>(), Mock.Of<IEventAggregator>(), Mock.Of<IAddressService>(), queue, Mock.Of<IEmailService>(), deletes,
			Mock.Of<IAuditLogsRepository>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IUserSessionService>(),
			Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentMemberEmergencyContactService>(), rmsCutovers: cutovers, rmsLegalHolds: holds);
	}
}
