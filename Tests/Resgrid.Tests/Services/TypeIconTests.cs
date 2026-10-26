using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Custom unit, call and POI type icons (M0285) and personnel role icons (M0286): upload validation and re-encoding,
	/// the upload / remove / lookup service, the URLs clients load icons from, the role order that picks a person's map
	/// icon, and the type and role deletes taking their icon with them.
	/// </summary>
	[TestFixture]
	public class TypeIconTests
	{
		private const int DepartmentId = 7;
		private const int TypeId = 31;
		private const string Key = "0123456789abcdef0123456789abcdef";

		#region Renditions

		[Test]
		public void Build_scales_a_wide_png_onto_a_transparent_128_square()
		{
			var rendition = TypeIconRenditions.Build(Png(64, 32, new Rgba32(200, 0, 0, 255)));

			rendition.ContentType.Should().Be("image/png");
			rendition.Width.Should().Be(TypeIconRenditions.Size);
			rendition.Height.Should().Be(TypeIconRenditions.Size);

			using var image = Image.Load<Rgba32>(rendition.Data);
			image.Width.Should().Be(128);
			image.Height.Should().Be(128);
			image[64, 2].A.Should().Be(0, "the 2:1 image is centred, so the band above it is transparent padding");
			image[64, 64].R.Should().BeGreaterThan(150, "the image itself fills the middle");
			image[64, 64].A.Should().Be(255);
		}

		[Test]
		public void Build_accepts_a_jpeg_and_re_encodes_it_as_png()
		{
			byte[] jpeg;
			using (var image = new Image<Rgba32>(40, 40, new Rgba32(0, 0, 200, 255)))
			using (var stream = new MemoryStream())
			{
				image.Save(stream, new JpegEncoder());
				jpeg = stream.ToArray();
			}

			var rendition = TypeIconRenditions.Build(jpeg);

			Image.DetectFormat(rendition.Data).Name.Should().Be("PNG");
		}

		[Test]
		public void Build_refuses_an_svg()
		{
			var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><script>alert(1)</script></svg>");

			Action act = () => TypeIconRenditions.Build(svg);

			act.Should().Throw<TypeIconRejectedException>().Which.Reason.Should().BeOneOf(TypeIconRejections.Unreadable, TypeIconRejections.UnsupportedFormat);
		}

		[Test]
		public void Build_refuses_a_bmp_as_an_unsupported_format()
		{
			byte[] bmp;
			using (var image = new Image<Rgba32>(32, 32, new Rgba32(0, 0, 0, 255)))
			using (var stream = new MemoryStream())
			{
				image.Save(stream, new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder());
				bmp = stream.ToArray();
			}

			Action act = () => TypeIconRenditions.Build(bmp);

			act.Should().Throw<TypeIconRejectedException>().Which.Reason.Should().Be(TypeIconRejections.UnsupportedFormat);
		}

		[Test]
		public void Build_keeps_only_the_first_frame_of_an_animated_gif()
		{
			byte[] gif;
			using (var image = new Image<Rgba32>(32, 32, new Rgba32(255, 0, 0, 255)))
			using (var second = new Image<Rgba32>(32, 32, new Rgba32(0, 0, 255, 255)))
			using (var stream = new MemoryStream())
			{
				image.Frames.AddFrame(second.Frames.RootFrame);
				image.Save(stream, new SixLabors.ImageSharp.Formats.Gif.GifEncoder());
				gif = stream.ToArray();
			}

			var rendition = TypeIconRenditions.Build(gif);

			using var stored = Image.Load<Rgba32>(rendition.Data);
			stored.Frames.Count.Should().Be(1);
			stored[64, 64].R.Should().BeGreaterThan(150, "the first (red) frame is the icon");
		}

		[Test]
		public void Build_refuses_an_empty_upload()
		{
			Action act = () => TypeIconRenditions.Build(Array.Empty<byte>());

			act.Should().Throw<TypeIconRejectedException>().WithMessage("An icon file is required.").Which.Reason.Should().Be(TypeIconRejections.Required);
		}

		[Test]
		public void Build_refuses_a_file_over_1_mb()
		{
			Action act = () => TypeIconRenditions.Build(new byte[TypeIconRenditions.MaxBytes + 1]);

			act.Should().Throw<TypeIconRejectedException>().WithMessage("*1024 KB*").Which.Reason.Should().Be(TypeIconRejections.TooManyBytes);
		}

		[Test]
		public void Build_refuses_a_tiny_image()
		{
			Action act = () => TypeIconRenditions.Build(Png(8, 8, new Rgba32(0, 0, 0, 255)));

			act.Should().Throw<TypeIconRejectedException>().WithMessage("*at least 16 px*").Which.Reason.Should().Be(TypeIconRejections.TooSmall);
		}

		[Test]
		public void Build_refuses_oversized_dimensions_from_the_header()
		{
			Action act = () => TypeIconRenditions.Build(Png(5000, 20, new Rgba32(0, 0, 0, 0)));

			act.Should().Throw<TypeIconRejectedException>().WithMessage("*4096 px*").Which.Reason.Should().Be(TypeIconRejections.TooLarge);
		}

		#endregion Renditions

		#region Service

		[TestCase(TypeIconKinds.UnitType)]
		[TestCase(TypeIconKinds.CallType)]
		[TestCase(TypeIconKinds.PoiType)]
		[TestCase(TypeIconKinds.PersonnelRole)]
		public async Task UploadIconAsync_stores_a_new_icon_and_points_the_type_at_it(TypeIconKinds kind)
		{
			var repository = new Mock<ITypeIconsRepository>();
			TypeIcon inserted = null;
			repository.Setup(x => x.InsertAsync(It.IsAny<TypeIcon>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((TypeIcon i, CancellationToken _, bool __) => inserted = i).ReturnsAsync((TypeIcon i, CancellationToken _, bool __) => i);
			repository.Setup(x => x.SetTypeIconKeyToNewestAsync(DepartmentId, kind, TypeId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

			var key = await Service(repository.Object).UploadIconAsync(DepartmentId, kind, TypeId, "admin", Png(48, 48, new Rgba32(0, 120, 0, 255)));

			inserted.Should().NotBeNull();
			inserted.DepartmentId.Should().Be(DepartmentId);
			inserted.TypeKind.Should().Be((int)kind);
			inserted.TypeId.Should().Be(TypeId);
			inserted.IconKey.Should().MatchRegex("^[0-9a-f]{32}$");
			inserted.Checksum.Should().HaveLength(64);
			inserted.Data.Length.Should().Be((int)inserted.ByteSize);
			key.Should().Be(inserted.IconKey, "a new upload gets a new key, so the cached URL never serves the old image");
			repository.Verify(x => x.SetTypeIconKeyToNewestAsync(DepartmentId, kind, TypeId, It.IsAny<CancellationToken>()), Times.Once);
			repository.Verify(x => x.DeleteAllButNewestInSlotAsync(DepartmentId, kind, TypeId, null, It.IsAny<CancellationToken>()), Times.Once,
				"only the type's own icon slot is replaced; its status and priority icons stay");
			repository.Verify(x => x.SetTypeIconKeyAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"pointing the type at \"this upload\" would let two uploads at once leave it on a deleted row");
			repository.Verify(x => x.DeleteSlotAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			repository.Verify(x => x.DeleteForTypeAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task UploadIconAsync_for_another_departments_type_stores_nothing()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.SetTypeIconKeyToNewestAsync(DepartmentId, TypeIconKinds.CallType, TypeId, It.IsAny<CancellationToken>())).ReturnsAsync(0);

			var key = await Service(repository.Object).UploadIconAsync(DepartmentId, TypeIconKinds.CallType, TypeId, "admin", Png(48, 48, new Rgba32(0, 0, 0, 255)));

			key.Should().BeNull("the department-scoped update changed no row, so the type is not this department's");
			repository.Verify(x => x.DeleteSlotAsync(DepartmentId, TypeIconKinds.CallType, TypeId, null, null, It.IsAny<CancellationToken>()), Times.Once,
				"the row inserted for it is removed again");
			repository.Verify(x => x.DeleteAllButNewestInSlotAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task RemoveIconAsync_clears_the_key_and_deletes_the_rows()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.SetTypeIconKeyAsync(DepartmentId, TypeIconKinds.PoiType, TypeId, null, It.IsAny<CancellationToken>())).ReturnsAsync(1);

			(await Service(repository.Object).RemoveIconAsync(DepartmentId, TypeIconKinds.PoiType, TypeId)).Should().BeTrue();

			repository.Verify(x => x.DeleteSlotAsync(DepartmentId, TypeIconKinds.PoiType, TypeId, null, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task RemoveIconAsync_for_another_departments_type_deletes_nothing()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.SetTypeIconKeyAsync(DepartmentId, TypeIconKinds.UnitType, TypeId, null, It.IsAny<CancellationToken>())).ReturnsAsync(0);

			(await Service(repository.Object).RemoveIconAsync(DepartmentId, TypeIconKinds.UnitType, TypeId)).Should().BeFalse();

			repository.Verify(x => x.DeleteSlotAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(TypeIconKinds.CallType, "p2")]
		[TestCase(TypeIconKinds.UnitType, "s5")]
		[TestCase(TypeIconKinds.UnitType, "o4410")]
		[TestCase(TypeIconKinds.PersonnelRole, "s3")]
		public async Task UploadVariantIconAsync_stores_the_icon_in_its_slot_and_replaces_only_that_slot(TypeIconKinds kind, string variant)
		{
			var repository = new Mock<ITypeIconsRepository>();
			var cache = new Mock<Resgrid.Model.Providers.ICacheProvider>();
			TypeIcon inserted = null;
			repository.Setup(x => x.TypeBelongsToDepartmentAsync(DepartmentId, kind, TypeId)).ReturnsAsync(true);
			repository.Setup(x => x.InsertAsync(It.IsAny<TypeIcon>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((TypeIcon i, CancellationToken _, bool __) => inserted = i).ReturnsAsync((TypeIcon i, CancellationToken _, bool __) => i);

			var key = await new TypeIconsService(repository.Object, cache.Object).UploadVariantIconAsync(DepartmentId, kind, TypeId, variant, "admin", Png(48, 48, new Rgba32(0, 120, 0, 255)));

			key.Should().Be(inserted.IconKey);
			inserted.Variant.Should().Be(variant);
			repository.Verify(x => x.DeleteAllButNewestInSlotAsync(DepartmentId, kind, TypeId, variant, It.IsAny<CancellationToken>()), Times.Once);
			repository.Verify(x => x.DeleteSlotAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"keeping \"this upload\" would let two uploads at once delete each other's row");
			repository.Verify(x => x.SetTypeIconKeyAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"a status or priority icon is never mirrored on the type");
			cache.Verify(x => x.RemoveAsync($"TypeIconVariants_{DepartmentId}"), Times.Once, "the map picks up the new icon at once");
		}

		[TestCase(TypeIconKinds.PoiType, "s1")]
		[TestCase(TypeIconKinds.CallType, "s1")]
		[TestCase(TypeIconKinds.UnitType, "p2")]
		[TestCase(TypeIconKinds.UnitType, "o0")]
		[TestCase(TypeIconKinds.UnitType, "s05")]
		[TestCase(TypeIconKinds.UnitType, "x1")]
		[TestCase(TypeIconKinds.UnitType, null)]
		public async Task UploadVariantIconAsync_refuses_a_slot_the_kind_cannot_have(TypeIconKinds kind, string variant)
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.TypeBelongsToDepartmentAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>())).ReturnsAsync(true);

			(await Service(repository.Object).UploadVariantIconAsync(DepartmentId, kind, TypeId, variant, "admin", Png(48, 48, new Rgba32(0, 0, 0, 255)))).Should().BeNull();

			repository.Verify(x => x.InsertAsync(It.IsAny<TypeIcon>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task UploadVariantIconAsync_for_another_departments_type_stores_nothing()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.TypeBelongsToDepartmentAsync(DepartmentId, TypeIconKinds.UnitType, TypeId)).ReturnsAsync(false);

			(await Service(repository.Object).UploadVariantIconAsync(DepartmentId, TypeIconKinds.UnitType, TypeId, "s5", "admin", Png(48, 48, new Rgba32(0, 0, 0, 255)))).Should().BeNull();

			repository.Verify(x => x.InsertAsync(It.IsAny<TypeIcon>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task RemoveVariantIconAsync_deletes_only_that_slot_of_the_departments_type()
		{
			var repository = new Mock<ITypeIconsRepository>();
			repository.Setup(x => x.TypeBelongsToDepartmentAsync(DepartmentId, TypeIconKinds.PersonnelRole, TypeId)).ReturnsAsync(true);

			(await Service(repository.Object).RemoveVariantIconAsync(DepartmentId, TypeIconKinds.PersonnelRole, TypeId, "o12")).Should().BeTrue();
			(await Service(repository.Object).RemoveVariantIconAsync(DepartmentId, TypeIconKinds.PersonnelRole, TypeId + 1, "o12")).Should().BeFalse();

			repository.Verify(x => x.DeleteSlotAsync(DepartmentId, TypeIconKinds.PersonnelRole, TypeId, "o12", null, It.IsAny<CancellationToken>()), Times.Once);
			repository.Verify(x => x.DeleteSlotAsync(DepartmentId, TypeIconKinds.PersonnelRole, TypeId + 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private static TypeIconsService Service(ITypeIconsRepository repository) =>
			new TypeIconsService(repository, new Mock<Resgrid.Model.Providers.ICacheProvider>().Object);

		[Test]
		public void An_unknown_kind_is_refused()
		{
			var service = Service(new Mock<ITypeIconsRepository>().Object);

			Func<Task> act = () => service.RemoveIconAsync(DepartmentId, (TypeIconKinds)99, TypeId);

			act.Should().ThrowAsync<ArgumentOutOfRangeException>();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("../../etc/passwd")]
		[TestCase("0123456789ABCDEF0123456789ABCDEF")]
		[TestCase("0123456789abcdef")]
		public async Task GetIconByKeyAsync_refuses_malformed_keys_without_a_query(string key)
		{
			var repository = new Mock<ITypeIconsRepository>();

			var icon = await Service(repository.Object).GetIconByKeyAsync(key);

			icon.Should().BeNull();
			repository.Verify(x => x.GetByIconKeyAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public void ValidateIcon_returns_the_rejection_reason_or_none()
		{
			var service = Service(new Mock<ITypeIconsRepository>().Object);

			service.ValidateIcon(Png(32, 32, new Rgba32(0, 0, 0, 255))).Should().Be(TypeIconRejections.None);
			service.ValidateIcon(Encoding.UTF8.GetBytes("not an image")).Should().Be(TypeIconRejections.Unreadable);
			service.ValidateIcon(Png(8, 8, new Rgba32(0, 0, 0, 255))).Should().Be(TypeIconRejections.TooSmall);
			service.ValidateIcon(null).Should().Be(TypeIconRejections.Required);
		}

		[Test]
		public void The_limits_the_picker_shows_are_the_ones_the_server_applies()
		{
			TypeIconRenditions.MaxBytes.Should().Be(TypeIcons.MaxUploadBytes).And.Be(1024 * 1024);
			TypeIconRenditions.MinimumEdge.Should().Be(TypeIcons.MinimumEdge);
			TypeIconRenditions.MaximumEdge.Should().Be(TypeIcons.MaximumEdge);
			TypeIconRenditions.Size.Should().Be(TypeIcons.StoredSize);
		}

		#endregion Service

		#region URLs

		[Test]
		public void Urls_use_the_uploaded_key_and_the_lowercase_built_in_asset_name()
		{
			var unitType = new UnitType { IconKey = Key, MapIconType = (int)MapIconTypes.CarTwo };
			var callType = new CallType { MapIconType = (int)MapIconTypes.CarAccident };
			var poiType = new PoiType { IconKey = Key };

			TypeIcons.ApiIconUrl("https://api.example.test/", unitType).Should().Be("https://api.example.test/api/v4/TypeIcons/" + Key);
			TypeIcons.ApiIconUrl("https://api.example.test", poiType).Should().Be("https://api.example.test/api/v4/TypeIcons/" + Key);
			TypeIcons.ApiIconUrl("https://api.example.test", callType).Should().BeNull();
			TypeIcons.WebIconUrl(Key).Should().Be("/TypeIcons/" + Key);
			TypeIcons.BuiltInIconName(unitType).Should().Be("cartwo", "the asset is cartwo.png on the web and in the apps");
			TypeIcons.BuiltInIconName(callType).Should().Be("caraccident");
		}

		[Test]
		public void Urls_are_null_when_the_type_has_no_icon()
		{
			var type = new UnitType { MapIconType = -1 };

			TypeIcons.ApiIconUrl("https://api.example.test", type).Should().BeNull();
			TypeIcons.WebIconUrl(type.IconKey).Should().BeNull();
			TypeIcons.BuiltInIconName(type).Should().BeNull();
			TypeIcons.BuiltInIconName((UnitType)null).Should().BeNull();
			TypeIcons.BuiltInIconName((CallType)null).Should().BeNull();
		}

		[Test]
		public void Every_built_in_unit_and_call_icon_name_has_a_web_asset()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			directory.Should().NotBeNull();

			for (var icon = MapIconTypes.Search; icon <= MapIconTypes.Watercraft; icon++)
			{
				var name = TypeIcons.BuiltInIconName(new UnitType { MapIconType = (int)icon });
				System.IO.File.Exists(Path.Combine(directory!.FullName, "Web", "Resgrid.Web", "wwwroot", "images", "Mapping", name + ".png"))
					.Should().BeTrue($"{icon} maps to {name}.png, which the web map and the type lists load");
			}
		}

		#endregion URLs

		#region Role order

		[Test]
		public void InRoleOrder_sorts_by_sort_order_then_name_then_id()
		{
			var roles = new[]
			{
				new PersonnelRole { PersonnelRoleId = 4, Name = "medic", SortOrder = 2 },
				new PersonnelRole { PersonnelRoleId = 3, Name = "Officer", SortOrder = 1 },
				new PersonnelRole { PersonnelRoleId = 2, Name = "Driver", SortOrder = 2 },
				new PersonnelRole { PersonnelRoleId = 1, Name = "Driver", SortOrder = 2 },
				null
			};

			TypeIcons.InRoleOrder(roles).Select(r => r.PersonnelRoleId).Should().Equal(3, 1, 2, 4);
			TypeIcons.InRoleOrder(null).Should().BeEmpty();
		}

		[Test]
		public void MapIconRole_is_the_first_held_role_in_order_that_has_an_icon()
		{
			var firefighter = new PersonnelRole { PersonnelRoleId = 1, Name = "Firefighter", SortOrder = 3, IconKey = Key };
			var officer = new PersonnelRole { PersonnelRoleId = 2, Name = "Officer", SortOrder = 1 };
			var paramedic = new PersonnelRole { PersonnelRoleId = 3, Name = "Paramedic", SortOrder = 2, IconKey = "fedcba9876543210fedcba9876543210" };

			TypeIcons.MapIconRole(new[] { firefighter, officer, paramedic }).Should().BeSameAs(paramedic,
				"the officer role comes first but has no icon, so the next role in order with one is used");
			TypeIcons.MapIconRole(new[] { firefighter, officer }).Should().BeSameAs(firefighter);
			TypeIcons.MapIconRole(new[] { officer }).Should().BeNull("with no role icon the map keeps the default person pin");
			TypeIcons.ApiIconUrl("https://api.example.test", TypeIcons.MapIconRole(new[] { officer })).Should().BeNull();
			TypeIcons.ApiIconUrl("https://api.example.test", firefighter).Should().Be("https://api.example.test/api/v4/TypeIcons/" + Key);
		}

		[Test]
		public async Task SetRoleOrderAsync_writes_the_listed_roles_first_and_keeps_the_rest_after_them()
		{
			var roles = new Mock<IPersonnelRolesRepository>();
			roles.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new[]
			{
				new PersonnelRole { PersonnelRoleId = 10, DepartmentId = DepartmentId, Name = "Captain", SortOrder = 0 },
				new PersonnelRole { PersonnelRoleId = 11, DepartmentId = DepartmentId, Name = "Driver", SortOrder = 0 },
				new PersonnelRole { PersonnelRoleId = 12, DepartmentId = DepartmentId, Name = "EMT", SortOrder = 0 }
			});
			IReadOnlyList<int> written = null;
			roles.Setup(x => x.SetRoleSortOrdersAsync(DepartmentId, It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
				.Callback((int _, IReadOnlyList<int> order, CancellationToken __) => written = order).ReturnsAsync(3);

			var saved = await RolesService(roles).SetRoleOrderAsync(DepartmentId, new[] { 12, 999, 12, 10 });

			saved.Should().BeTrue();
			written.Should().Equal(new[] { 12, 10, 11 }, "another department's id and the repeat are dropped, and Driver keeps its place after the listed roles");
		}

		[Test]
		public async Task A_new_role_goes_to_the_end_once_the_department_has_ordered_its_roles()
		{
			var roles = new Mock<IPersonnelRolesRepository>();
			roles.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new[]
			{
				new PersonnelRole { PersonnelRoleId = 10, DepartmentId = DepartmentId, Name = "Captain", SortOrder = 1 },
				new PersonnelRole { PersonnelRoleId = 11, DepartmentId = DepartmentId, Name = "Driver", SortOrder = 2 }
			});
			roles.Setup(x => x.SaveOrUpdateAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((PersonnelRole r, CancellationToken _, bool __) => r);

			var saved = await RolesService(roles).SaveRoleAsync(new PersonnelRole { DepartmentId = DepartmentId, Name = "EMT" });

			saved.SortOrder.Should().Be(3);
		}

		[Test]
		public async Task A_new_role_keeps_order_0_while_the_department_has_never_ordered_its_roles()
		{
			var roles = new Mock<IPersonnelRolesRepository>();
			roles.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new[]
			{
				new PersonnelRole { PersonnelRoleId = 10, DepartmentId = DepartmentId, Name = "Captain", SortOrder = 0 }
			});
			roles.Setup(x => x.SaveOrUpdateAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((PersonnelRole r, CancellationToken _, bool __) => r);

			var saved = await RolesService(roles).SaveRoleAsync(new PersonnelRole { DepartmentId = DepartmentId, Name = "EMT" });

			saved.SortOrder.Should().Be(0, "unordered roles list by name, so the new one takes its alphabetical place");
		}

		[Test]
		public async Task DeleteRoleByIdAsync_deletes_the_roles_uploaded_icon()
		{
			var roles = new Mock<IPersonnelRolesRepository>();
			var role = new PersonnelRole { PersonnelRoleId = TypeId, DepartmentId = DepartmentId, Name = "Paramedic" };
			roles.Setup(x => x.GetRoleByRoleIdAsync(TypeId)).ReturnsAsync(role);
			roles.Setup(x => x.DeleteAsync(role, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var icons = new Mock<ITypeIconsRepository>();

			(await RolesService(roles, icons).DeleteRoleByIdAsync(TypeId)).Should().BeTrue();

			icons.Verify(x => x.DeleteForTypeAsync(DepartmentId, TypeIconKinds.PersonnelRole, TypeId, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		private static PersonnelRolesService RolesService(Mock<IPersonnelRolesRepository> roles, Mock<ITypeIconsRepository> icons = null) =>
			new PersonnelRolesService(roles.Object, new Mock<IPersonnelRoleUsersRepository>().Object, new Mock<ISubscriptionsService>().Object,
				new Mock<IDepartmentMembersRepository>().Object, null, new Mock<IUnitOfWork>().Object, typeIconsRepository: (icons ?? new Mock<ITypeIconsRepository>()).Object);

		#endregion Role order

		#region Type deletes

		[Test]
		public async Task DeleteUnitTypeAsync_deletes_the_types_uploaded_icon()
		{
			var unitTypes = new Mock<IUnitTypesRepository>();
			var type = new UnitType { UnitTypeId = TypeId, DepartmentId = DepartmentId, Type = "MUG" };
			unitTypes.Setup(x => x.GetByIdAsync(TypeId)).ReturnsAsync(type);
			unitTypes.Setup(x => x.DeleteAsync(type, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var icons = new Mock<ITypeIconsRepository>();

			var service = new UnitsService(null, null, null, unitTypes.Object, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
				typeIconsRepository: icons.Object);

			(await service.DeleteUnitTypeAsync(TypeId)).Should().BeTrue();
			icons.Verify(x => x.DeleteForTypeAsync(DepartmentId, TypeIconKinds.UnitType, TypeId, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task DeletePOITypeAsync_deletes_the_types_uploaded_icon()
		{
			var poiTypes = new Mock<IPoiTypesRepository>();
			var type = new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId, Name = "Hospitals" };
			poiTypes.Setup(x => x.GetPoiTypeByTypeIdAsync(TypeId)).ReturnsAsync(type);
			poiTypes.Setup(x => x.DeleteAsync(type, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var icons = new Mock<ITypeIconsRepository>();

			var service = new MappingService(poiTypes.Object, null, null, null, null, icons.Object);

			(await service.DeletePOITypeAsync(TypeId)).Should().BeTrue();
			icons.Verify(x => x.DeleteForTypeAsync(DepartmentId, TypeIconKinds.PoiType, TypeId, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task DeleteCallTypeAsync_deletes_the_types_uploaded_icon()
		{
			var callTypes = new Mock<ICallTypesRepository>();
			var type = new CallType { CallTypeId = TypeId, DepartmentId = DepartmentId, Type = "Structure Fire" };
			callTypes.Setup(x => x.GetByIdAsync(TypeId)).ReturnsAsync(type);
			callTypes.Setup(x => x.DeleteAsync(type, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var icons = new Mock<ITypeIconsRepository>();

			(await CallsService(callTypes, icons).DeleteCallTypeAsync(TypeId)).Should().BeTrue();
			icons.Verify(x => x.DeleteForTypeAsync(DepartmentId, TypeIconKinds.CallType, TypeId, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase(TypeIconKinds.UnitType)]
		[TestCase(TypeIconKinds.CallType)]
		[TestCase(TypeIconKinds.PoiType)]
		public async Task A_type_delete_that_fails_keeps_the_types_uploaded_icon(TypeIconKinds kind)
		{
			var icons = new Mock<ITypeIconsRepository>();
			bool deleted;

			switch (kind)
			{
				case TypeIconKinds.UnitType:
					var unitTypes = new Mock<IUnitTypesRepository>();
					var unitType = new UnitType { UnitTypeId = TypeId, DepartmentId = DepartmentId, Type = "MUG" };
					unitTypes.Setup(x => x.GetByIdAsync(TypeId)).ReturnsAsync(unitType);
					unitTypes.Setup(x => x.DeleteAsync(unitType, It.IsAny<CancellationToken>())).ReturnsAsync(false);
					deleted = await new UnitsService(null, null, null, unitTypes.Object, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
						typeIconsRepository: icons.Object).DeleteUnitTypeAsync(TypeId);
					break;
				case TypeIconKinds.CallType:
					var callTypes = new Mock<ICallTypesRepository>();
					var callType = new CallType { CallTypeId = TypeId, DepartmentId = DepartmentId, Type = "Structure Fire" };
					callTypes.Setup(x => x.GetByIdAsync(TypeId)).ReturnsAsync(callType);
					callTypes.Setup(x => x.DeleteAsync(callType, It.IsAny<CancellationToken>())).ReturnsAsync(false);
					deleted = await CallsService(callTypes, icons).DeleteCallTypeAsync(TypeId);
					break;
				default:
					var poiTypes = new Mock<IPoiTypesRepository>();
					var poiType = new PoiType { PoiTypeId = TypeId, DepartmentId = DepartmentId, Name = "Hospitals" };
					poiTypes.Setup(x => x.GetPoiTypeByTypeIdAsync(TypeId)).ReturnsAsync(poiType);
					poiTypes.Setup(x => x.DeleteAsync(poiType, It.IsAny<CancellationToken>())).ReturnsAsync(false);
					deleted = await new MappingService(poiTypes.Object, null, null, null, null, icons.Object).DeletePOITypeAsync(TypeId);
					break;
			}

			deleted.Should().BeFalse();
			icons.Verify(x => x.DeleteForTypeAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"the type is still there and its IconKey still points at the icon, so the icon has to stay too");
		}

		private static CallsService CallsService(Mock<ICallTypesRepository> callTypes, Mock<ITypeIconsRepository> icons) =>
			new CallsService(null, null, null, callTypes.Object, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
				typeIconsRepository: icons.Object);

		#endregion Type deletes

		private static byte[] Png(int width, int height, Rgba32 color)
		{
			using var image = new Image<Rgba32>(width, height, color);
			using var stream = new MemoryStream();
			image.Save(stream, new PngEncoder());
			return stream.ToArray();
		}
	}
}
