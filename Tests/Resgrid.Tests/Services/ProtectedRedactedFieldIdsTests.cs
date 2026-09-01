using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Per-row redaction reporting for LIST endpoints.
	///
	/// A batch resolve returns one result whose RedactedFields is the union across every row it
	/// touched. That is the right answer for a single record and the wrong one for a list: a field
	/// redacted on one contact would be reported as redacted on all of them, including rows where
	/// the value is simply empty.
	///
	/// The alternative — resolving each row separately — is N broker round trips instead of one, on
	/// the endpoint most likely to return hundreds of rows. So the batch runs once and each row's
	/// own values are read back afterwards, which is what this pins.
	/// </summary>
	[TestFixture]
	public class ProtectedRedactedFieldIdsTests
	{
		private static Contact Contact(string firstName = null, string lastName = null, string email = null) =>
			new Contact { FirstName = firstName, LastName = lastName, Email = email };

		[Test]
		public void Reports_only_the_fields_this_row_actually_had_redacted()
		{
			var contact = Contact(
				firstName: ProtectedDataEnvelope.RedactionValue,
				lastName: "Doe",
				email: ProtectedDataEnvelope.RedactionValue);

			var redacted = ProtectedReadService.GetRedactedFieldIds(contact, ProtectedReadService.ContactFieldAccessors);

			redacted.Should().BeEquivalentTo(new[] { "contacts.firstname", "contacts.email" });
		}

		[Test]
		public void A_row_with_nothing_redacted_reports_nothing()
		{
			var redacted = ProtectedReadService.GetRedactedFieldIds(
				Contact(firstName: "Jamie", lastName: "Doe", email: "jamie@example.com"),
				ProtectedReadService.ContactFieldAccessors);

			redacted.Should().BeEmpty();
		}

		[Test]
		public void An_empty_value_is_not_a_redacted_one()
		{
			// The distinction the union got wrong: a contact with no email has nothing withheld, and
			// saying otherwise would put a lock icon on a field that is simply blank.
			var redacted = ProtectedReadService.GetRedactedFieldIds(
				Contact(firstName: "Jamie", lastName: "Doe", email: string.Empty),
				ProtectedReadService.ContactFieldAccessors);

			redacted.Should().BeEmpty();
		}

		[Test]
		public void Two_rows_in_one_batch_report_independently()
		{
			// The actual list case. One contact's withheld email must not mark the other's.
			var withheld = Contact(firstName: "Jamie", email: ProtectedDataEnvelope.RedactionValue);
			var plain = Contact(firstName: "Alex", email: "alex@example.com");

			ProtectedReadService.GetRedactedFieldIds(withheld, ProtectedReadService.ContactFieldAccessors)
				.Should().Contain("contacts.email");
			ProtectedReadService.GetRedactedFieldIds(plain, ProtectedReadService.ContactFieldAccessors)
				.Should().NotContain("contacts.email");
		}

		[Test]
		public void A_value_that_merely_resembles_the_sentinel_is_not_redacted()
		{
			// Ordinal equality, not a contains or a case-insensitive match: a member is allowed to
			// write "redacted" in a field and have it shown back to them.
			var redacted = ProtectedReadService.GetRedactedFieldIds(
				Contact(firstName: "redacted", lastName: "REDACTED ", email: "REDACTEDX"),
				ProtectedReadService.ContactFieldAccessors);

			redacted.Should().BeEmpty();
		}

		[Test]
		public void A_null_entity_reports_nothing_rather_than_throwing()
		{
			ProtectedReadService.GetRedactedFieldIds((Contact)null, ProtectedReadService.ContactFieldAccessors)
				.Should().BeEmpty();
		}

		[Test]
		public void Every_reported_id_is_a_real_catalog_field()
		{
			// The ids go to clients, which match them against their own catalog constants. One that
			// does not exist server-side would never match and the field would render raw.
			var contact = Contact(firstName: ProtectedDataEnvelope.RedactionValue);

			var redacted = ProtectedReadService.GetRedactedFieldIds(contact, ProtectedReadService.ContactFieldAccessors);

			redacted.Should().OnlyContain(id => ProtectedReadService.ContactFieldAccessors.Keys.Contains(id));
		}
	}
}
