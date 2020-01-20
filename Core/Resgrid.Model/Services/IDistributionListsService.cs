using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IDistributionListsService
	{
		List<DistributionList> GetAll();
		DistributionList GetDistributionListById(int distributionListId);
		List<DistributionList> GetDistributionListsByDepartmentId(int departmentId);
		DistributionList SaveDistributionList(DistributionList distributionList);
		void DeleteDistributionListsById(int distributionListId);
		DistributionList SaveDistributionListOnly(DistributionList distributionList);
		List<DistributionList> GetAllActiveDistributionLists();
	    List<DistributionListMember> GetAllListMembersByListId(int listId);
	    void RemoveUserFromAllLists(string userId);
		DistributionList GetDistributionListByAddress(string emailAddress);
	}
}
