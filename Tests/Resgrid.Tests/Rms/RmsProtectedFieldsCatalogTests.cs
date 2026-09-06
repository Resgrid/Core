using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Pins the three-way agreement the RMS ADP integration rests on (RMS plan section 5.9.2, ADP catalog v10):
	/// every field the write seam can encrypt is in the catalog, every cataloged RMS field has a migration
	/// binding, and the catalog version the cutover preflight keys on is the one those entries were added in.
	/// </summary>
	[TestFixture]
	public class RmsProtectedFieldsCatalogTests
	{
		[Test]
		public void Every_rms_seam_field_is_cataloged_at_version_10()
		{
			var catalog = new ProtectedFieldCatalog();
			var entries = catalog.GetAll().Where(e => e.Family == RmsProtectedFields.Family).ToDictionary(e => e.FieldId);

			ProtectedFieldCatalog.RecordsCatalogVersion.Should().Be(10);
			catalog.Version.Should().BeGreaterThanOrEqualTo(ProtectedFieldCatalog.RecordsCatalogVersion);

			foreach (var fieldId in RmsProtectedFields.AllFieldIds())
			{
				entries.Should().ContainKey(fieldId, $"the write seam can encrypt {fieldId}, so the catalog must own it");
				entries[fieldId].AddedInCatalogVersion.Should().Be(ProtectedFieldCatalog.RecordsCatalogVersion, $"{fieldId} ships with the RMS catalog bump");
			}
		}

		[Test]
		public void Every_cataloged_rms_field_has_a_seam_and_a_migration_binding()
		{
			var catalog = new ProtectedFieldCatalog();
			var seam = RmsProtectedFields.AllFieldIds().ToHashSet();
			var bindings = AdpTableBindings.V1.Select(b => b.TableName.ToLowerInvariant()).ToHashSet();

			foreach (var entry in catalog.GetAll().Where(e => e.Family == RmsProtectedFields.Family))
			{
				seam.Should().Contain(entry.FieldId, $"a cataloged column without a write seam would ship plaintext: {entry.FieldId}");
				bindings.Should().Contain(entry.TableName.ToLowerInvariant(), $"the enrollment sweep needs a binding for {entry.TableName}");
			}
		}

		[Test]
		public void Field_ids_follow_the_table_dot_column_convention_and_never_collide()
		{
			var ids = RmsProtectedFields.AllFieldIds().ToList();
			ids.Should().OnlyHaveUniqueItems();
			ids.Should().OnlyContain(id => id == id.ToLowerInvariant() && id.Contains('.'));
			ids.Should().Contain("rmsoperationalrecorddetails.narrative").And.Contain("rmsrecordattachments.data").And.Contain("rmsexportruns.data");
		}
	}
}
