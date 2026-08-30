using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Catalog v8 brings moderation into the protected-field catalog (plan 5.3). A moderation record
	/// is a verbatim copy of the worst content a department holds — the reported message, the file
	/// that came with it, the reporter's words and the moderator's account of why. Leaving it in the
	/// clear would mean a protected department encrypts the original and keeps a plaintext duplicate
	/// one table over, reachable by anyone who can open the queue.
	/// </summary>
	[TestFixture]
	public class ModerationProtectionTests
	{
		private static readonly string[] ModerationTables =
		{
			"ModerationRequests", "ModerationReports", "ModerationActions",
			"ChatMessageFlags", "ChatModerationActions", "ChatExports"
		};

		private ProtectedFieldCatalog _catalog;

		[SetUp]
		public void SetUp() => _catalog = new ProtectedFieldCatalog();

		[Test]
		public void Moderation_is_what_moved_the_catalog_to_eight()
		{
			_catalog.Version.Should().BeGreaterThanOrEqualTo(8);

			_catalog.GetAddedBetween(7, 8).Select(e => e.FieldId)
				.Should().BeEquivalentTo(new[]
				{
					"moderationrequests.originalsubject",
					"moderationrequests.originaltext",
					"moderationrequests.originalfilename",
					"moderationrequests.originalcontenttype",
					"moderationrequests.originalcontent",
					"moderationrequests.originalmetadatajson",
					"moderationrequests.adminnote",
					"moderationreports.note",
					"moderationactions.note",
					"moderationactions.detailsjson",
					"moderationactions.evidencetext",
					"moderationactions.evidencecontent",
					"moderationactions.evidencemetadatajson",
					"chatmessageflags.note",
					"chatmessageflags.resolutionnote",
					"chatmoderationactions.reason",
					"chatmoderationactions.detailsjson",
					"chatexports.data",
					"chatexports.error"
				});
		}

		/// <summary>
		/// The moderation queue has to stay usable without a grant: a moderator triages by status,
		/// reason CODE and counts long before they need to read the excerpt. Those columns are
		/// structural (section 5.4) and must never be encrypted, or the queue itself stops working
		/// for a protected department.
		/// </summary>
		[Test]
		public void Triage_columns_stay_plaintext()
		{
			var fieldIds = _catalog.GetAll().Select(e => e.FieldId).ToList();

			foreach (var structural in new[]
			{
				"moderationrequests.status", "moderationrequests.disposition", "moderationrequests.itemtype",
				"moderationreports.reason", "chatmessageflags.reason", "chatmessageflags.status",
				"chatexports.status", "chatexports.format"
			})
			{
				fieldIds.Should().NotContain(structural);
			}
		}

		/// <summary>
		/// The action row records WHO moderated and from where. That is the audit trail used to
		/// investigate abuse of the moderation tools themselves, it is not the reported content, and
		/// encrypting it would blind the very trail it exists to provide (plan 5.4).
		/// </summary>
		[Test]
		public void The_moderator_audit_trail_stays_readable()
		{
			var fieldIds = _catalog.GetAll().Select(e => e.FieldId).ToList();

			foreach (var audit in new[]
			{
				"moderationactions.actorrole", "moderationactions.ipaddress", "moderationactions.useragent",
				"moderationactions.traceid", "moderationactions.servername"
			})
			{
				fieldIds.Should().NotContain(audit);
			}

			ProtectedReadService.ModerationActionFieldAccessors.Keys
				.Should().NotContain("moderationactions.ipaddress");
		}

		[Test]
		public void Every_moderation_table_is_scoped_by_its_own_department_column_and_marked()
		{
			foreach (var table in ModerationTables)
			{
				var binding = AdpTableBindings.V1.Single(b => b.TableName == table);

				binding.DepartmentColumn.Should().Be("DepartmentId",
					$"{table} carries its own department, so the sweep never has to join to find the owner");
				binding.ParentTable.Should().BeNull();
				binding.ProtectedMarkerColumn.Should().Be("IsProtected", "M0139 added the marker to every one of them");
			}
		}

		[Test]
		public void The_reported_file_and_the_export_archive_are_bound_as_binary()
		{
			var binaries = AdpTableBindings.V1
				.Where(b => ModerationTables.Contains(b.TableName))
				.SelectMany(b => b.Columns)
				.Where(c => c.StorageKind == ProtectedFieldStorageKind.Binary)
				.Select(c => c.FieldId)
				.ToList();

			binaries.Should().BeEquivalentTo(new[]
			{
				"moderationrequests.originalcontent",
				"moderationactions.evidencecontent",
				"chatexports.data"
			}, "an rgdpb envelope, not a text one - these are files and archives");
		}

		[Test]
		public void A_catalog_upgrade_from_seven_touches_only_the_moderation_tables()
		{
			var scoped = AdpTableBindings.ForVersionRange(_catalog, 7, 8);

			scoped.Select(b => b.TableName).Should().BeEquivalentTo(ModerationTables,
				"an upgrade sweep must not re-read a table it has nothing to do in");
		}
	}
}
