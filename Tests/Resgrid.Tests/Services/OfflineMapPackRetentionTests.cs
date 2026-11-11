using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Offline pack retention (<see cref="OfflineMapPackService.CleanupAsync"/>) keeps running while offline installs are switched off:
	/// department deletion leaves pack rows (the only handle to their files) to this sweep, and stuck builds are recovered so
	/// retention (which skips Building rows) can reach them.
	/// </summary>
	[TestFixture]
	public class OfflineMapPackRetentionTests
	{
		private bool _offlineMapsEnabled;
		private Mock<IOfflineMapPacksRepository> _packs;
		private Mock<IOfflineMapPackStore> _store;
		private OfflineMapPack _expired;

		[SetUp]
		public void SetUp()
		{
			_offlineMapsEnabled = MappingConfig.OfflineMapsEnabled;
			_packs = new Mock<IOfflineMapPacksRepository>();
			_store = new Mock<IOfflineMapPackStore>();
			_expired = new OfflineMapPack { OfflineMapPackId = "p1", DepartmentId = 12, PackKey = "dept-12-units", StoragePath = "packs/dept-12-units.pmtiles" };
			_packs.Setup(x => x.GetNotRequestedSinceAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<OfflineMapPack> { _expired });
			_packs.Setup(x => x.GetStuckBuildingAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<OfflineMapPack> { new OfflineMapPack { OfflineMapPackId = "stuck", Status = (int)OfflineMapPackStatus.Building } });
			_packs.Setup(x => x.DeleteAsync(It.IsAny<OfflineMapPack>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
		}

		[TearDown]
		public void TearDown() => MappingConfig.OfflineMapsEnabled = _offlineMapsEnabled;

		private OfflineMapPackService Service()
			=> new OfflineMapPackService(_packs.Object, _store.Object, Mock.Of<IDepartmentSettingsService>(), Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IGeoService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<ICustomMapService>(), Mock.Of<ICustomMapMercatorTileRenderer>(), Mock.Of<IMappingService>(),
				Mock.Of<IUnitsService>(), Mock.Of<ICallsService>(), Mock.Of<ITypeIconsService>(), Mock.Of<IRouteService>());

		[Test]
		public async Task Expired_packs_are_removed_and_stuck_builds_recovered_while_offline_installs_are_switched_off()
		{
			MappingConfig.OfflineMapsEnabled = false;
			_store.SetupGet(x => x.IsConfigured).Returns(true);

			var changed = await Service().CleanupAsync();

			changed.Should().Be(2);
			_store.Verify(x => x.DeleteAsync(_expired.StoragePath, It.IsAny<CancellationToken>()), Times.Once);
			_packs.Verify(x => x.DeleteAsync(_expired, It.IsAny<CancellationToken>()), Times.Once);
			// Retention skips Building rows, so a build stopped mid-way must leave Building or it would never age out.
			_packs.Verify(x => x.SaveOrUpdateAsync(It.Is<OfflineMapPack>(p => p.OfflineMapPackId == "stuck" && p.Status == (int)OfflineMapPackStatus.Queued), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Stuck_builds_are_requeued_and_expired_packs_removed_while_installs_are_on()
		{
			MappingConfig.OfflineMapsEnabled = true;
			_store.SetupGet(x => x.IsConfigured).Returns(true);

			var changed = await Service().CleanupAsync();

			changed.Should().Be(2);
			_packs.Verify(x => x.SaveOrUpdateAsync(It.Is<OfflineMapPack>(p => p.OfflineMapPackId == "stuck" && p.Status == (int)OfflineMapPackStatus.Queued), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_packs.Verify(x => x.DeleteAsync(_expired, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Nothing_runs_without_a_configured_store()
		{
			MappingConfig.OfflineMapsEnabled = true;
			_store.SetupGet(x => x.IsConfigured).Returns(false);

			(await Service().CleanupAsync()).Should().Be(0);

			_packs.Verify(x => x.GetNotRequestedSinceAsync(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
			_packs.Verify(x => x.GetStuckBuildingAsync(It.IsAny<DateTime>()), Times.Never);
		}
	}
}
