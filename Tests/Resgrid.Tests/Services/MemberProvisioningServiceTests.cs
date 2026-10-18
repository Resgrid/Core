using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using Resgrid.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Member provisioning is retried with the same pre-generated account id (Department Import plan section 11.2): a retry
	/// finishes what a crashed attempt left undone instead of skipping it because the account already exists, and still
	/// reports the account as the import's own so rollback can delete it (section 11.8).
	/// </summary>
	[TestFixture]
	public class MemberProvisioningServiceTests
	{
		private const int DepartmentId = 7;
		private const string UserId = "4B1D0C2E-0000-4000-8000-000000000001";

		private Mock<IMemberAccountService> _accounts;
		private Mock<IUsersService> _users;
		private IdentityUser _account;

		[SetUp]
		public void SetUp()
		{
			_accounts = new Mock<IMemberAccountService>();
			_users = new Mock<IUsersService>();
			_account = new IdentityUser { Id = UserId, UserName = "pat.lee", Email = "pat@example.test", EmailConfirmed = true };
			_accounts.Setup(a => a.FindByIdAsync(UserId)).ReturnsAsync(_account);
			_accounts.Setup(a => a.SetEmailConfirmedAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>())).ReturnsAsync(MemberAccountResult.Ok());
		}

		private MemberProvisioningService Service() => new(_accounts.Object, _users.Object, Mock.Of<IUserProfileService>(), Mock.Of<IDepartmentsService>(),
			Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
			Mock.Of<IUserDefinedFieldsService>());

		private static MemberProvisioningRequest Request() => new()
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			Email = "pat@example.test",
			EmailConfirmed = false,
			// The importer's account claim: the id belongs to this provisioning alone.
			UserIdReserved = true
		};

		[Test]
		public async Task A_retry_initializes_the_extension_row_a_crashed_attempt_never_wrote()
		{
			_users.Setup(u => u.HasUserExtInfoAsync(UserId)).ReturnsAsync(false);

			var result = await Service().ProvisionAsync(Request(), ImportEffectPolicy.Import, CancellationToken.None);

			result.Succeeded.Should().BeTrue();
			result.CreatedAccount.Should().BeTrue("the earlier attempt made the account under the reserved id: rollback may delete it");
			_users.Verify(u => u.InitUserExtInfo(UserId), Times.Once);
			// InitUserExtInfo confirms the email; an imported, not-yet-invited account goes back to unconfirmed.
			_accounts.Verify(a => a.SetEmailConfirmedAsync(_account, false), Times.Once);
		}

		[Test]
		public async Task A_retry_leaves_an_existing_extension_row_alone()
		{
			_users.Setup(u => u.HasUserExtInfoAsync(UserId)).ReturnsAsync(true);

			var result = await Service().ProvisionAsync(Request(), ImportEffectPolicy.Import, CancellationToken.None);

			result.Succeeded.Should().BeTrue();
			result.CreatedAccount.Should().BeTrue();
			_users.Verify(u => u.AddUserToUserRole(UserId), Times.Once);
			_users.Verify(u => u.InitUserExtInfo(It.IsAny<string>()), Times.Never);
			_accounts.Verify(a => a.SetEmailConfirmedAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task An_account_under_an_id_that_was_not_reserved_is_never_reported_as_created()
		{
			_users.Setup(u => u.HasUserExtInfoAsync(UserId)).ReturnsAsync(true);
			var request = Request();
			request.UserIdReserved = false;

			var result = await Service().ProvisionAsync(request, ImportEffectPolicy.Interactive, CancellationToken.None);

			result.Succeeded.Should().BeTrue();
			result.CreatedAccount.Should().BeFalse("nothing says this provisioning made the account");
		}

		[Test]
		public async Task A_reserved_account_from_an_earlier_attempt_keeps_the_profile_it_already_has()
		{
			_users.Setup(u => u.HasUserExtInfoAsync(UserId)).ReturnsAsync(true);
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync(UserId, true)).ReturnsAsync(new Resgrid.Model.UserProfile { UserId = UserId, FirstName = "Pat" });
			var request = Request();
			request.Profile = new Resgrid.Model.UserProfile { FirstName = "Patricia" };
			var service = new MemberProvisioningService(_accounts.Object, _users.Object, profiles.Object, Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IUserDefinedFieldsService>());

			(await service.ProvisionAsync(request, ImportEffectPolicy.Import, CancellationToken.None)).CreatedAccount.Should().BeTrue();

			profiles.Verify(p => p.SaveProfileAsync(It.IsAny<int>(), It.IsAny<Resgrid.Model.UserProfile>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task Without_account_extras_the_extension_row_is_never_checked()
		{
			var request = Request();
			request.InitializeAccountExtras = false;

			(await Service().ProvisionAsync(request, ImportEffectPolicy.Import, CancellationToken.None)).Succeeded.Should().BeTrue();

			_users.Verify(u => u.HasUserExtInfoAsync(It.IsAny<string>()), Times.Never);
			_users.Verify(u => u.InitUserExtInfo(It.IsAny<string>()), Times.Never);
		}
	}
}
