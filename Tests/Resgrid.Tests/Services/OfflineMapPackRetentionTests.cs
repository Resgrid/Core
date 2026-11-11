using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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

		private OfflineMapPackService Service(IUnitsService units = null, ITypeIconsService typeIcons = null)
			=> new OfflineMapPackService(_packs.Object, _store.Object, Mock.Of<IDepartmentSettingsService>(), Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IGeoService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<ICustomMapService>(), Mock.Of<ICustomMapMercatorTileRenderer>(), Mock.Of<IMappingService>(),
				units ?? Mock.Of<IUnitsService>(), Mock.Of<ICallsService>(), typeIcons ?? Mock.Of<ITypeIconsService>(), Mock.Of<IRouteService>());

		private static byte[] Png()
		{
			using var image = new Image<Rgba32>(8, 8);
			using var stream = new MemoryStream();
			image.SaveAsPng(stream);
			return stream.ToArray();
		}

		[Test]
		public async Task A_sprite_build_records_its_storage_handle_before_uploading_so_a_failed_build_keeps_it()
		{
			MappingConfig.OfflineMapsEnabled = true;
			_store.SetupGet(x => x.IsConfigured).Returns(true);
			var queued = new OfflineMapPack { OfflineMapPackId = "sprite-1", DepartmentId = 12, Kind = (int)OfflineMapPackKind.Sprite, PackKey = "sprite:12:abc", Version = "abc", Status = (int)OfflineMapPackStatus.Queued };
			_packs.Setup(x => x.GetQueuedAsync(It.IsAny<int>())).ReturnsAsync(new List<OfflineMapPack> { queued });
			_packs.Setup(x => x.TryClaimAsync("sprite-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var order = new List<string>();
			_packs.Setup(x => x.SetStorageHandleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("handle")).Returns(Task.CompletedTask);
			OfflineMapPack saved = null;
			_packs.Setup(x => x.SaveOrUpdateAsync(It.IsAny<OfflineMapPack>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((OfflineMapPack p, CancellationToken _, bool __) => saved = p).ReturnsAsync((OfflineMapPack p, CancellationToken _, bool __) => p);
			// The upload stops part-way: a store error here. A killed worker stops at the same point, without the final save.
			_store.Setup(x => x.PutAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("upload")).ThrowsAsync(new IOException("store unavailable"));
			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitTypesForDepartmentAsync(12)).ReturnsAsync(new List<UnitType> { new UnitType { IconKey = "icon-1" } });
			var typeIcons = new Mock<ITypeIconsService>();
			typeIcons.Setup(x => x.GetIconByKeyAsync("icon-1")).ReturnsAsync(new TypeIcon { DepartmentId = 12, Data = Png() });

			(await Service(units.Object, typeIcons.Object).ProcessQueueAsync(5)).Should().Be(1);

			order.Should().StartWith(new[] { "handle", "upload" }, "the row names the files before the first one is uploaded");
			_packs.Verify(x => x.SetStorageHandleAsync("sprite-1", "sprite/12/abc", It.IsAny<string>(), CancellationToken.None), Times.Once);
			saved.Should().NotBeNull();
			saved.Status.Should().Be((int)OfflineMapPackStatus.Failed);
			saved.StoragePath.Should().Be("sprite/12/abc", "retention deletes the failed build's files through this handle");
			JsonConvert.DeserializeObject<List<OfflineMapPackFile>>(saved.FilesJson).Select(f => f.Name).Should().Contain(new[] { "sprite.png", "sprite.json", "sprite@2x.png", "sprite@2x.json" });
		}

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
