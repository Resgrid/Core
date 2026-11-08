using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Station map icons (TypeIconKinds.Station): a station group's own upload, else the department's icon for every station
	/// (TypeId 0), else the bundled Station.png. The keys live only in TypeIcons, never on DepartmentGroups, so a group save
	/// cannot put an old key back.
	/// </summary>
	[TestFixture]
	public class StationIconTests
	{
		private const int DepartmentId = 7;
		private const int StationId = 301;

		private static byte[] Png(int width, int height)
		{
			using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30, 255));
			using var stream = new MemoryStream();
			image.SaveAsPng(stream);
			return stream.ToArray();
		}

		private static TypeIconVariant StationIcon(int typeId, string key, string variant = null, TypeIconKinds kind = TypeIconKinds.Station) =>
			new TypeIconVariant { TypeKind = (int)kind, TypeId = typeId, IconKey = key, Variant = variant };

		private static MapIconResolver Resolver(params TypeIconVariant[] stationIcons) =>
			new MapIconResolver(DepartmentId, null, null, null, null, null, null, stationIcons: stationIcons);

		[Test]
		public async Task A_stations_own_icon_is_stored_without_touching_the_group_row()
		{
			var repository = new Mock<ITypeIconsRepository>();
			var cache = new Mock<ICacheProvider>();
			TypeIcon inserted = null;
			repository.Setup(x => x.TypeBelongsToDepartmentAsync(DepartmentId, TypeIconKinds.Station, StationId)).ReturnsAsync(true);
			repository.Setup(x => x.InsertAsync(It.IsAny<TypeIcon>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((TypeIcon i, CancellationToken _, bool __) => inserted = i).ReturnsAsync((TypeIcon i, CancellationToken _, bool __) => i);

			var key = await new TypeIconsService(repository.Object, cache.Object).UploadIconAsync(DepartmentId, TypeIconKinds.Station, StationId, "admin", Png(48, 48));

			key.Should().Be(inserted.IconKey);
			inserted.TypeKind.Should().Be((int)TypeIconKinds.Station);
			inserted.TypeId.Should().Be(StationId);
			inserted.Variant.Should().BeNull();
			repository.Verify(x => x.DeleteAllButNewestInSlotAsync(DepartmentId, TypeIconKinds.Station, StationId, null, It.IsAny<CancellationToken>()), Times.Once);
			repository.Verify(x => x.SetTypeIconKeyAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"a group save rewrites the whole row, so the key is never mirrored on DepartmentGroups");
			repository.Verify(x => x.SetTypeIconKeyToNewestAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			cache.Verify(x => x.RemoveAsync($"TypeIconStations_{DepartmentId}"), Times.Once, "the map picks up the new icon at once");
		}

		[Test]
		public async Task The_departments_station_icon_needs_no_group()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.InsertAsync(It.IsAny<TypeIcon>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((TypeIcon i, CancellationToken _, bool __) => i);

			var key = await new TypeIconsService(repository.Object, Mock.Of<ICacheProvider>())
				.UploadIconAsync(DepartmentId, TypeIconKinds.Station, TypeIcons.DepartmentStationTypeId, "admin", Png(48, 48));

			key.Should().NotBeNull();
			repository.Verify(x => x.TypeBelongsToDepartmentAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>()), Times.Never);
			repository.Verify(x => x.InsertAsync(It.Is<TypeIcon>(i => i.TypeId == 0 && i.DepartmentId == DepartmentId), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Another_departments_group_or_an_organizational_group_gets_no_icon()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.TypeBelongsToDepartmentAsync(DepartmentId, TypeIconKinds.Station, StationId)).ReturnsAsync(false);

			(await new TypeIconsService(repository.Object, Mock.Of<ICacheProvider>())
				.UploadIconAsync(DepartmentId, TypeIconKinds.Station, StationId, "admin", Png(48, 48))).Should().BeNull();

			repository.Verify(x => x.InsertAsync(It.IsAny<TypeIcon>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Removing_a_station_icon_deletes_only_its_slot_in_the_department()
		{
			var repository = new Mock<ITypeIconsRepository>();
			var cache = new Mock<ICacheProvider>();

			(await new TypeIconsService(repository.Object, cache.Object).RemoveIconAsync(DepartmentId, TypeIconKinds.Station, StationId)).Should().BeTrue();

			repository.Verify(x => x.DeleteSlotAsync(DepartmentId, TypeIconKinds.Station, StationId, null, null, It.IsAny<CancellationToken>()), Times.Once);
			repository.Verify(x => x.SetTypeIconKeyAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			cache.Verify(x => x.RemoveAsync($"TypeIconStations_{DepartmentId}"), Times.Once);
		}

		[Test]
		public async Task Station_icons_have_no_status_slots()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.TypeBelongsToDepartmentAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>())).ReturnsAsync(true);

			(await new TypeIconsService(repository.Object, Mock.Of<ICacheProvider>())
				.UploadVariantIconAsync(DepartmentId, TypeIconKinds.Station, StationId, "s0", "admin", Png(48, 48))).Should().BeNull();

			TypeIcons.IsValidVariant(TypeIconKinds.Station, "s0").Should().BeFalse();
		}

		[Test]
		public void A_station_shows_its_own_icon_then_the_departments_then_the_bundled_pin()
		{
			var resolver = Resolver(StationIcon(StationId, "own"), StationIcon(TypeIcons.DepartmentStationTypeId, "all"));

			resolver.ForStation(StationId).IconKey.Should().Be("own");
			resolver.ForStation(StationId + 1).IconKey.Should().Be("all");
			resolver.ForStation(null).IconKey.Should().Be("all");
			Resolver().ForStation(StationId).IconKey.Should().BeNull();

			var icon = resolver.ForStation(StationId);
			icon.ImagePath.Should().Be("Station", "older clients keep the bundled station pin");
			icon.Style.Should().BeNull();
			icon.PinName.Should().BeNull("a station has no status, so no styled pin");
			icon.BadgeStyle.Should().BeNull();
		}

		[Test]
		public void The_newest_row_of_a_slot_wins_and_other_rows_are_ignored()
		{
			// Oldest first, as the repository returns them; a slot caught mid-upload holds two rows.
			var resolver = Resolver(StationIcon(StationId, "older"), StationIcon(StationId, "newer"),
				StationIcon(StationId + 5, "status", variant: "s0"),
				StationIcon(StationId + 6, "unit-type", kind: TypeIconKinds.UnitType),
				StationIcon(StationId + 7, " "));

			resolver.StationIconKey(StationId).Should().Be("newer");
			resolver.StationIconKey(StationId + 5).Should().BeNull();
			resolver.StationIconKey(StationId + 6).Should().BeNull();
			resolver.StationIconKey(StationId + 7).Should().BeNull();
			resolver.DepartmentStationIconKey.Should().BeNull();
			resolver.StationIconKey(TypeIcons.DepartmentStationTypeId).Should().BeNull("the department's icon is not a station's own");
		}

		[Test]
		public void The_legend_lists_the_department_icon_then_stations_with_their_own_by_name()
		{
			var resolver = Resolver(StationIcon(302, "zulu"), StationIcon(303, "alpha"), StationIcon(TypeIcons.DepartmentStationTypeId, "all"));
			var stations = new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = 301, Name = "No icon", Type = (int)DepartmentGroupTypes.Station },
				new DepartmentGroup { DepartmentGroupId = 302, Name = "Zulu", Type = (int)DepartmentGroupTypes.Station },
				new DepartmentGroup { DepartmentGroupId = 303, Name = "Alpha", Type = (int)DepartmentGroupTypes.Station }
			};

			var rows = MapIconLegendBuilder.StationRows(resolver, stations);

			rows.Select(x => x.Id).Should().Equal(null, 303, 302);
			rows[0].Name.Should().BeNull("the host names the row for every station");
			rows.Select(x => x.OwnIcon.IconKey).Should().Equal("all", "alpha", "zulu");
			rows.Should().OnlyContain(x => x.States.Count == 0, "a station icon never changes with status");
		}

		[Test]
		public void The_full_legend_carries_the_station_rows()
		{
			var legend = MapIconLegendBuilder.Build(Resolver(), null, null, null, null, null, "c", "u", "r");

			legend.StationRows.Should().ContainSingle();
			legend.StationRows[0].OwnIcon.IconKey.Should().BeNull();
			legend.StationRows[0].OwnIcon.ImagePath.Should().Be("Station");
		}

		[Test]
		public void Only_station_keys_are_kept_out_of_the_type_rows()
		{
			TypeIcons.MirrorsIconKey(TypeIconKinds.Station).Should().BeFalse();
			TypeIcons.MirrorsIconKey(TypeIconKinds.UnitType).Should().BeTrue();
			TypeIcons.MirrorsIconKey(TypeIconKinds.CallType).Should().BeTrue();
			TypeIcons.MirrorsIconKey(TypeIconKinds.PoiType).Should().BeTrue();
			TypeIcons.MirrorsIconKey(TypeIconKinds.PersonnelRole).Should().BeTrue();
		}
	}
}
