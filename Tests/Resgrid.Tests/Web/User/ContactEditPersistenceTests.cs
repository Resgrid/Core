using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The contact edit form binds 22 of the entity's columns; the entity has more — the image, the
	/// geofence, the five government-ID fields, both address links and the audit stamps. The action
	/// used to load the stored row, mutate it, and then persist the POSTED object, so every column
	/// the form does not carry was blanked on every edit (for a protected department, that included
	/// the only copy of enveloped ID numbers) and the address links it had just resolved were
	/// thrown away.
	///
	/// Structural, because the failure is invisible: the save succeeds and the redirect looks
	/// normal. A behavioural test would need the whole controller graph to see it, and would not
	/// notice a NEW field being copied onto the wrong object.
	/// </summary>
	[TestFixture]
	public class ContactEditPersistenceTests
	{
		private static string EditPostBody()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the tests must be able to find the repository root");

			var path = Path.Combine(directory!.FullName, "Web", "Resgrid.Web", "Areas", "User",
				"Controllers", "ContactsController.cs");

			File.Exists(path).Should().BeTrue($"expected the controller at {path}");
			var source = File.ReadAllText(path);

			// The POST overload takes the view model; slice from its signature to the next method.
			var start = source.IndexOf("public async Task<IActionResult> Edit(EditContactView model", System.StringComparison.Ordinal);
			start.Should().BeGreaterThan(0, "the Edit POST action should still exist");

			var next = Regex.Match(source.Substring(start + 1),
				@"(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?Task<[^>]+>\s+\w+\s*\(");

			return next.Success ? source.Substring(start, next.Index + 1) : source.Substring(start);
		}

		[Test]
		public void The_edit_action_persists_the_stored_row_not_the_posted_one()
		{
			var body = EditPostBody();

			body.Should().Contain("SaveContactAsync(contact,",
				"the posted object carries only the columns this form binds; saving it blanks the rest");
			body.Should().NotContain("SaveContactAsync(model.Contact,");
		}

		[Test]
		public void The_resolved_address_links_land_on_the_object_that_gets_saved()
		{
			var body = EditPostBody();

			// Both links are resolved from the freshly saved Address rows; if either is assigned to
			// the posted object while the stored row is persisted, editing an address silently
			// fails to link it.
			body.Should().Contain("contact.PhysicalAddressId = physicalAddress.AddressId;");
			body.Should().Contain("contact.MailingAddressId = mailingAddress.AddressId;");
			body.Should().NotContain("model.Contact.MailingAddressId = physicalAddress.AddressId;",
				"the same-as-physical branch has to target the saved row too");
		}

		[Test]
		public void An_edit_stamps_the_edit_fields_and_leaves_the_creator_alone()
		{
			var body = EditPostBody();

			body.Should().Contain("contact.EditedByUserId = UserId;");
			body.Should().NotContain("AddedByUserId = UserId;",
				"AddedOn/AddedByUserId belong to whoever created the contact; overwriting them on an edit loses that");
		}

		/// <summary>
		/// Each pair of latitude/longitude inputs is labelled Location, Entrance or Exit in the
		/// view. The POST used to write the LOCATION pair into EntranceGpsCoordinates (which the
		/// entrance pair then overwrote) and the GET read Entrance back into the LOCATION inputs, so
		/// LocationGpsCoordinates was never populated and the Entrance boxes were always empty.
		/// </summary>
		[Test]
		public void Each_coordinate_pair_is_wired_to_the_column_its_label_names()
		{
			var body = EditPostBody();

			body.Should().Contain("contact.LocationGpsCoordinates = ResolveCoordinates(model.LocationGpsLatitude");
			body.Should().Contain("contact.EntranceGpsCoordinates = ResolveCoordinates(model.EntranceGpsLatitude");
			body.Should().Contain("contact.ExitGpsCoordinates = ResolveCoordinates(model.ExitGpsLatitude");
		}
	}
}
