using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ProtectedFieldCatalogTests
	{
		private ProtectedFieldCatalog _catalog;

		[SetUp]
		public void SetUp()
		{
			_catalog = new ProtectedFieldCatalog();
		}

		[Test]
		public void Field_ids_are_unique_and_lowercase()
		{
			var all = _catalog.GetAll();

			all.Should().NotBeEmpty();
			all.Select(e => e.FieldId).Should().OnlyHaveUniqueItems(
				"FieldIds are AAD components and must never collide");
			all.Should().OnlyContain(e => e.FieldId == e.FieldId.ToLowerInvariant(),
				"FieldIds are stable lowercase identifiers");
			all.Should().OnlyContain(e => e.FieldId == $"{e.TableName.ToLowerInvariant()}.{e.ColumnName.ToLowerInvariant()}",
				"FieldId convention is table.column so ids stay derivable and collision-free");
		}

		[Test]
		public void P0_families_are_present()
		{
			_catalog.GetById("calls.name").Should().NotBeNull();
			_catalog.GetById("calls.natureofcall").Classification.Should().Be(ProtectedFieldClassification.Phi);
			_catalog.GetById("calllogs.narrative").Should().NotBeNull();
			_catalog.GetById("contacts.email").Should().NotBeNull();
			_catalog.GetById("contactnotes.note").Should().NotBeNull();
			_catalog.GetById("departmentmembersensitivedata.identificationnumber").Should().NotBeNull();
		}

		[Test]
		public void Storage_kinds_match_column_types()
		{
			_catalog.GetById("callattachments.data").StorageKind.Should().Be(ProtectedFieldStorageKind.Binary);
			_catalog.GetById("contacts.image").StorageKind.Should().Be(ProtectedFieldStorageKind.Binary);
			_catalog.GetById("callnotes.latitude").StorageKind.Should().Be(ProtectedFieldStorageKind.CompanionColumn);
			_catalog.GetById("callnotes.longitude").StorageKind.Should().Be(ProtectedFieldStorageKind.CompanionColumn);
			_catalog.GetById("calls.notes").StorageKind.Should().Be(ProtectedFieldStorageKind.Text);
		}

		[Test]
		public void Table_lookup_is_case_insensitive()
		{
			_catalog.GetForTable("CALLS").Should().NotBeEmpty();
			_catalog.GetForTable("calls").Select(e => e.FieldId)
				.Should().BeEquivalentTo(_catalog.GetForTable("Calls").Select(e => e.FieldId));
			_catalog.IsProtectedField("calls", "NAME").Should().BeTrue();
			_catalog.IsProtectedField("Calls", "Number").Should().BeFalse(
				"the system-generated call number stays plaintext");
			_catalog.GetForTable("NoSuchTable").Should().BeEmpty();
		}

		[Test]
		public void Permissions_follow_family_boundaries()
		{
			_catalog.GetForTable("Calls").Should().OnlyContain(e => e.ViewPermission == PermissionTypes.ViewProtectedCallData);
			_catalog.GetForTable("Calls").Should().OnlyContain(e => e.EditPermission == PermissionTypes.EditProtectedCallData);
			_catalog.GetForTable("Contacts").Should().OnlyContain(e => e.ViewPermission == PermissionTypes.ViewProtectedContactData);
			_catalog.GetForTable("DepartmentMemberSensitiveData")
				.Should().OnlyContain(e => e.ViewPermission == PermissionTypes.ViewProtectedPersonnelData);
		}

		[Test]
		public void Version_tracks_the_highest_entry_version()
		{
			// The constant must never lag the data: version-scoped queries and the upgrade work list
			// both key off it, so a hand-maintained value that fell behind would quietly hand a v1
			// department fields it does not own.
			_catalog.Version.Should().Be(_catalog.GetAll().Max(e => e.AddedInCatalogVersion));
			_catalog.GetAll().Should().OnlyContain(e => e.AddedInCatalogVersion >= 1);
		}

		[Test]
		public void Catalog_versions_are_contiguous_from_one()
		{
			// A gap (v1 then v3) would leave an upgrade range that sweeps nothing while still
			// bumping a department's pinned version past fields that were never encrypted.
			var versions = _catalog.GetAll().Select(e => e.AddedInCatalogVersion).Distinct().OrderBy(v => v).ToList();

			versions.Should().BeEquivalentTo(Enumerable.Range(1, _catalog.Version));
		}
	
		/// <summary>
		/// Version scoping is what keeps a department that enrolled under an older catalog from
		/// silently starting to encrypt fields added later: its AAD is computed from its pinned
		/// version and those rows were never swept. AddedInCatalogVersion drives all of it.
		/// </summary>
		[Test]
		public void Version_scoping_returns_only_what_a_pinned_department_owns()
		{
			var catalog = new ProtectedFieldCatalog();

			catalog.GetAllForVersion(catalog.Version).Should().BeEquivalentTo(catalog.GetAll(),
				"a current department owns the whole catalog");
			catalog.GetAllForVersion(catalog.Version + 5).Should().BeEquivalentTo(catalog.GetAll(),
				"a version beyond the code's catalog still owns only what exists");
			catalog.GetAllForVersion(0).Should().BeEmpty("an unstamped department owns nothing");
			catalog.GetAllForVersion(-1).Should().BeEmpty();

			catalog.GetAllForVersion(1).Should().OnlyContain(e => e.AddedInCatalogVersion <= 1);
		}

		[Test]
		public void Version_scoping_applies_per_table_too()
		{
			var catalog = new ProtectedFieldCatalog();

			catalog.GetForTableAndVersion("Calls", catalog.Version)
				.Should().BeEquivalentTo(catalog.GetForTable("Calls"));
			catalog.GetForTableAndVersion("Calls", 0).Should().BeEmpty();
			catalog.GetForTableAndVersion("NoSuchTable", catalog.Version).Should().BeEmpty();
		}

		[Test]
		public void Added_between_is_the_exact_upgrade_work_list()
		{
			var catalog = new ProtectedFieldCatalog();

			catalog.GetAddedBetween(catalog.Version, catalog.Version)
				.Should().BeEmpty("a current department is owed no upgrade sweep");
			catalog.GetAddedBetween(catalog.Version + 1, catalog.Version)
				.Should().BeEmpty("a backwards range is not an upgrade");

			// Everything in v1 is 'added between 0 and 1' — the enrollment sweep's own work list.
			catalog.GetAddedBetween(0, 1).Should().OnlyContain(e => e.AddedInCatalogVersion == 1);
			catalog.GetAddedBetween(0, catalog.Version).Should().BeEquivalentTo(catalog.GetAll(),
				"a department starting from nothing is owed every cataloged field");
		}
}
}
