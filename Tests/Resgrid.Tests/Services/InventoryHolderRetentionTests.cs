using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class InventoryHolderRetentionTests
	{
		private static Mock<IUnitOfWork> UnitOfWork(bool joined = false)
		{
			var work = new Mock<IUnitOfWork>();
			work.SetupGet(u => u.Transaction).Returns(joined ? new Mock<DbTransaction>().Object : null);
			work.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DbConnection)null);
			return work;
		}
		[TestCase(false, false)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public async Task Group_inventory_history_stops_all_association_cleanup(bool archived, bool nextPage)
		{
			var location = new InventoryLocation { DepartmentId = 77, LocationType = (int)InventoryLocationType.Station, GroupId = 101, IsDeleted = archived };
			var store = new Mock<IInventoryStore>(); var work = UnitOfWork();
			store.Setup(s => s.ListAsync<InventoryLocation>(77, 0)).ReturnsAsync(nextPage
				? Enumerable.Range(0, 500).Select(_ => new InventoryLocation { DepartmentId = 77, GroupId = 102 }).Append(location).ToList()
				: new List<InventoryLocation> { location });
			store.Setup(s => s.ListAsync<InventoryLocation>(77, 500)).ReturnsAsync(new List<InventoryLocation> { location });
			var resources = new Mock<IAuthorizationService>();
			resources.Setup(a => a.CanUserEditDepartmentGroupAsync("manager", 101)).ReturnsAsync(true);
			var calls = new Mock<ICallsService>(MockBehavior.Strict);
			var logs = new Mock<IWorkLogsService>(MockBehavior.Strict);
			var units = new Mock<IUnitsService>(MockBehavior.Strict);
			var shifts = new Mock<IShiftsService>(MockBehavior.Strict);
			var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
			var groups = new Mock<IDepartmentGroupsService>(MockBehavior.Strict);
			var service = DeleteService(resources.Object, calls.Object, logs.Object, units.Object, shifts.Object, inventory.Object, groups.Object, store.Object, work.Object);

			var error = (await ((Func<Task>)(async () => await service.DeleteGroupAsync(101, 77, "manager"))).Should().ThrowAsync<InventoryException>()).Which;
			error.Code.Should().Be("HolderHistoryRetained"); error.StatusCode.Should().Be(409);
			calls.VerifyNoOtherCalls(); logs.VerifyNoOtherCalls(); units.VerifyNoOtherCalls(); shifts.VerifyNoOtherCalls(); inventory.VerifyNoOtherCalls(); groups.VerifyNoOtherCalls();
			store.Verify(s => s.LockDepartmentAsync(77), Times.Once);
			store.Verify(s => s.ListAsync<InventoryLocation>(77, 500), nextPage ? Times.Once() : Times.Never());
			work.Verify(u => u.DiscardChanges(), Times.Once); work.Verify(u => u.CommitChanges(), Times.Never);
		}
		[Test]
		public async Task Group_without_inventory_references_cleans_associations_only_after_locking()
		{
			var order = new List<string>();
			var store = new Mock<IInventoryStore>(); var work = UnitOfWork();
			store.Setup(s => s.LockDepartmentAsync(77)).Callback(() => order.Add("lock")).Returns(Task.CompletedTask);
			store.Setup(s => s.ListAsync<InventoryLocation>(77, 0)).Callback(() => order.Add("inventory-check")).ReturnsAsync(new List<InventoryLocation>());
			var resources = new Mock<IAuthorizationService>(); resources.Setup(a => a.CanUserEditDepartmentGroupAsync("manager", 101)).ReturnsAsync(true);
			var calls = new Mock<ICallsService>(); var logs = new Mock<IWorkLogsService>(); var units = new Mock<IUnitsService>();
			var shifts = new Mock<IShiftsService>(); var inventory = new Mock<IInventoryService>(); var groups = new Mock<IDepartmentGroupsService>();
			calls.Setup(c => c.ClearGroupForDispatchesAsync(101, It.IsAny<CancellationToken>())).Callback(() => order.Add("cleanup")).ReturnsAsync(true);
			var service = DeleteService(resources.Object, calls.Object, logs.Object, units.Object, shifts.Object, inventory.Object, groups.Object, store.Object, work.Object);

			(await service.DeleteGroupAsync(101, 77, "manager")).Should().Be(DeleteGroupResults.NoFailure);
			order.Should().Equal("lock", "inventory-check", "cleanup");
			groups.Verify(g => g.DeleteGroupMembersByGroupIdAsync(101, 77, It.IsAny<CancellationToken>()), Times.Once);
			groups.Verify(g => g.DeleteGroupByIdAsync(101, It.IsAny<CancellationToken>()), Times.Once);
			work.Verify(u => u.CommitChanges(), Times.Once); work.Verify(u => u.DiscardChanges(), Times.Never);
		}
		[TestCase(false)]
		[TestCase(true)]
		public async Task Unit_history_blocks_state_deletion_and_respects_transaction_ownership(bool joined)
		{
			var store = new Mock<IInventoryStore>(); var work = UnitOfWork(joined);
			store.Setup(s => s.ListAsync<InventoryLocation>(77, 0)).ReturnsAsync(new List<InventoryLocation>
				{ new() { DepartmentId = 77, LocationType = (int)InventoryLocationType.Unit, UnitId = 501, IsDeleted = true } });
			var units = new Mock<IUnitsRepository>(MockBehavior.Strict);
			units.Setup(u => u.GetByIdAsync(501)).ReturnsAsync(new Unit { UnitId = 501, DepartmentId = 77 });
			var states = new Mock<IUnitStatesRepository>(MockBehavior.Strict);
			var activeRoles = new Mock<IUnitActiveRolesRepository>(MockBehavior.Strict);
			var limits = new Mock<ILimitsService>(MockBehavior.Strict);
			var events = new Mock<IEventAggregator>(MockBehavior.Strict);
			var service = new UnitsService(units.Object, states.Object, Mock.Of<IUnitLogsRepository>(), Mock.Of<IUnitTypesRepository>(),
				Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(), Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(),
				events.Object, Mock.Of<ICustomStateService>(), new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(), new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				activeRoles.Object, Mock.Of<IDepartmentGroupsService>(), limits.Object, Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()), new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()), store.Object, work.Object);

			var error = (await ((Func<Task>)(async () => await service.DeleteUnitAsync(501))).Should().ThrowAsync<InventoryException>()).Which;
			error.Code.Should().Be("HolderHistoryRetained"); error.StatusCode.Should().Be(409);
			units.Verify(u => u.GetByIdAsync(501), Times.Once); units.VerifyNoOtherCalls();
			states.VerifyNoOtherCalls(); activeRoles.VerifyNoOtherCalls(); limits.VerifyNoOtherCalls(); events.VerifyNoOtherCalls();
			store.Verify(s => s.LockDepartmentAsync(77), Times.Once);
			work.Verify(u => u.DiscardChanges(), joined ? Times.Never() : Times.Once());
			work.Verify(u => u.CommitChanges(), Times.Never);
		}
		private static DeleteService DeleteService(IAuthorizationService resources, ICallsService calls, IWorkLogsService logs, IUnitsService units,
			IShiftsService shifts, IInventoryService inventory, IDepartmentGroupsService groups, IInventoryStore store, IUnitOfWork work) => new DeleteService(
			resources, Mock.Of<IDepartmentsService>(), calls, Mock.Of<IActionLogsService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
			Mock.Of<IMessageService>(), groups, logs, Mock.Of<IUserStateService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IDistributionListsService>(), shifts,
			units, Mock.Of<ICertificationService>(), Mock.Of<ILogService>(), inventory, Mock.Of<IEventAggregator>(), Mock.Of<IAddressService>(), Mock.Of<IQueueService>(),
			Mock.Of<IEmailService>(), Mock.Of<IDeleteRepository>(), Mock.Of<IAuditLogsRepository>(), Mock.Of<IScheduledTasksService>(), Mock.Of<IUserSessionService>(),
			Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentMemberEmergencyContactService>(), store, work);
	}
}
