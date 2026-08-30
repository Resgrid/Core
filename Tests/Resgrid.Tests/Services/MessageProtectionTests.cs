using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Catalog v7 brought member messaging into the protected-field catalog. These pin the shape of
	/// that binding — which columns are in, which are deliberately out, and that the two tables are
	/// scoped by their own DepartmentId rather than a join (M0137).
	/// </summary>
	[TestFixture]
	public class MessageProtectionTests
	{
		private ProtectedFieldCatalog _catalog;

		[SetUp]
		public void SetUp() => _catalog = new ProtectedFieldCatalog();

		[Test]
		public void Messaging_is_what_moved_the_catalog_to_seven()
		{
			_catalog.Version.Should().BeGreaterThanOrEqualTo(7);

			_catalog.GetAddedBetween(6, 7).Select(e => e.FieldId)
				.Should().BeEquivalentTo(new[]
				{
					"messages.subject",
					"messages.body",
					"messagerecipients.response",
					"messagerecipients.note",
					"messagerecipients.latitude",
					"messagerecipients.longitude"
				});
		}

		/// <summary>
		/// The note is cataloged and the prompt metadata that used to share its column is NOT,
		/// and it must stay that way. Every reader of that token — the chatbot inbound resolver, the
		/// RSVP prompt service, both message controllers — runs with NO grant, against a broker
		/// whose workload lane is encrypt-only, so enveloping it would silently break calendar RSVP
		/// and poll replies for exactly the departments that turned protection on.
		/// </summary>
		[Test]
		public void The_note_is_cataloged_and_the_prompt_metadata_beside_it_is_not()
		{
			var fieldIds = _catalog.GetAll().Select(e => e.FieldId).ToList();

			fieldIds.Should().Contain("messagerecipients.note");
			fieldIds.Should().NotContain("messagerecipients.promptmetadata",
				"the token is a row pointer read without a grant; encrypting it breaks RSVP and polls");

			var columns = AdpTableBindings.V1.Single(b => b.TableName == "MessageRecipients")
				.Columns.Select(c => c.FieldId).ToList();

			columns.Should().Contain("messagerecipients.note");
			columns.Should().NotContain("messagerecipients.promptmetadata");
		}

		[Test]
		public void Both_message_tables_are_scoped_by_their_own_department_column()
		{
			foreach (var table in new[] { "Messages", "MessageRecipients" })
			{
				var binding = AdpTableBindings.V1.Single(b => b.TableName == table);

				binding.DepartmentColumn.Should().Be("DepartmentId",
					$"{table} got its own department column in M0137 precisely so the AAD has a value to bind");
				binding.ParentTable.Should().BeNull($"{table} no longer derives ownership through a parent");
			}
		}

		[Test]
		public void The_recipient_marker_column_is_bound_and_the_message_one_is_not()
		{
			// M0129 added IsProtected to MessageRecipients only; Messages detects the envelope
			// prefix on read instead, exactly as Calls and Contacts do.
			AdpTableBindings.V1.Single(b => b.TableName == "MessageRecipients")
				.ProtectedMarkerColumn.Should().Be("IsProtected");
			AdpTableBindings.V1.Single(b => b.TableName == "Messages")
				.ProtectedMarkerColumn.Should().BeNull();
		}

		[Test]
		public void Recipient_coordinates_are_bound_as_companion_columns()
		{
			var columns = AdpTableBindings.V1.Single(b => b.TableName == "MessageRecipients").Columns
				.Where(c => c.StorageKind == ProtectedFieldStorageKind.CompanionColumn)
				.ToList();

			columns.Select(c => c.CompanionColumn)
				.Should().BeEquivalentTo(new[] { "ProtectedLatitudeEnvelope", "ProtectedLongitudeEnvelope" },
					"a decimal column cannot hold an rgdp envelope, so the value moves to its companion");
		}

		[Test]
		public void A_catalog_upgrade_from_six_touches_only_the_message_tables()
		{
			var scoped = AdpTableBindings.ForVersionRange(_catalog, 6, 7);

			scoped.Select(b => b.TableName)
				.Should().BeEquivalentTo(new[] { "Messages", "MessageRecipients" },
					"an upgrade sweep must not re-read a table it has nothing to do in");
		}
	}
}
