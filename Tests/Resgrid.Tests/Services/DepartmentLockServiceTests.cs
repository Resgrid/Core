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
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DepartmentLockServiceTests
	{
		private Mock<IDepartmentOperationLockRepository> _lockRepo;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentLockService _service;

		[SetUp]
		public void SetUp()
		{
			_lockRepo = new Mock<IDepartmentOperationLockRepository>();
			_cacheProvider = new Mock<ICacheProvider>();

			// Cache pass-through: always executes the fallback so repository setups drive behavior.
			_cacheProvider
				.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentOperationLock>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentOperationLock>>, TimeSpan>((key, fallback, expiration) => fallback());
			_cacheProvider.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			_service = new DepartmentLockService(_lockRepo.Object, _cacheProvider.Object);
		}

		private static DepartmentOperationLock ActiveLock(int id = 5, int departmentId = 9, int expiresInMinutes = 10) => new DepartmentOperationLock
		{
			DepartmentOperationLockId = id,
			DepartmentId = departmentId,
			LockType = (int)DepartmentOperationLockType.AdpMigration,
			AppliedUtc = DateTime.UtcNow.AddMinutes(-5),
			HeartbeatUtc = DateTime.UtcNow,
			ExpiresUtc = DateTime.UtcNow.AddMinutes(expiresInMinutes)
		};

		[Test]
		public async Task Active_unexpired_lock_reports_locked()
		{
			_lockRepo.Setup(x => x.GetActiveByDepartmentIdAsync(9)).ReturnsAsync(ActiveLock());

			(await _service.IsDepartmentLockedAsync(9)).Should().BeTrue();
		}

		[Test]
		public async Task Lock_past_its_safety_valve_stops_enforcing_immediately()
		{
			_lockRepo.Setup(x => x.GetActiveByDepartmentIdAsync(9)).ReturnsAsync(ActiveLock(expiresInMinutes: -1));

			(await _service.IsDepartmentLockedAsync(9)).Should().BeFalse();
		}

		[Test]
		public async Task No_lock_reports_unlocked()
		{
			_lockRepo.Setup(x => x.GetActiveByDepartmentIdAsync(9)).ReturnsAsync((DepartmentOperationLock)null);

			(await _service.IsDepartmentLockedAsync(9)).Should().BeFalse();
		}

		[Test]
		public async Task Blank_cache_poisoned_entity_reports_unlocked()
		{
			// An empty cached payload deserializes to a non-null entity with default values; it must
			// never enforce a lock.
			_lockRepo.Setup(x => x.GetActiveByDepartmentIdAsync(9)).ReturnsAsync(new DepartmentOperationLock());

			(await _service.IsDepartmentLockedAsync(9)).Should().BeFalse();
		}

		[Test]
		public async Task Lock_store_outage_fails_open()
		{
			_lockRepo.Setup(x => x.GetActiveByDepartmentIdAsync(9)).ThrowsAsync(new InvalidOperationException("db down"));

			(await _service.IsDepartmentLockedAsync(9)).Should().BeFalse(
				"dispatch availability beats migration progress");
		}

		[Test]
		public async Task ApplyLock_returns_lock_and_invalidates_cache_when_acquired()
		{
			_lockRepo.Setup(x => x.TryAcquireAsync(It.IsAny<DepartmentOperationLock>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await _service.ApplyLockAsync(9, DepartmentOperationLockType.AdpMigration, "ADP migration",
				"corr-1", "worker:adp", DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow.AddHours(8));

			result.Should().NotBeNull();
			result.DepartmentId.Should().Be(9);
			_cacheProvider.Verify(x => x.RemoveAsync(It.Is<string>(k => k.Contains("9"))), Times.Once);
		}

		[Test]
		public async Task ApplyLock_returns_null_when_another_lock_holds()
		{
			_lockRepo.Setup(x => x.TryAcquireAsync(It.IsAny<DepartmentOperationLock>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);

			var result = await _service.ApplyLockAsync(9, DepartmentOperationLockType.AdpMigration, "ADP migration",
				"corr-1", "worker:adp", DateTime.UtcNow.AddMinutes(5), null);

			result.Should().BeNull();
		}

		[Test]
		public async Task Expired_sweep_releases_only_past_valve_locks()
		{
			var expired = ActiveLock(id: 1, departmentId: 9, expiresInMinutes: -5);
			var healthy = ActiveLock(id: 2, departmentId: 10, expiresInMinutes: 30);
			_lockRepo.Setup(x => x.GetAllActiveAsync())
				.ReturnsAsync(new List<DepartmentOperationLock> { expired, healthy });
			_lockRepo.Setup(x => x.ReleaseAsync(1, DepartmentOperationLockReleaseKind.Expired, It.IsAny<string>(),
					It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(1);

			var released = await _service.ReleaseExpiredLocksAsync();

			released.Should().ContainSingle(l => l.DepartmentOperationLockId == 1);
			_lockRepo.Verify(x => x.ReleaseAsync(2, It.IsAny<DepartmentOperationLockReleaseKind>(), It.IsAny<string>(),
				It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
