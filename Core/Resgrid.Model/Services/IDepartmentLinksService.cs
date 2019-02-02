using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IDepartmentLinksService
	{
		List<DepartmentLink> GetAllLinksForDepartment(int departmentId);
		DepartmentLink Save(DepartmentLink link);
		bool DoesLinkExist(int departmentId, int departmentLinkId);
		Department GetDepartmentByLinkCode(string code);
		DepartmentLink GetLinkById(int linkId);
	}
}