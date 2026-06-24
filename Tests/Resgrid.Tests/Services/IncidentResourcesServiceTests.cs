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
	/// Covers idempotent ad-hoc resource creation: the create must actually INSERT (a pre-set GUID + SaveOrUpdateAsync
	/// would be a silent 0-row UPDATE), honor a client-supplied GUID, treat a replayed id as a no-op (return the stored
	/// row, no duplicate), and reject an id owned by another department.
	/// </summary>
	[TestFixture]
	public class IncidentResourcesServiceTests
	{
		private const int Dept = 10;
		private const int CallId = 7;

		private Mock<IIncidentAdHocUnitRepository> _unitRepo;
		private Mock<IIncidentAdHocPersonnelRepository> _personnelRepo;
		private Mock<IIncidentCommandService> _commandService;
		private Mock<IEventAggregator> _eventAggregator;
		private IncidentResourcesService _service;

		[SetUp]
		public void SetUp()
		{
			_unitRepo = new Mock<IIncidentAdHocUnitRepository>();
			_personnelRepo = new Mock<IIncidentAdHocPersonnelRepository>();
			_commandService = new Mock<IIncidentCommandService>();
			_eventAggregator = new Mock<IEventAggregator>();

			_service = new IncidentResourcesService(_unitRepo.Object, _personnelRepo.Object, _commandService.Object, _eventAggregator.Object);
		}

		private void ArrangeActiveCommand()
		{
			_commandService.Setup(x => x.GetActiveCommandForCallAsync(Dept, CallId)).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Status = (int)IncidentCommandStatus.Active
			});
		}

		[Test]
		public async Task CreateAdHocUnitAsync_ReturnsNull_WhenNoActiveCommandForCall()
		{
			_commandService.Setup(x => x.GetActiveCommandForCallAsync(Dept, CallId)).ReturnsAsync((IncidentCommand)null);

			var result = await _service.CreateAdHocUnitAsync(new IncidentAdHocUnit { DepartmentId = Dept, CallId = CallId, Name = "Engine 1" }, "user1");

			result.Should().BeNull();
			_unitRepo.Verify(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task CreateAdHocUnitAsync_NoId_GeneratesIdAndInserts()
		{
			ArrangeActiveCommand();
			_unitRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentAdHocUnit u, CancellationToken ct, bool b) => u);

			var result = await _service.CreateAdHocUnitAsync(new IncidentAdHocUnit { DepartmentId = Dept, CallId = CallId, Name = "Engine 1" }, "user1");

			result.Should().NotBeNull();
			result.IncidentAdHocUnitId.Should().NotBeNullOrEmpty();
			result.CreatedOn.Should().NotBe(default(DateTime));
			_unitRepo.Verify(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_unitRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task CreateAdHocUnitAsync_ClientSuppliedId_NotPersisted_InsertsWithThatId()
		{
			ArrangeActiveCommand();
			_unitRepo.Setup(x => x.GetByIdAsync("client-1")).ReturnsAsync((IncidentAdHocUnit)null);
			_unitRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentAdHocUnit u, CancellationToken ct, bool b) => u);

			var result = await _service.CreateAdHocUnitAsync(new IncidentAdHocUnit { IncidentAdHocUnitId = "client-1", DepartmentId = Dept, CallId = CallId, Name = "Engine 1" }, "user1");

			result.Should().NotBeNull();
			result.IncidentAdHocUnitId.Should().Be("client-1"); // honored the client GUID
			_unitRepo.Verify(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task CreateAdHocUnitAsync_ReplayOfExistingId_ReturnsStored_WithoutDuplicate()
		{
			ArrangeActiveCommand();
			_unitRepo.Setup(x => x.GetByIdAsync("client-1")).ReturnsAsync(new IncidentAdHocUnit
			{
				IncidentAdHocUnitId = "client-1", DepartmentId = Dept, CallId = CallId, Name = "Engine 1"
			});

			var result = await _service.CreateAdHocUnitAsync(new IncidentAdHocUnit { IncidentAdHocUnitId = "client-1", DepartmentId = Dept, CallId = CallId, Name = "Engine 1" }, "user1");

			result.Should().NotBeNull();
			result.IncidentAdHocUnitId.Should().Be("client-1");
			_unitRepo.Verify(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_unitRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task CreateAdHocUnitAsync_ClientSuppliedId_OwnedByAnotherDepartment_ReturnsNull()
		{
			ArrangeActiveCommand();
			_unitRepo.Setup(x => x.GetByIdAsync("client-1")).ReturnsAsync(new IncidentAdHocUnit
			{
				IncidentAdHocUnitId = "client-1", DepartmentId = 99, CallId = CallId, Name = "Engine 1"
			});

			var result = await _service.CreateAdHocUnitAsync(new IncidentAdHocUnit { IncidentAdHocUnitId = "client-1", DepartmentId = Dept, CallId = CallId, Name = "Engine 1" }, "user1");

			result.Should().BeNull();
			_unitRepo.Verify(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task CreateAdHocPersonnelAsync_NoId_GeneratesIdAndInserts()
		{
			ArrangeActiveCommand();
			_personnelRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentAdHocPersonnel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentAdHocPersonnel p, CancellationToken ct, bool b) => p);

			var result = await _service.CreateAdHocPersonnelAsync(new IncidentAdHocPersonnel { DepartmentId = Dept, CallId = CallId, Name = "J. Doe" }, "user1");

			result.Should().NotBeNull();
			result.IncidentAdHocPersonnelId.Should().NotBeNullOrEmpty();
			_personnelRepo.Verify(x => x.InsertAsync(It.IsAny<IncidentAdHocPersonnel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_personnelRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<IncidentAdHocPersonnel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		// Change tracking: ad-hoc resources now carry ModifiedOn so they ride the /Sync/Changes delta.

		[Test]
		public async Task CreateAdHocUnitAsync_StampsModifiedOn()
		{
			ArrangeActiveCommand();
			IncidentAdHocUnit inserted = null;
			_unitRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentAdHocUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((IncidentAdHocUnit u, CancellationToken ct, bool b) => inserted = u)
				.ReturnsAsync((IncidentAdHocUnit u, CancellationToken ct, bool b) => u);

			await _service.CreateAdHocUnitAsync(new IncidentAdHocUnit { DepartmentId = Dept, CallId = CallId, Name = "Engine 1" }, "user1");

			inserted.Should().NotBeNull();
			inserted.ModifiedOn.Should().NotBeNull();
		}

		[Test]
		public async Task GetAdHocChangesSinceAsync_ReturnsOnlyRowsChangedAfterCursor()
		{
			var since = DateTime.UtcNow.AddMinutes(-10);
			_unitRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentAdHocUnit>
			{
				new IncidentAdHocUnit { IncidentAdHocUnitId = "u-new", DepartmentId = Dept, CallId = CallId, ModifiedOn = DateTime.UtcNow },
				new IncidentAdHocUnit { IncidentAdHocUnitId = "u-old", DepartmentId = Dept, CallId = CallId, ModifiedOn = since.AddMinutes(-5) },
				new IncidentAdHocUnit { IncidentAdHocUnitId = "u-null", DepartmentId = Dept, CallId = CallId, ModifiedOn = null }
			});
			_personnelRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentAdHocPersonnel>
			{
				// A released person changed after the cursor must be included so the client reconciles the removal.
				new IncidentAdHocPersonnel { IncidentAdHocPersonnelId = "p-rel", DepartmentId = Dept, CallId = CallId, ReleasedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow }
			});

			var (units, personnel) = await _service.GetAdHocChangesSinceAsync(Dept, since);

			units.Should().ContainSingle().Which.IncidentAdHocUnitId.Should().Be("u-new");
			personnel.Should().ContainSingle().Which.IncidentAdHocPersonnelId.Should().Be("p-rel");
		}
	}
}
