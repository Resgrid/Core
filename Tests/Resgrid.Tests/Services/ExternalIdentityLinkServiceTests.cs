using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ExternalIdentityLinkServiceTests
	{
		[Test]
		public async Task durable_link_with_missing_configuration_fails_closed_for_local_login()
		{
			var links = new Mock<IUserExternalIdentityLinksRepository>();
			var configs = new Mock<IDepartmentSsoConfigRepository>();
			var members = new Mock<IDepartmentMembersRepository>();
			links.Setup(x => x.GetActiveByUserAsync("user-1")).ReturnsAsync(new List<UserExternalIdentityLink>
			{
				new UserExternalIdentityLink
				{
					UserId = "user-1",
					DepartmentId = 9,
					DepartmentSsoConfigId = "missing-config",
					IsActive = true
				}
			});
			configs.Setup(x => x.GetAllByDepartmentIdAsync(9))
				.ReturnsAsync(new List<DepartmentSsoConfig>());

			var service = new ExternalIdentityLinkService(links.Object, configs.Object, members.Object);

			Assert.That(await service.IsLocalLoginAllowedAsync("user-1", 9), Is.False);
			Assert.That(await service.IsLocalLoginAllowedAsync("user-1"), Is.False);
		}

		[Test]
		public async Task enabled_link_must_explicitly_allow_local_login()
		{
			var links = new Mock<IUserExternalIdentityLinksRepository>();
			var configs = new Mock<IDepartmentSsoConfigRepository>();
			var members = new Mock<IDepartmentMembersRepository>();
			links.Setup(x => x.GetActiveByUserAsync("user-1")).ReturnsAsync(new List<UserExternalIdentityLink>
			{
				new UserExternalIdentityLink
				{
					UserId = "user-1",
					DepartmentId = 9,
					DepartmentSsoConfigId = "config-1",
					IsActive = true
				}
			});
			configs.Setup(x => x.GetAllByDepartmentIdAsync(9)).ReturnsAsync(new[]
			{
				new DepartmentSsoConfig
				{
					DepartmentSsoConfigId = "config-1",
					DepartmentId = 9,
					IsEnabled = true,
					AllowLocalLogin = false
				}
			});

			var service = new ExternalIdentityLinkService(links.Object, configs.Object, members.Object);

			Assert.That(await service.IsLocalLoginAllowedAsync("user-1", 9), Is.False);
		}

		[Test]
		public async Task legacy_link_in_any_department_keeps_credentials_and_email_sso_managed()
		{
			var links = new Mock<IUserExternalIdentityLinksRepository>();
			var configs = new Mock<IDepartmentSsoConfigRepository>();
			var members = new Mock<IDepartmentMembersRepository>();
			links.Setup(x => x.GetActiveByUserAsync("user-1"))
				.ReturnsAsync(new List<UserExternalIdentityLink>());
			members.Setup(x => x.GetAllDepartmentMemberByUserIdAsync("user-1"))
				.ReturnsAsync(new[]
				{
					new DepartmentMember
					{
						UserId = "user-1",
						DepartmentId = 22,
						ExternalSsoId = "legacy-subject"
					}
				});

			var service = new ExternalIdentityLinkService(links.Object, configs.Object, members.Object);
			var state = await service.GetSsoManagementStateAsync("user-1");

			Assert.That(state.IsSsoManaged, Is.True);
			Assert.That(state.IsEmailExternallyManaged, Is.True);
		}
	}
}
