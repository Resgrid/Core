using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The incident command resource picker (v4 MutualAid/GetAssignableResources) adds a linked department's units and
	/// personnel only for what that department shares toward the viewer, over an enabled and accepted link.
	/// </summary>
	[TestFixture]
	public class MutualAidServiceTests
	{
		private const int Creator = 100;
		private const int Linked = 200;

		private static (MutualAidService Service, Mock<IUnitsService> Units, Mock<IDepartmentsService> Departments) Build(int viewer, DepartmentLink link)
		{
			var links = new Mock<IDepartmentLinksService>();
			links.Setup(x => x.GetAllLinksForDepartmentAsync(viewer)).ReturnsAsync(new List<DepartmentLink> { link });

			var units = new Mock<IUnitsService>();
			var departments = new Mock<IDepartmentsService>();
			foreach (var departmentId in new[] { Creator, Linked })
			{
				var id = departmentId;
				units.Setup(x => x.GetUnitsForDepartmentAsync(id)).ReturnsAsync(new List<Unit> { new Unit { UnitId = id, DepartmentId = id, Name = $"Engine {id}" } });
				departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(id))
					.ReturnsAsync(new List<PersonName> { new PersonName { UserId = $"USER-{id}", FirstName = "Member", LastName = id.ToString() } });
			}

			return (new MutualAidService(links.Object, units.Object, departments.Object), units, departments);
		}

		private static DepartmentLink Link(bool enabled = true, bool accepted = true) => new DepartmentLink
		{
			DepartmentLinkId = 9,
			DepartmentId = Creator,
			LinkedDepartmentId = Linked,
			LinkEnabled = enabled,
			LinkAccepted = accepted ? System.DateTime.UtcNow : (System.DateTime?)null
		};

		private static IEnumerable<AssignableResource> MutualAid(List<AssignableResource> resources) => resources.Where(r => r.IsMutualAid);

		[Test]
		public async Task The_linked_department_gets_only_what_the_creator_shares()
		{
			var link = Link();
			link.DepartmentShareUnits = true;
			link.LinkedDepartmentSharePersonnel = true; // the other direction: does not count for the linked viewer
			var (service, _, departments) = Build(Linked, link);

			var resources = MutualAid(await service.GetAssignableResourcesForIncidentAsync(Linked)).ToList();

			resources.Should().ContainSingle().Which.Should().Match<AssignableResource>(r =>
				r.DepartmentId == Creator && r.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit);
			departments.Verify(x => x.GetAllPersonnelNamesForDepartmentAsync(Creator), Times.Never);
		}

		[Test]
		public async Task The_creator_gets_only_what_the_linked_department_shares_back()
		{
			var link = Link();
			link.DepartmentShareUnits = link.DepartmentSharePersonnel = true; // the creator's own flags: not for the creator
			link.LinkedDepartmentSharePersonnel = true;
			var (service, units, _) = Build(Creator, link);

			var resources = MutualAid(await service.GetAssignableResourcesForIncidentAsync(Creator)).ToList();

			resources.Should().ContainSingle().Which.Should().Match<AssignableResource>(r =>
				r.DepartmentId == Linked && r.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel);
			units.Verify(x => x.GetUnitsForDepartmentAsync(Linked), Times.Never);
		}

		[TestCase(false, true)]
		[TestCase(true, false)]
		public async Task A_disabled_or_unaccepted_link_adds_nothing(bool enabled, bool accepted)
		{
			var link = Link(enabled, accepted);
			link.DepartmentShareUnits = link.DepartmentSharePersonnel = true;
			link.LinkedDepartmentShareUnits = link.LinkedDepartmentSharePersonnel = true;
			var (service, _, _) = Build(Linked, link);

			MutualAid(await service.GetAssignableResourcesForIncidentAsync(Linked)).Should().BeEmpty();
		}
	}
}
