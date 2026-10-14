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
	/// A deleted department's phones and browsers are never told it is gone, so the deletion drops every Novu
	/// subscriber it was keyed by. The subscribers can only be named before the purge, and may only be dropped once
	/// the purge has gone through: a deferred or failed deletion leaves the department, whose people still need pushes.
	/// </summary>
	[TestFixture]
	public class DepartmentDeletionPushSubscriberTests
	{
		private const int DepartmentId = 11261;
		private const string Code = "DEPT";
		private const string RequestingUser = "requesting-user";

		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUnitsService> _units;
		private Mock<IQueueService> _queue;
		private Mock<IDeleteRepository> _deletes;
		private Mock<INovuProvider> _novu;
		private QueueItem _item;
		private List<string> _order;

		[SetUp]
		public void SetUp()
		{
			_order = new List<string>();
			_item = new QueueItem
			{
				QueueItemId = 9001, SourceId = DepartmentId.ToString(), QueuedByUserId = RequestingUser,
				QueuedOn = DateTime.UtcNow.AddDays(-31), ToBeCompletedOn = DateTime.UtcNow.AddDays(-1)
			};

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(a => a.CanUserDeleteDepartmentAsync(RequestingUser, DepartmentId)).ReturnsAsync(true);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = Code });
			_departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { UserId = "user-a" },
				new DepartmentMember { UserId = "user-b", IsDeleted = true },
				new DepartmentMember { UserId = "USER-A" },
				new DepartmentMember { UserId = null }
			});

			_units = new Mock<IUnitsService>();
			_units.Setup(u => u.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 7, DepartmentId = DepartmentId },
				new Unit { UnitId = 8, DepartmentId = DepartmentId, IsDeleted = true }
			});

			_queue = new Mock<IQueueService>();
			_queue.Setup(q => q.UpdateQueueItem(It.IsAny<QueueItem>(), It.IsAny<CancellationToken>()))
				.Callback(() => Record("queue-updated"))
				.ReturnsAsync((QueueItem q, CancellationToken _) => q);

			_deletes = new Mock<IDeleteRepository>();
			_deletes.Setup(d => d.DeleteDepartmentAndUsersAsync(DepartmentId)).Callback(() => Record("purged")).ReturnsAsync(true);

			_novu = new Mock<INovuProvider>();
			_novu.Setup(n => n.DeleteUserSubscriber(It.IsAny<string>(), Code)).Callback(() => Record("novu")).ReturnsAsync(true);
			_novu.Setup(n => n.DeleteICUserSubscriber(It.IsAny<string>(), Code)).Callback(() => Record("novu")).ReturnsAsync(true);
			_novu.Setup(n => n.DeleteUnitSubscriber(It.IsAny<int>(), Code)).Callback(() => Record("novu")).ReturnsAsync(true);
		}

		[Test]
		public async Task A_completed_deletion_drops_every_member_and_unit_subscriber()
		{
			var result = await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			result.Should().Be(DeleteDepartmentResults.NoFailure);
			_novu.Verify(n => n.DeleteUserSubscriber("user-a", Code), Times.Once);
			_novu.Verify(n => n.DeleteICUserSubscriber("user-a", Code), Times.Once);
			_novu.Verify(n => n.DeleteUserSubscriber("user-b", Code), Times.Once, "a removed member's subscriber can still hold a phone");
			_novu.Verify(n => n.DeleteICUserSubscriber("user-b", Code), Times.Once);
			_novu.Verify(n => n.DeleteUnitSubscriber(7, Code), Times.Once);
			_novu.Verify(n => n.DeleteUnitSubscriber(8, Code), Times.Once, "a deleted unit's subscriber is dropped too");
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Subscribers_are_dropped_only_after_the_purge_is_recorded_as_done()
		{
			await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			_order.IndexOf("purged").Should().BeLessThan(_order.IndexOf("queue-updated"));
			_order.IndexOf("queue-updated").Should().BeLessThan(_order.IndexOf("novu"));
			_departments.Verify(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId), Times.Once);
		}

		[Test]
		public async Task A_failed_purge_leaves_every_subscriber()
		{
			_deletes.Setup(d => d.DeleteDepartmentAndUsersAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("Transaction was deadlocked"));

			var result = await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			result.Should().Be(DeleteDepartmentResults.Failure);
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_deferred_purge_leaves_every_subscriber()
		{
			_deletes.Setup(d => d.DeleteDepartmentAndUsersAsync(DepartmentId))
				.ThrowsAsync(new DepartmentDeletionDeferredException("Department deletion is waiting for an active Records legal hold to be released."));

			await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_novu_failure_does_not_turn_a_completed_deletion_into_a_retry()
		{
			_novu.Setup(n => n.DeleteUserSubscriber(It.IsAny<string>(), Code)).ReturnsAsync(false);
			_novu.Setup(n => n.DeleteUnitSubscriber(7, Code)).ThrowsAsync(new TimeoutException());

			var result = await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			result.Should().Be(DeleteDepartmentResults.NoFailure);
			_item.CompletedOn.Should().NotBeNull();
			_item.AttemptCount.Should().Be(0);
			_novu.Verify(n => n.DeleteUnitSubscriber(8, Code), Times.Once, "one failure must not stop the others");
		}

		[Test]
		public async Task A_failed_subscriber_read_still_deletes_the_department()
		{
			_departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId)).ThrowsAsync(new TimeoutException());

			var result = await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			result.Should().Be(DeleteDepartmentResults.NoFailure);
			_deletes.Verify(d => d.DeleteDepartmentAndUsersAsync(DepartmentId), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_department_without_a_code_has_no_subscribers_to_drop()
		{
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = " " });

			await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			_deletes.Verify(d => d.DeleteDepartmentAndUsersAsync(DepartmentId), Times.Once);
			_novu.VerifyNoOtherCalls();
		}

		// The subscriber deletes run in parallel, so their callbacks can land together.
		private void Record(string step)
		{
			lock (_order)
				_order.Add(step);
		}

		private DeleteService Service() => new DeleteService(
			_authorization.Object, _departments.Object, Mock.Of<ICallsService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
			Mock.Of<IMessageService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IWorkLogsService>(), Mock.Of<IUserStateService>(), Mock.Of<IPersonnelRolesService>(),
			Mock.Of<IDistributionListsService>(), Mock.Of<IShiftsService>(), _units.Object, Mock.Of<ICertificationService>(), Mock.Of<ILogService>(),
			Mock.Of<IInventoryService>(), Mock.Of<IEventAggregator>(), Mock.Of<IAddressService>(), _queue.Object, Mock.Of<IEmailService>(), _deletes.Object,
			Mock.Of<IAuditLogsRepository>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IUserSessionService>(),
			Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentMemberEmergencyContactService>(), novuProvider: _novu.Object);
	}
}
