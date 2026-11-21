using System;
using System.Collections.Generic;
using System.Linq;
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
	/// A deleted department's dedicated numbers are named before the purge (it deletes the only record of their carrier ids) and handed
	/// back to the carrier only once the purge went through: a deferred or failed deletion keeps the department and its number.
	/// </summary>
	[TestFixture]
	public class DepartmentDeletionDedicatedNumberTests
	{
		private const int DepartmentId = 11262;
		private const string RequestingUser = "requesting-user";

		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IQueueService> _queue;
		private Mock<IDeleteRepository> _deletes;
		private Mock<IDepartmentPhoneNumbersService> _numbers;
		private QueueItem _item;
		private List<string> _order;
		private List<DepartmentPhoneNumber> _held;

		[SetUp]
		public void SetUp()
		{
			_order = new List<string>();
			_item = new QueueItem
			{
				QueueItemId = 9002, SourceId = DepartmentId.ToString(), QueuedByUserId = RequestingUser,
				QueuedOn = DateTime.UtcNow.AddDays(-31), ToBeCompletedOn = DateTime.UtcNow.AddDays(-1)
			};

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(a => a.CanUserDeleteDepartmentAsync(RequestingUser, DepartmentId)).ReturnsAsync(true);
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			_queue = new Mock<IQueueService>();
			_queue.Setup(q => q.UpdateQueueItem(It.IsAny<QueueItem>(), It.IsAny<CancellationToken>())).ReturnsAsync((QueueItem q, CancellationToken _) => q);
			_deletes = new Mock<IDeleteRepository>();
			_deletes.Setup(d => d.DeleteDepartmentAndUsersAsync(DepartmentId)).Callback(() => _order.Add("purged")).ReturnsAsync(true);

			_held = new List<DepartmentPhoneNumber>
			{
				new DepartmentPhoneNumber { DepartmentPhoneNumberId = "a", DepartmentId = DepartmentId, PhoneNumber = "+17755550100", ProviderNumberId = "PN1", Status = (int)DepartmentPhoneNumberStatuses.Active },
				new DepartmentPhoneNumber { DepartmentPhoneNumberId = "b", DepartmentId = DepartmentId, PhoneNumber = "+17755550101", ProviderNumberId = "PN2", Status = (int)DepartmentPhoneNumberStatuses.Released },
			};
			_numbers = new Mock<IDepartmentPhoneNumbersService>();
			_numbers.Setup(n => n.GetAllForDepartmentAsync(DepartmentId)).Callback(() => _order.Add("read")).ReturnsAsync(_held);
			_numbers.Setup(n => n.ReleaseAtCarrierAsync(It.IsAny<IReadOnlyCollection<DepartmentPhoneNumber>>(), It.IsAny<CancellationToken>()))
				.Callback(() => _order.Add("released")).ReturnsAsync(1);
		}

		[Test]
		public async Task Held_numbers_are_read_before_the_purge_and_released_after_it()
		{
			var result = await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			result.Should().Be(DeleteDepartmentResults.NoFailure);
			_order.Should().Equal("read", "purged", "released");
			_numbers.Verify(n => n.ReleaseAtCarrierAsync(It.Is<IReadOnlyCollection<DepartmentPhoneNumber>>(list => list.Single().ProviderNumberId == "PN1"),
				CancellationToken.None), Times.Once, "only numbers still held at the carrier are released, and a stopping worker cannot cut it short");
		}

		[Test]
		public async Task A_deferred_purge_keeps_the_department_number()
		{
			_deletes.Setup(d => d.DeleteDepartmentAndUsersAsync(DepartmentId))
				.ThrowsAsync(new DepartmentDeletionDeferredException("Department deletion is waiting for an active Records legal hold to be released."));

			await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			_numbers.Verify(n => n.ReleaseAtCarrierAsync(It.IsAny<IReadOnlyCollection<DepartmentPhoneNumber>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_carrier_failure_never_turns_a_completed_deletion_into_a_retry()
		{
			_numbers.Setup(n => n.ReleaseAtCarrierAsync(It.IsAny<IReadOnlyCollection<DepartmentPhoneNumber>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			var result = await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			result.Should().Be(DeleteDepartmentResults.NoFailure);
			_item.CompletedOn.Should().NotBeNull();
			_item.AttemptCount.Should().Be(0);
		}

		[Test]
		public async Task A_completed_purge_releases_the_numbers_even_when_recording_the_deletion_fails()
		{
			// PR #588: the queue write after the purge threw, the attempt went to the retry path, and the carrier ids (gone with the
			// purge) were never used. A retry finds no department rows and so no numbers.
			_queue.Setup(q => q.UpdateQueueItem(It.IsAny<QueueItem>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			_order.Should().Equal("read", "purged", "released");
			_numbers.Verify(n => n.ReleaseAtCarrierAsync(It.Is<IReadOnlyCollection<DepartmentPhoneNumber>>(list => list.Single().ProviderNumberId == "PN1"),
				CancellationToken.None), Times.Once);
		}

		[Test]
		public async Task An_unreadable_number_list_still_deletes_the_department()
		{
			_numbers.Setup(n => n.GetAllForDepartmentAsync(DepartmentId)).ThrowsAsync(new TimeoutException());

			var result = await Service().HandlePendingDepartmentDeletionRequestAsync(_item);

			result.Should().Be(DeleteDepartmentResults.NoFailure);
			_deletes.Verify(d => d.DeleteDepartmentAndUsersAsync(DepartmentId), Times.Once);
			_numbers.Verify(n => n.ReleaseAtCarrierAsync(It.IsAny<IReadOnlyCollection<DepartmentPhoneNumber>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private DeleteService Service() => new DeleteService(
			_authorization.Object, _departments.Object, Mock.Of<ICallsService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
			Mock.Of<IMessageService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IWorkLogsService>(), Mock.Of<IUserStateService>(), Mock.Of<IPersonnelRolesService>(),
			Mock.Of<IDistributionListsService>(), Mock.Of<IShiftsService>(), Mock.Of<IUnitsService>(), Mock.Of<ICertificationService>(), Mock.Of<ILogService>(),
			Mock.Of<IInventoryService>(), Mock.Of<IEventAggregator>(), Mock.Of<IAddressService>(), _queue.Object, Mock.Of<IEmailService>(), _deletes.Object,
			Mock.Of<IAuditLogsRepository>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IUserSessionService>(),
			Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentMemberEmergencyContactService>(), departmentPhoneNumbers: _numbers.Object);
	}
}
