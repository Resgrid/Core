using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The v4 role and personnel results carry the personnel role map icon and the role order (M0286): a role's own IconUrl
	/// and SortOrder, and a person's RoleIconUrl, which is the icon of the first role they hold, in role order, with one.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class RoleIconApiTests
	{
		private const string Api = "https://api.example.test";
		private const string ParamedicKey = "0123456789abcdef0123456789abcdef";
		private const string FirefighterKey = "fedcba9876543210fedcba9876543210";

		private string _previousApiBaseUrl;

		[SetUp]
		public void SetUp()
		{
			_previousApiBaseUrl = Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = Api;
		}

		[TearDown]
		public void TearDown() => Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = _previousApiBaseUrl;

		[Test]
		public void ConvertRoleData_carries_the_icon_url_and_the_sort_order()
		{
			var data = RolesController.ConvertRoleData(new PersonnelRole { PersonnelRoleId = 7, Name = "Paramedic", IconKey = ParamedicKey, SortOrder = 2 });

			data.RoleId.Should().Be("7");
			data.IconUrl.Should().Be(Api + "/api/v4/TypeIcons/" + ParamedicKey);
			data.SortOrder.Should().Be(2);
			RolesController.ConvertRoleData(new PersonnelRole { PersonnelRoleId = 8, Name = "Driver" }).IconUrl.Should().BeNull();
		}

		[Test]
		public async Task ConvertPersonnelInfo_gives_the_icon_of_the_first_held_role_in_order_that_has_one()
		{
			var roles = new List<PersonnelRole>
			{
				new PersonnelRole { PersonnelRoleId = 3, Name = "Firefighter", SortOrder = 3, IconKey = FirefighterKey },
				new PersonnelRole { PersonnelRoleId = 1, Name = "Officer", SortOrder = 1 },
				new PersonnelRole { PersonnelRoleId = 2, Name = "Paramedic", SortOrder = 2, IconKey = ParamedicKey }
			};

			var data = await Convert(roles);

			data.RoleIconUrl.Should().Be(Api + "/api/v4/TypeIcons/" + ParamedicKey, "Officer is first but has no icon, so Paramedic's is used");
			data.Roles.Should().BeEquivalentTo("Firefighter", "Officer", "Paramedic");
		}

		[Test]
		public async Task ConvertPersonnelInfo_has_no_role_icon_when_no_role_has_one()
		{
			(await Convert(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 1, Name = "Officer" } })).RoleIconUrl.Should().BeNull();
			(await Convert(null)).RoleIconUrl.Should().BeNull();
		}

		private static Task<Resgrid.Web.Services.Models.v4.Personnel.PersonnelInfoResultData> Convert(List<PersonnelRole> roles) =>
			PersonnelController.ConvertPersonnelInfo(new IdentityUser { Id = "u1", Email = "u1@example.test" }, new Department { DepartmentId = 1 },
				new UserProfile { UserId = "u1", FirstName = "Pat", LastName = "Medic" }, null, roles, null, null, canViewPII: false, canViewLocation: false);
	}
}
