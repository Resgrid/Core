using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>Bulk removal of imported map data: POIs of one type and every region of a custom map layer.</summary>
	[TestFixture]
	public class BulkMapDataRemovalTests
	{
		private const int DepartmentId = 7;
		private const int TypeId = 40;

		private Mock<IPoiTypesRepository> _poiTypes;
		private Mock<IPoisRepository> _pois;
		private Mock<ISearchProjectionService> _projections;
		private MappingService _mapping;
		private List<int> _deletedIds;

		[SetUp]
		public void SetUp()
		{
			_poiTypes = new Mock<IPoiTypesRepository>();
			_pois = new Mock<IPoisRepository>();
			_projections = new Mock<ISearchProjectionService>();
			_deletedIds = null;
			_pois.Setup(x => x.DeleteForTypeAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int type, IReadOnlyCollection<int> ids, CancellationToken _) => { _deletedIds = ids.ToList(); return ids.ToList(); });
			_mapping = new MappingService(_poiTypes.Object, _pois.Object, null, null, new Lazy<ISearchProjectionService>(() => _projections.Object));
		}

		private PoiType SeedType(int departmentId, params int[] poiIds)
		{
			var type = new PoiType { PoiTypeId = TypeId, DepartmentId = departmentId, Name = "Hydrants (imported)", Pois = poiIds.Select(id => new Poi { PoiId = id, PoiTypeId = TypeId }).ToList() };
			_poiTypes.Setup(x => x.GetPoiTypeByTypeIdAsync(TypeId)).ReturnsAsync(type);
			return type;
		}

		[Test]
		public async Task Pois_of_the_type_are_deleted_and_unindexed_and_foreign_ids_are_ignored()
		{
			SeedType(DepartmentId, 1, 2, 3, 4);

			var removed = await _mapping.DeletePOIsAsync(DepartmentId, TypeId, new[] { 2, 3, 3, 99, -5, 0 }, 2);

			removed.Should().Be(2);
			_deletedIds.Should().BeEquivalentTo(new[] { 2, 3 }, "99 is not a POI of this type");
			_pois.Verify(x => x.DeleteForTypeAsync(TypeId, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Once);
			_projections.Verify(x => x.RemoveManyAsync(DepartmentId, SearchEntityTypes.Poi, It.Is<IEnumerable<string>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { "2", "3" })), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Only_the_pois_the_statement_deleted_lose_their_search_projection()
		{
			SeedType(DepartmentId, 1, 2, 3);
			// POI 2 was moved to another type after the page loaded: the type filter keeps it, so its projection must stay.
			_pois.Setup(x => x.DeleteForTypeAsync(TypeId, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<int> { 1, 3 });

			var removed = await _mapping.DeletePOIsAsync(DepartmentId, TypeId, new[] { 1, 2, 3 }, 3);

			removed.Should().Be(2);
			_projections.Verify(x => x.RemoveManyAsync(DepartmentId, SearchEntityTypes.Poi, It.Is<IEnumerable<string>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { "1", "3" })), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_count_that_does_not_match_removes_nothing()
		{
			SeedType(DepartmentId, 1, 2, 3);

			Func<Task> stale = () => _mapping.DeletePOIsAsync(DepartmentId, TypeId, new[] { 1, 2, 3 }, 2);

			await stale.Should().ThrowAsync<ArgumentException>().WithMessage("3 POIs match*not 2*Reload*");
			_pois.Verify(x => x.DeleteForTypeAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
			_projections.Verify(x => x.RemoveManyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Another_departments_type_is_refused()
		{
			SeedType(DepartmentId + 1, 1, 2);

			Func<Task> foreign = () => _mapping.DeletePOIsAsync(DepartmentId, TypeId, new[] { 1, 2 }, 2);

			await foreign.Should().ThrowAsync<ArgumentException>().WithMessage("*does not exist*");
			_pois.Verify(x => x.DeleteForTypeAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Empty_and_oversized_requests_are_refused()
		{
			SeedType(DepartmentId, 1);

			Func<Task> none = () => _mapping.DeletePOIsAsync(DepartmentId, TypeId, new int[0], null);
			await none.Should().ThrowAsync<ArgumentException>().WithMessage("Select the POIs*");
			Func<Task> tooMany = () => _mapping.DeletePOIsAsync(DepartmentId, TypeId, Enumerable.Range(1, MappingService.MaxBulkPoiDelete + 1), null);
			await tooMany.Should().ThrowAsync<ArgumentException>().WithMessage("*at most*");
			_pois.Verify(x => x.DeleteForTypeAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Ids_that_match_nothing_delete_nothing()
		{
			SeedType(DepartmentId, 1, 2);

			(await _mapping.DeletePOIsAsync(DepartmentId, TypeId, new[] { 50, 51 }, 0)).Should().Be(0);

			_pois.Verify(x => x.DeleteForTypeAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private static CustomMapService CustomMaps(Mock<IIndoorMapZonesRepository> zones, Mock<IUnitOfWork> unitOfWork = null)
			=> new CustomMapService(Mock.Of<IIndoorMapsRepository>(), Mock.Of<IIndoorMapFloorsRepository>(), zones.Object, Mock.Of<ICustomMapTilesRepository>(), Mock.Of<ICustomMapImportsRepository>(),
				(unitOfWork ?? new Mock<IUnitOfWork>()).Object);

		private static Mock<IIndoorMapZonesRepository> Zones(params bool[] deleted)
		{
			var zones = new Mock<IIndoorMapZonesRepository>();
			zones.Setup(x => x.GetZonesByFloorIdAsync("layer-1")).ReturnsAsync(deleted.Select((d, i) => new IndoorMapZone { IndoorMapZoneId = "z" + i, IndoorMapFloorId = "layer-1", IsDeleted = d }).ToList());
			zones.Setup(x => x.SoftDeleteForFloorAsync("layer-1", It.IsAny<CancellationToken>())).ReturnsAsync(deleted.Count(d => !d));
			return zones;
		}

		[Test]
		public async Task Clearing_a_layer_removes_its_live_regions_when_the_count_matches()
		{
			var zones = Zones(false, false, true);

			(await CustomMaps(zones).DeleteRegionsForLayerAsync("layer-1", 2)).Should().Be(2);

			zones.Verify(x => x.SoftDeleteForFloorAsync("layer-1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Clearing_a_layer_commits_only_when_the_update_matches_the_confirmed_count()
		{
			var unitOfWork = new Mock<IUnitOfWork>();
			var zones = Zones(false, false);
			(await CustomMaps(zones, unitOfWork).DeleteRegionsForLayerAsync("layer-1", 2)).Should().Be(2);
			unitOfWork.Verify(x => x.CommitChanges(), Times.Once);
			unitOfWork.Verify(x => x.DiscardChanges(), Times.Never);

			// A region added between the page's count and the UPDATE: the statement matches 3, so the removal rolls back.
			unitOfWork = new Mock<IUnitOfWork>();
			zones = Zones(false, false);
			zones.Setup(x => x.SoftDeleteForFloorAsync("layer-1", It.IsAny<CancellationToken>())).ReturnsAsync(3);
			Func<Task> raced = () => CustomMaps(zones, unitOfWork).DeleteRegionsForLayerAsync("layer-1", 2);

			await raced.Should().ThrowAsync<ArgumentException>().WithMessage("3 regions are on this layer, not 2*");
			unitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
			unitOfWork.Verify(x => x.CommitChanges(), Times.Never);
		}

		[Test]
		public async Task Clearing_a_layer_with_a_stale_count_removes_nothing()
		{
			var zones = Zones(false, false, false);

			Func<Task> stale = () => CustomMaps(zones).DeleteRegionsForLayerAsync("layer-1", 2);

			await stale.Should().ThrowAsync<ArgumentException>().WithMessage("3 regions are on this layer, not 2*");
			zones.Verify(x => x.SoftDeleteForFloorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Clearing_an_empty_layer_touches_nothing()
		{
			var zones = Zones(true);

			(await CustomMaps(zones).DeleteRegionsForLayerAsync("layer-1", 0)).Should().Be(0);
			Func<Task> noLayer = () => CustomMaps(zones).DeleteRegionsForLayerAsync(" ", 0);
			await noLayer.Should().ThrowAsync<ArgumentException>();

			zones.Verify(x => x.SoftDeleteForFloorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
