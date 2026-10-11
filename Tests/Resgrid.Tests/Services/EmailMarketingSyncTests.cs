using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Marketing;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Department administrators and managing members are pushed to the email marketing platform (Loops), and a
	/// user's newsletter choice decides whether they are subscribed there.
	/// </summary>
	[TestFixture]
	public class EmailMarketingSyncTests
	{
		private const int Dept = 52;

		#region EmailMarketingService

		private Mock<IEmailMarketingProvider> _provider;
		private Mock<IUsersService> _users;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDepartmentMembersRepository> _memberRows;
		private Mock<IDepartmentsRepository> _departmentRows;
		private List<EmailMarketingContact> _upserts;
		private UserProfile _profile;

		[SetUp]
		public void SetUp()
		{
			_upserts = new List<EmailMarketingContact>();
			_provider = new Mock<IEmailMarketingProvider>();
			_provider.SetupGet(p => p.IsConfigured).Returns(true);
			_provider.Setup(p => p.UpsertContactAsync(It.IsAny<EmailMarketingContact>(), It.IsAny<CancellationToken>()))
				.Callback<EmailMarketingContact, CancellationToken>((c, _) => _upserts.Add(c)).ReturnsAsync(true);

			_users = new Mock<IUsersService>();
			_users.Setup(u => u.GetUserById(It.IsAny<string>(), It.IsAny<bool>()))
				.Returns((string id, bool _) => new IdentityUser { Id = id, Email = $"{id}@example.com" });

			_profile = new UserProfile { FirstName = "Matt", LastName = "Casey" };
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(() => _profile);

			_memberRows = new Mock<IDepartmentMembersRepository>();
			_departmentRows = new Mock<IDepartmentsRepository>();
		}

		private EmailMarketingService Service() =>
			new EmailMarketingService(_provider.Object, _users.Object, _profiles.Object, _memberRows.Object, _departmentRows.Object);

		private void Memberships(params DepartmentMember[] rows)
		{
			_memberRows.Setup(m => m.GetAllDepartmentMemberByUserIdAsync(It.IsAny<string>())).ReturnsAsync(rows);
			_departmentRows.Setup(d => d.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync((object id) => new Department { DepartmentId = (int)id, Name = $"Station {id}", ManagingUserId = "someone-else" });
		}

		[Test]
		public async Task Managing_member_is_pushed_with_department_details_and_no_subscription_change()
		{
			var department = new Department { DepartmentId = Dept, Name = "Firehouse 51", ManagingUserId = "FOUNDER" };

			(await Service().SyncDepartmentAdministratorAsync(department, "founder")).Should().BeTrue();

			var contact = _upserts.Should().ContainSingle().Subject;
			contact.UserId.Should().Be("founder");
			contact.EmailAddress.Should().Be("founder@example.com");
			contact.FirstName.Should().Be("Matt");
			contact.LastName.Should().Be("Casey");
			contact.UserGroup.Should().Be(EmailMarketingService.ManagingMemberUserGroup);
			contact.IsManagingMember.Should().BeTrue("the managing user id is compared case-insensitively");
			contact.DepartmentId.Should().Be(Dept);
			contact.DepartmentName.Should().Be("Firehouse 51");
			contact.Subscribed.Should().BeNull("becoming an admin is not consent, so an unsubscribe made on the platform must stand");
		}

		[Test]
		public async Task Admin_who_opted_out_on_their_profile_is_pushed_unsubscribed()
		{
			_profile.DoNotRecieveNewsletters = true;

			await Service().SyncDepartmentAdministratorAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51", ManagingUserId = "founder" }, "lieutenant");

			var contact = _upserts.Should().ContainSingle().Subject;
			contact.Subscribed.Should().BeFalse();
			contact.UserGroup.Should().Be(EmailMarketingService.DepartmentAdminUserGroup);
			contact.IsManagingMember.Should().BeFalse();
		}

		[Test]
		public async Task Nothing_is_sent_or_read_when_no_platform_is_configured()
		{
			_provider.SetupGet(p => p.IsConfigured).Returns(false);
			var service = Service();

			service.IsEnabled.Should().BeFalse();
			(await service.SyncDepartmentAdministratorAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51" }, "founder")).Should().BeFalse();
			(await service.SyncNewsletterPreferenceAsync("founder", true)).Should().BeFalse();

			_upserts.Should().BeEmpty();
			_users.Verify(u => u.GetUserById(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
			_memberRows.Verify(m => m.GetAllDepartmentMemberByUserIdAsync(It.IsAny<string>()), Times.Never());
		}

		[Test]
		public async Task A_user_without_an_email_address_is_not_pushed()
		{
			_users.Setup(u => u.GetUserById(It.IsAny<string>(), It.IsAny<bool>())).Returns(new IdentityUser { Email = null });

			(await Service().SyncDepartmentAdministratorAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51" }, "founder")).Should().BeFalse();

			_upserts.Should().BeEmpty();
		}

		[TestCase(false, true)]
		[TestCase(true, false)]
		public async Task An_admins_newsletter_choice_is_applied_explicitly(bool doNotReceiveNewsletters, bool expectedSubscribed)
		{
			Memberships(new DepartmentMember { DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true });

			(await Service().SyncNewsletterPreferenceAsync("lieutenant", doNotReceiveNewsletters)).Should().BeTrue();

			var contact = _upserts.Should().ContainSingle().Subject;
			contact.Subscribed.Should().Be(expectedSubscribed, "the person made this choice, so it may re-subscribe as well as unsubscribe");
			contact.UserGroup.Should().Be(EmailMarketingService.DepartmentAdminUserGroup);
			contact.DepartmentId.Should().Be(Dept);
			_provider.Verify(p => p.ContactExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never(),
				"an administrator is upserted, no lookup needed");
		}

		[Test]
		public async Task A_managing_member_is_preferred_over_another_admin_membership()
		{
			_memberRows.Setup(m => m.GetAllDepartmentMemberByUserIdAsync("chief")).ReturnsAsync(new[]
			{
				new DepartmentMember { DepartmentId = 1, UserId = "chief", IsAdmin = true },
				new DepartmentMember { DepartmentId = 2, UserId = "chief", IsAdmin = false }
			});
			_departmentRows.Setup(d => d.GetByIdAsync(1)).ReturnsAsync(new Department { DepartmentId = 1, Name = "Station 1", ManagingUserId = "someone-else" });
			_departmentRows.Setup(d => d.GetByIdAsync(2)).ReturnsAsync(new Department { DepartmentId = 2, Name = "Station 2", ManagingUserId = "chief" });

			await Service().SyncNewsletterPreferenceAsync("chief", false);

			var contact = _upserts.Should().ContainSingle().Subject;
			contact.DepartmentId.Should().Be(2);
			contact.IsManagingMember.Should().BeTrue();
		}

		[Test]
		public async Task A_regular_members_choice_only_updates_a_contact_the_platform_already_holds()
		{
			Memberships(new DepartmentMember { DepartmentId = Dept, UserId = "firefighter", IsAdmin = false });
			_provider.Setup(p => p.ContactExistsAsync("firefighter", "firefighter@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await Service().SyncNewsletterPreferenceAsync("firefighter", true)).Should().BeFalse();
			_upserts.Should().BeEmpty("a regular member is never added to the audience");

			_provider.Setup(p => p.ContactExistsAsync("firefighter", "firefighter@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await Service().SyncNewsletterPreferenceAsync("firefighter", true)).Should().BeTrue();
			var contact = _upserts.Should().ContainSingle().Subject;
			contact.UserId.Should().Be("firefighter");
			contact.EmailAddress.Should().Be("firefighter@example.com");
			contact.Subscribed.Should().BeFalse();
			contact.UserGroup.Should().BeNull("a former admin's segment is not rewritten from a newsletter toggle");
			contact.DepartmentId.Should().BeNull();
		}

		[TestCase(true, false, false)]
		[TestCase(false, true, false)]
		[TestCase(false, false, true)]
		public async Task Removed_disabled_or_hidden_admin_memberships_do_not_count(bool deleted, bool disabled, bool hidden)
		{
			Memberships(new DepartmentMember { DepartmentId = Dept, UserId = "former", IsAdmin = true, IsDeleted = deleted, IsDisabled = disabled, IsHidden = hidden });

			await Service().SyncNewsletterPreferenceAsync("former", true);

			_provider.Verify(p => p.ContactExistsAsync("former", "former@example.com", It.IsAny<CancellationToken>()), Times.Once(),
				"the user is handled as a regular member");
			_upserts.Should().BeEmpty();
		}

		[Test]
		public async Task A_platform_failure_never_escapes()
		{
			_provider.Setup(p => p.UpsertContactAsync(It.IsAny<EmailMarketingContact>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

			(await Service().SyncDepartmentAdministratorAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51" }, "founder")).Should().BeFalse();
		}

		#endregion EmailMarketingService

		#region DepartmentsService change detection

		private static (DepartmentsService Service, Mock<IEmailMarketingService> Marketing, Mock<IDepartmentMembersRepository> Members, Mock<IDepartmentsRepository> Departments)
			Departments(bool marketingEnabled = true)
		{
			var marketing = new Mock<IEmailMarketingService>();
			marketing.SetupGet(m => m.IsEnabled).Returns(marketingEnabled);
			marketing.Setup(m => m.SyncDepartmentAdministratorAsync(It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var members = new Mock<IDepartmentMembersRepository>();
			members.Setup(m => m.SaveOrUpdateAsync(It.IsAny<DepartmentMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentMember m, CancellationToken _, bool __) => m);

			var departments = new Mock<IDepartmentsRepository>();
			departments.Setup(d => d.SaveOrUpdateAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Department d, CancellationToken _, bool __) => d);

			var service = new DepartmentsService(departments.Object, members.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentCallEmailsRepository>(),
				Mock.Of<IDepartmentCallPruningRepository>(), Mock.Of<ICacheProvider>(), Mock.Of<IUsersService>(), Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IUserProfileService>(), Mock.Of<ILimitsService>(), Mock.Of<IEventAggregator>(), Mock.Of<IIdentityRepository>(), Mock.Of<IDepartmentCallPruningRepository>(),
				emailMarketingService: marketing.Object);

			return (service, marketing, members, departments);
		}

		private static void VerifySynced(Mock<IEmailMarketingService> marketing, string userId, Times times) =>
			marketing.Verify(m => m.SyncDepartmentAdministratorAsync(It.Is<Department>(d => d.DepartmentId == Dept), userId, It.IsAny<CancellationToken>()), times);

		[Test]
		public async Task Promoting_a_member_to_admin_syncs_them()
		{
			var (service, marketing, members, departments) = Departments();
			members.Setup(m => m.GetByIdAsync(9)).ReturnsAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = false });
			departments.Setup(d => d.GetByIdAsync(Dept)).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51" });

			await service.SaveDepartmentMemberAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true });

			VerifySynced(marketing, "lieutenant", Times.Once());
		}

		[Test]
		public async Task Saving_someone_who_already_was_an_admin_does_not_sync()
		{
			var (service, marketing, members, _) = Departments();
			members.Setup(m => m.GetByIdAsync(9)).ReturnsAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true });

			await service.SaveDepartmentMemberAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true });

			VerifySynced(marketing, "lieutenant", Times.Never());
		}

		[Test]
		public async Task Re_enabling_a_disabled_admin_syncs_them()
		{
			var (service, marketing, members, departments) = Departments();
			members.Setup(m => m.GetByIdAsync(9)).ReturnsAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true, IsDisabled = true });
			departments.Setup(d => d.GetByIdAsync(Dept)).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51" });

			await service.SaveDepartmentMemberAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true, IsDisabled = false });

			VerifySynced(marketing, "lieutenant", Times.Once());
		}

		[Test]
		public async Task Promoting_a_disabled_member_does_not_sync_or_read_the_stored_row()
		{
			var (service, marketing, members, _) = Departments();

			await service.SaveDepartmentMemberAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true, IsDisabled = true });

			VerifySynced(marketing, "lieutenant", Times.Never());
			members.Verify(m => m.GetByIdAsync(It.IsAny<object>()), Times.Never());
		}

		[Test]
		public async Task Member_saves_skip_the_lookup_when_email_marketing_is_off()
		{
			var (service, marketing, members, _) = Departments(marketingEnabled: false);

			await service.SaveDepartmentMemberAsync(new DepartmentMember { DepartmentMemberId = 9, DepartmentId = Dept, UserId = "lieutenant", IsAdmin = true });

			members.Verify(m => m.GetByIdAsync(It.IsAny<object>()), Times.Never());
			VerifySynced(marketing, "lieutenant", Times.Never());
		}

		[TestCase(true, 1)]
		[TestCase(false, 0)]
		public async Task Adding_a_user_to_a_department_syncs_them_only_as_an_admin(bool isAdmin, int expectedSyncs)
		{
			var (service, marketing, _, departments) = Departments();
			departments.Setup(d => d.GetByIdAsync(Dept)).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51", ManagingUserId = "founder" });

			await service.AddUserToDepartmentAsync(Dept, "founder", isAdmin);

			VerifySynced(marketing, "founder", Times.Exactly(expectedSyncs));
		}

		[Test]
		public async Task Handing_a_department_to_a_new_managing_member_syncs_them()
		{
			var (service, marketing, _, departments) = Departments();
			departments.Setup(d => d.GetByIdAsync(Dept)).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51", ManagingUserId = "old-chief" });

			await service.SaveDepartmentAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51", ManagingUserId = "new-chief" });

			marketing.Verify(m => m.SyncDepartmentAdministratorAsync(It.Is<Department>(d => d.ManagingUserId == "new-chief"), "new-chief", It.IsAny<CancellationToken>()), Times.Once());
		}

		[Test]
		public async Task Saving_a_department_without_a_new_managing_member_does_not_sync()
		{
			var (service, marketing, _, departments) = Departments();
			departments.Setup(d => d.GetByIdAsync(Dept)).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51", ManagingUserId = "CHIEF" });

			await service.SaveDepartmentAsync(new Department { DepartmentId = Dept, Name = "Firehouse 51", ManagingUserId = "chief" });

			marketing.Verify(m => m.SyncDepartmentAdministratorAsync(It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		[Test]
		public async Task Creating_a_department_leaves_the_sync_to_adding_its_founder()
		{
			var (service, marketing, _, departments) = Departments();

			await service.SaveDepartmentAsync(new Department { Name = "Firehouse 51", ManagingUserId = "founder" });

			departments.Verify(d => d.GetByIdAsync(It.IsAny<object>()), Times.Never());
			marketing.Verify(m => m.SyncDepartmentAdministratorAsync(It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		#endregion DepartmentsService change detection

		#region Loops request body

		[Test]
		public void Loops_body_leaves_subscribed_out_unless_it_is_meant()
		{
			var body = LoopsEmailMarketingProvider.BuildContactBody(new EmailMarketingContact { UserId = "u1", EmailAddress = " u1@example.com " }, true);

			body.Should().NotContainKey("subscribed", "Loops re-subscribes a contact on subscribed = true");
			body["email"].Should().Be("u1@example.com");
			body["userId"].Should().Be("u1");
			body.Should().NotContainKeys("firstName", "lastName", "userGroup", "departmentId", "departmentName", "isManagingMember", "mailingLists");

			LoopsEmailMarketingProvider.BuildContactBody(new EmailMarketingContact { UserId = "u1", Subscribed = false }, true)["subscribed"].Should().Be(false);
		}

		[Test]
		public void Loops_body_drops_custom_properties_and_mailing_lists_for_the_fallback_call()
		{
			var contact = new EmailMarketingContact
			{
				UserId = "u1",
				EmailAddress = "u1@example.com",
				UserGroup = EmailMarketingService.ManagingMemberUserGroup,
				DepartmentId = Dept,
				DepartmentName = "Firehouse 51",
				IsManagingMember = true,
				MailingListIds = new[] { "list-1", " " }
			};

			var full = LoopsEmailMarketingProvider.BuildContactBody(contact, true);
			full["departmentId"].Should().Be(Dept);
			full["departmentName"].Should().Be("Firehouse 51");
			full["isManagingMember"].Should().Be(true);
			full["mailingLists"].Should().BeEquivalentTo(new Dictionary<string, bool> { ["list-1"] = true });

			var fallback = LoopsEmailMarketingProvider.BuildContactBody(contact, false);
			fallback.Should().NotContainKeys("departmentId", "departmentName", "isManagingMember", "mailingLists");
			fallback["userGroup"].Should().Be(EmailMarketingService.ManagingMemberUserGroup, "userGroup is a default Loops property");
		}

		#endregion Loops request body
	}
}
