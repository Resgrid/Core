using System;
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

		private static DeleteService DeleteService(IAuthorizationService authorization, IQueueService queue, IDeleteRepository deletes) => new DeleteService(
			authorization, Mock.Of<IDepartmentsService>(), Mock.Of<ICallsService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
			Mock.Of<IMessageService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IWorkLogsService>(), Mock.Of<IUserStateService>(), Mock.Of<IPersonnelRolesService>(),
			Mock.Of<IDistributionListsService>(), Mock.Of<IShiftsService>(), Mock.Of<IUnitsService>(), Mock.Of<ICertificationService>(), Mock.Of<ILogService>(),
			Mock.Of<IInventoryService>(), Mock.Of<IEventAggregator>(), Mock.Of<IAddressService>(), queue, Mock.Of<IEmailService>(), deletes,
			Mock.Of<IAuditLogsRepository>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IUserSessionService>(),
			Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentMemberEmergencyContactService>());
	}
}
