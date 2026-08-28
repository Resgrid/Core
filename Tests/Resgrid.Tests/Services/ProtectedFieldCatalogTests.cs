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
		public void Version_is_one_and_all_entries_belong_to_it()
		{
			_catalog.Version.Should().Be(1);
			_catalog.GetAll().Should().OnlyContain(e => e.AddedInCatalogVersion == 1);
		}
	}
}
