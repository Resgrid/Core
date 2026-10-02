using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// Writes that change a projected related row (a contact note, role membership) refresh the owning entity's search
	/// projection, so the catch-up sweep re-indexes it rather than waiting for a full rebuild.
	/// </summary>
	[TestFixture]
	public class SearchProjectionHookTests
	{
		private const int Dept = 7;
		private Mock<ISearchProjectionService> _search;
		private List<(int Department, string Type, string Id)> _refreshed;

		[SetUp]
		public void SetUp()
		{
			_refreshed = new List<(int, string, string)>();
			_search = new Mock<ISearchProjectionService>();
			_search.Setup(s => s.RefreshAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((int d, string t, string id, CancellationToken _) => _refreshed.Add((d, t, id))).Returns(Task.CompletedTask);
		}

		private ContactsService Contacts(Mock<IContactNotesRepository> notes, Mock<IContactNoteTypesRepository> types = null)
		{
			var writes = new Mock<IProtectedWriteService>();
			writes.Setup(w => w.PrepareContactNoteWriteAsync(It.IsAny<int>(), It.IsAny<ContactNote>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			return new ContactsService(Mock.Of<IContactsRepository>(), notes.Object, Mock.Of<IContactCategoryRepository>(), (types ?? new Mock<IContactNoteTypesRepository>()).Object,
				Mock.Of<IContactAssociationsRepository>(), Mock.Of<IContactPreplanRepository>(), Mock.Of<IContactPreplanHazardRepository>(), Mock.Of<IContactAttachmentRepository>(),
				Mock.Of<ICallsRepository>(), Mock.Of<ICallContactsRepository>(), Mock.Of<IEventAggregator>(),
				new Lazy<IProtectedWriteService>(() => writes.Object), new Lazy<IContactPreplanOwnershipGate>(() => Mock.Of<IContactPreplanOwnershipGate>()),
				new Lazy<ISearchProjectionService>(() => _search.Object));
		}

		[Test]
		public async Task Saving_a_contact_note_refreshes_the_contact()
		{
			var notes = new Mock<IContactNotesRepository>();
			notes.Setup(n => n.SaveOrUpdateAsync(It.IsAny<ContactNote>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((ContactNote n, CancellationToken _, bool __) => n);

			await Contacts(notes).SaveContactNoteAsync(new ContactNote { ContactNoteId = "n1", ContactId = "c1", DepartmentId = Dept, Note = "Alarm panel moved" });

			_refreshed.Should().Equal((Dept, SearchEntityTypes.Contact, "c1"));
		}

		[Test]
		public async Task Reading_a_contacts_notes_leaves_out_the_deleted_ones()
		{
			var notes = new Mock<IContactNotesRepository>();
			notes.Setup(n => n.GetContactNotesByContactIdAsync("c1")).ReturnsAsync(new List<ContactNote>
			{
				new ContactNote { ContactNoteId = "live", ContactId = "c1" }, new ContactNote { ContactNoteId = "gone", ContactId = "c1", IsDeleted = true }
			});
			var types = new Mock<IContactNoteTypesRepository>();
			types.Setup(t => t.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<ContactNoteType>());
			var service = Contacts(notes, types);

			(await service.GetContactNotesByContactIdAsync("c1", Dept)).Select(n => n.ContactNoteId).Should().Equal("live");
			(await service.GetContactNotesByContactIdAsync("c1", Dept, getDeleted: true)).Select(n => n.ContactNoteId).Should().Equal("live", "gone");
		}

		[Test]
		public async Task Setting_a_members_roles_refreshes_that_member_once()
		{
			var roles = new Mock<IPersonnelRolesRepository>();
			roles.Setup(r => r.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 3, DepartmentId = Dept, Name = "Engineer" } });
			roles.Setup(r => r.GetPersonnelRolesByDepartmentIdAsync(Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 3, DepartmentId = Dept, Name = "Engineer" } });
			var roleUsers = new Mock<IPersonnelRoleUsersRepository>();
			roleUsers.Setup(r => r.GetAllRoleUsersForUserAsync(Dept, "u1")).ReturnsAsync(new List<PersonnelRoleUser>());
			var service = new PersonnelRolesService(roles.Object, roleUsers.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentMembersRepository>(),
				Mock.Of<IEventAggregator>(), Mock.Of<IUnitOfWork>(), null, new Lazy<ISearchProjectionService>(() => _search.Object));

			await service.SetRolesForUserAsync(Dept, "u1", new[] { "3" });

			_refreshed.Should().Equal((Dept, SearchEntityTypes.Personnel, "u1"));
		}

		[Test]
		public async Task Deleting_a_role_refreshes_its_former_members()
		{
			var roles = new Mock<IPersonnelRolesRepository>();
			roles.Setup(r => r.GetRoleByRoleIdAsync(3)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 3, DepartmentId = Dept, Name = "Engineer" });
			roles.Setup(r => r.DeleteAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var roleUsers = new Mock<IPersonnelRoleUsersRepository>();
			roleUsers.Setup(r => r.GetAllMembersOfRoleAsync(3)).ReturnsAsync(new List<PersonnelRoleUser>
			{
				new PersonnelRoleUser { PersonnelRoleId = 3, DepartmentId = Dept, UserId = "u1" }, new PersonnelRoleUser { PersonnelRoleId = 3, DepartmentId = Dept, UserId = "u2" }
			});
			var unitOfWork = new Mock<IUnitOfWork>();
			unitOfWork.Setup(u => u.CreateOrGetConnection()).Returns((DbConnection)null);
			var service = new PersonnelRolesService(roles.Object, roleUsers.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentMembersRepository>(),
				Mock.Of<IEventAggregator>(), unitOfWork.Object, null, new Lazy<ISearchProjectionService>(() => _search.Object));

			(await service.DeleteRoleByIdAsync(3)).Should().BeTrue();

			_refreshed.Select(r => r.Id).Should().BeEquivalentTo(new[] { "u1", "u2" });
			_refreshed.Should().OnlyContain(r => r.Department == Dept && r.Type == SearchEntityTypes.Personnel);
		}
	}
}
