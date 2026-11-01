using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class ContactsPreplansWidgetProviderTests
	{
		private Mock<IContactsService> _contacts;
		private Mock<IProtectedReadService> _protectedReads;
		private List<IReadOnlyList<Contact>> _resolved;

		private ContactsPreplansWidgetProvider Provider(bool redact)
		{
			_contacts = new Mock<IContactsService>(MockBehavior.Strict);
			_contacts.Setup(x => x.GetPreplansDueForReviewAsync(DepartmentId)).ReturnsAsync(new List<ContactPreplan>
			{
				new ContactPreplan { ContactId = "company", DepartmentId = DepartmentId, NextReviewDue = Now.AddDays(-10) },
				new ContactPreplan { ContactId = "person", DepartmentId = DepartmentId, NextReviewDue = Now.AddDays(-2) },
				new ContactPreplan { ContactId = "deleted", DepartmentId = DepartmentId, NextReviewDue = Now.AddDays(-30) },
				new ContactPreplan { ContactId = "foreign", DepartmentId = OtherDepartmentId, NextReviewDue = Now.AddDays(-40) }
			});
			_contacts.Setup(x => x.GetContactsByIdsAsync(DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new Dictionary<string, Contact>
			{
				["company"] = new Contact { ContactId = "company", DepartmentId = DepartmentId, ContactType = 1, CompanyName = "Acme Storage", FirstName = "rgdp:v1:x" },
				["person"] = new Contact { ContactId = "person", DepartmentId = DepartmentId, ContactType = 0, FirstName = "Jane", LastName = "Doe" },
				["deleted"] = new Contact { ContactId = "deleted", DepartmentId = DepartmentId, CompanyName = "Gone Inc", IsDeleted = true }
			});

			_resolved = new List<IReadOnlyList<Contact>>();
			_protectedReads = new Mock<IProtectedReadService>(MockBehavior.Strict);
			_protectedReads.Setup(x => x.ResolveContactsForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<Contact>>(), It.IsAny<string>(), UserId, It.IsAny<CancellationToken>()))
				.Callback<int, IReadOnlyList<Contact>, string, string, CancellationToken>((_, list, _, _, _) =>
				{
					_resolved.Add(list);
					if (redact)
						foreach (var contact in list)
						{
							contact.FirstName = ProtectedDataEnvelope.RedactionValue;
							contact.LastName = ProtectedDataEnvelope.RedactionValue;
						}
				})
				.ReturnsAsync(new ProtectedReadResult());

			return new ContactsPreplansWidgetProvider(_contacts.Object, _protectedReads.Object);
		}

		[Test]
		public async Task Company_names_are_plain_and_only_person_contacts_are_resolved()
		{
			var provider = Provider(redact: false);

			var data = (ContactsPreplansWidgetData)await provider.GetDataAsync(For("contacts.preplans"), CancellationToken.None);

			data.DueCount.Should().Be(2, "a deleted contact and another department's pre-plan are not listed");
			data.Items.Select(i => i.ContactId).Should().Equal("company", "person");
			data.Items[0].Name.Should().BeEquivalentTo(new { Value = "Acme Storage", IsProtected = false });
			data.Items[0].DaysOverdue.Should().Be(10);
			data.Items[1].Name.Value.Should().Be("Jane Doe");
			_resolved.Should().ContainSingle().Which.Select(c => c.ContactId).Should().Equal("person");
		}

		[Test]
		public async Task A_redacted_person_name_is_a_shield_revealable_only_with_the_contact_permission()
		{
			var withPermission = (ContactsPreplansWidgetData)await Provider(redact: true).GetDataAsync(
				For("contacts.preplans", adpEnforced: true, adpPermissions: new[] { PermissionTypes.ViewProtectedContactData }, grant: "g"), CancellationToken.None);
			var withoutPermission = (ContactsPreplansWidgetData)await Provider(redact: true).GetDataAsync(
				For("contacts.preplans", adpEnforced: true, adpPermissions: new[] { PermissionTypes.ViewProtectedOperationalData }), CancellationToken.None);

			withPermission.Items.Single(i => i.ContactId == "person").Name.Should().BeEquivalentTo(new { IsProtected = true, CanReveal = true, Value = (string)null });
			withoutPermission.Items.Single(i => i.ContactId == "person").Name.Should().BeEquivalentTo(new { IsProtected = true, CanReveal = false });
			withPermission.Items.Single(i => i.ContactId == "company").Name.Value.Should().Be("Acme Storage");
		}
	}
}
