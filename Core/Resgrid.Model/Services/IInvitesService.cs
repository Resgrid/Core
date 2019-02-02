using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IInvitesService
	{
		List<Invite> GetAllInvitesForDepartment(int departmentId);
		void CreateInvites(Department department, string addingUserId, List<string> emailAddresses);
		Invite GetInviteByCode(Guid inviteCode);
		void CompleteInvite(Guid inviteCode, string userId);
		void ResendInvite(int inviteId);
		void DeleteInvite(int inviteId);
		Invite GetInviteById(int inviteId);
		Invite GetInviteByEmail(string emailAddress);
	}
}