using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Aggregates assignable resources across the own department and its accepted mutual-aid links. Read-only;
	/// honors the per-direction <c>DepartmentLink</c> share flags and never exposes personnel from a link that
	/// does not share personnel toward this department.
	/// </summary>
	public class MutualAidService : IMutualAidService
	{
		private readonly IDepartmentLinksService _departmentLinksService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentsService _departmentsService;

		public MutualAidService(
			IDepartmentLinksService departmentLinksService,
			IUnitsService unitsService,
			IDepartmentsService departmentsService)
		{
			_departmentLinksService = departmentLinksService;
			_unitsService = unitsService;
			_departmentsService = departmentsService;
		}

		public async Task<List<AssignableResource>> GetAssignableResourcesForIncidentAsync(int departmentId)
		{
			var resources = new List<AssignableResource>();

			// Own department resources.
			await AddDepartmentResourcesAsync(resources, departmentId, isMutualAid: false, color: null, includeUnits: true, includePersonnel: true);

			// Linked (mutual-aid) department resources shared toward us.
			var links = await _departmentLinksService.GetAllLinksForDepartmentAsync(departmentId);
			if (links != null)
			{
				foreach (var link in links)
				{
					if (!link.LinkEnabled || link.LinkAccepted == null)
						continue;

					int otherDepartmentId;
					bool sharesUnits;
					bool sharesPersonnel;
					string color;

					// Determine the other party and what THEY share toward us (flags are per-direction).
					if (link.DepartmentId == departmentId)
					{
						otherDepartmentId = link.LinkedDepartmentId;
						sharesUnits = link.LinkedDepartmentShareUnits;
						sharesPersonnel = link.LinkedDepartmentSharePersonnel;
						color = link.LinkedDepartmentColor;
					}
					else
					{
						otherDepartmentId = link.DepartmentId;
						sharesUnits = link.DepartmentShareUnits;
						sharesPersonnel = link.DepartmentSharePersonnel;
						color = link.DepartmentColor;
					}

					await AddDepartmentResourcesAsync(resources, otherDepartmentId, isMutualAid: true, color: color, includeUnits: sharesUnits, includePersonnel: sharesPersonnel);
				}
			}

			return resources;
		}

		private async Task AddDepartmentResourcesAsync(List<AssignableResource> resources, int departmentId, bool isMutualAid, string color, bool includeUnits, bool includePersonnel)
		{
			if (includeUnits)
			{
				var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId);
				if (units != null)
				{
					foreach (var unit in units)
					{
						resources.Add(new AssignableResource
						{
							ResourceKind = (int)(isMutualAid ? ResourceAssignmentKind.LinkedDeptUnit : ResourceAssignmentKind.RealUnit),
							ResourceId = unit.UnitId.ToString(),
							Name = unit.Name,
							DepartmentId = departmentId,
							IsMutualAid = isMutualAid,
							Color = color
						});
					}
				}
			}

			if (includePersonnel)
			{
				var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId);
				if (names != null)
				{
					foreach (var person in names)
					{
						resources.Add(new AssignableResource
						{
							ResourceKind = (int)(isMutualAid ? ResourceAssignmentKind.LinkedDeptPersonnel : ResourceAssignmentKind.RealPersonnel),
							ResourceId = person.UserId,
							Name = $"{person.FirstName} {person.LastName}".Trim(),
							DepartmentId = departmentId,
							IsMutualAid = isMutualAid,
							Color = color
						});
					}
				}
			}
		}
	}
}
