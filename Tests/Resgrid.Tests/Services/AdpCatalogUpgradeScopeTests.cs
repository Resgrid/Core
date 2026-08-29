using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A catalog upgrade must sweep ONLY the fields added since the department's pinned version.
	/// Re-running the whole catalog would be both wasteful and wrong: existing envelopes are already
	/// protected, and (since the catalog version is not an AAD component) they do not need rewriting
	/// when the version advances.
	/// </summary>
	[TestFixture]
	public class AdpCatalogUpgradeScopeTests
	{
		private ProtectedFieldCatalog _catalog;

		[SetUp]
		public void SetUp() => _catalog = new ProtectedFieldCatalog();

		[Test]
		public void A_current_department_has_nothing_to_sweep()
		{
			AdpTableBindings.ForVersionRange(_catalog, _catalog.Version, _catalog.Version)
				.Should().BeEmpty("a department already at the current catalog is owed no upgrade");
		}

		[Test]
		public void A_backwards_or_null_range_sweeps_nothing()
		{
			AdpTableBindings.ForVersionRange(_catalog, _catalog.Version, _catalog.Version - 1).Should().BeEmpty();
			AdpTableBindings.ForVersionRange(null, 0, _catalog.Version).Should().BeEmpty();
		}

		[Test]
		public void A_department_starting_from_nothing_sweeps_the_whole_catalog()
		{
			var scoped = AdpTableBindings.ForVersionRange(_catalog, 0, _catalog.Version);

			scoped.Select(b => b.TableName)
				.Should().BeEquivalentTo(AdpTableBindings.V1.Select(b => b.TableName));

			scoped.SelectMany(b => b.Columns).Select(c => c.FieldId)
				.Should().BeEquivalentTo(AdpTableBindings.V1.SelectMany(b => b.Columns).Select(c => c.FieldId));
		}

		[Test]
		public void Scoped_bindings_preserve_addressing_and_the_protected_marker()
		{
			var scoped = AdpTableBindings.ForVersionRange(_catalog, 0, _catalog.Version);

			foreach (var original in AdpTableBindings.V1)
			{
				var rebuilt = scoped.Single(b => b.TableName == original.TableName);

				rebuilt.PkColumn.Should().Be(original.PkColumn);
				rebuilt.PkIsNumeric.Should().Be(original.PkIsNumeric);
				rebuilt.DepartmentColumn.Should().Be(original.DepartmentColumn);
				rebuilt.ParentFkColumn.Should().Be(original.ParentFkColumn);
				rebuilt.ParentTable.Should().Be(original.ParentTable);
				rebuilt.ParentPkColumn.Should().Be(original.ParentPkColumn);
				rebuilt.ProtectedMarkerColumn.Should().Be(original.ProtectedMarkerColumn,
					"a scoped sweep still has to stamp the row-level protected marker");
			}
		}

		[Test]
		public void Every_bound_column_is_a_real_catalog_field()
		{
			// The binding field ids ARE the AAD components; a typo here would encrypt under an id
			// the catalog never issued and no read path could ever resolve.
			foreach (var column in AdpTableBindings.V1.SelectMany(b => b.Columns))
			{
				_catalog.GetById(column.FieldId)
					.Should().NotBeNull($"binding field id '{column.FieldId}' must exist in the catalog");
			}
		}

		[Test]
		public void Catalog_upgrade_is_a_distinct_migration_kind()
		{
			((int)DepartmentDataProtectionMigrationKind.CatalogUpgrade).Should().Be(3,
				"the persisted value is a stored discriminator and must stay stable");
		}
	}
}
