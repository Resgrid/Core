using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Catalog v9 closes the plan's candidate list: unit log narratives, user state notes, calendar
	/// entries, department documents, and the stored mailbox credentials from section 22.1. Nothing
	/// the plan named as a protected-field candidate is left unbound after this.
	/// </summary>
	[TestFixture]
	public class RemainingCandidateProtectionTests
	{
		private ProtectedFieldCatalog _catalog;

		[SetUp]
		public void SetUp() => _catalog = new ProtectedFieldCatalog();

		[Test]
		public void The_catalog_is_at_version_nine_and_the_last_candidates_are_what_moved_it()
		{
			_catalog.Version.Should().Be(9);

			_catalog.GetAddedBetween(8, 9).Select(e => e.FieldId)
				.Should().BeEquivalentTo(new[]
				{
					"unitlogs.narrative",
					"userstates.note",
					"calendaritems.title",
					"calendaritems.description",
					"calendaritems.location",
					"documents.name",
					"documents.description",
					"documents.filename",
					"documents.data",
					"distributionlists.username",
					"distributionlists.password"
				});
		}

		/// <summary>
		/// A unit log has no DepartmentId of its own, so it derives ownership through its unit — the
		/// same shape UnitStates uses. Everything else in this wave carries its own.
		/// </summary>
		[Test]
		public void Unit_logs_derive_their_department_through_the_unit()
		{
			var binding = AdpTableBindings.V1.Single(b => b.TableName == "UnitLogs");

			binding.ParentTable.Should().Be("Units");
			binding.ParentFkColumn.Should().Be("UnitId");
			binding.DepartmentColumn.Should().BeNull("the row has no department column to scope by");
		}

		/// <summary>
		/// A calendar has to lay out for a protected department without anyone stepping up: the
		/// scheduling columns are structural and must never be encrypted. Only what a human wrote —
		/// title, description, location — is protected.
		/// </summary>
		[Test]
		public void Calendar_scheduling_columns_stay_plaintext()
		{
			var fieldIds = _catalog.GetAll().Select(e => e.FieldId).ToList();

			foreach (var structural in new[]
			{
				"calendaritems.start", "calendaritems.end", "calendaritems.starttimezone",
				"calendaritems.endtimezone", "calendaritems.recurrencerule", "calendaritems.recurrenceid"
			})
			{
				fieldIds.Should().NotContain(structural);
			}
		}

		[Test]
		public void The_document_payload_is_bound_as_binary_with_its_name()
		{
			var columns = AdpTableBindings.V1.Single(b => b.TableName == "Documents").Columns;

			columns.Single(c => c.StorageKind == ProtectedFieldStorageKind.Binary)
				.FieldId.Should().Be("documents.data");
			columns.Select(c => c.FieldId).Should().Contain("documents.filename",
				"protecting the file while serving its name in the clear protects very little");
		}

		/// <summary>
		/// Section 22.1 credential hygiene. Binding these is only safe because nothing in the
		/// codebase reads them today — the broker's decrypt lane is grant-gated, so a future
		/// grantless consumer would need an attended path or a separate secret store. This pins the
		/// assumption so that a reader added later trips a test rather than a production mailbox.
		/// </summary>
		[Test]
		public void Stored_mailbox_credentials_are_bound()
		{
			var columns = AdpTableBindings.V1.Single(b => b.TableName == "DistributionLists")
				.Columns.Select(c => c.FieldId).ToList();

			columns.Should().BeEquivalentTo(new[] { "distributionlists.username", "distributionlists.password" });

			// The address and display name stay readable: they are how the list is administered and
			// how inbound mail is routed to it.
			_catalog.GetAll().Select(e => e.FieldId).Should().NotContain("distributionlists.emailaddress");
			_catalog.GetAll().Select(e => e.FieldId).Should().NotContain("distributionlists.name");
		}

		[Test]
		public void A_catalog_upgrade_from_eight_touches_only_the_new_tables()
		{
			var scoped = AdpTableBindings.ForVersionRange(_catalog, 8, 9);

			scoped.Select(b => b.TableName).Should().BeEquivalentTo(new[]
			{
				"UnitLogs", "UserStates", "CalendarItems", "Documents", "DistributionLists"
			}, "an upgrade sweep must not re-read a table it has nothing to do in");
		}

		/// <summary>
		/// Every table the plan's field-and-storage audit prepared for protection is now bound. If a
		/// new candidate is added to the plan, this is the test that should fail first.
		/// </summary>
		[Test]
		public void No_prepared_candidate_table_is_left_unbound()
		{
			var bound = AdpTableBindings.V1.Select(b => b.TableName).ToList();

			bound.Should().Contain(new[]
			{
				"Calls", "CallNotes", "CallAttachments", "CallLogs", "CallReferences",
				"Contacts", "ContactNotes", "Logs", "UdfFieldValues", "UnitStates",
				"DepartmentMemberSensitiveData", "DepartmentMemberEmergencyContacts", "PersonnelCertifications",
				"Messages", "MessageRecipients",
				"ModerationRequests", "ModerationReports", "ModerationActions",
				"ChatMessageFlags", "ChatModerationActions", "ChatExports",
				"UnitLogs", "UserStates", "CalendarItems", "Documents", "DistributionLists"
			});
		}
	}
}
