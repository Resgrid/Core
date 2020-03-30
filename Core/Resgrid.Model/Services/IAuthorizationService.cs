namespace Resgrid.Model.Services
{
	public interface IAuthorizationService
	{
		bool CanUserDeletePushUri(string userId, int pushUriId);
		bool CanUserManageInvite(string userId, int inviteId);
		bool CanUserViewCall(string userId, int callId);
		bool CanUserEditCall(string userId, int callId);
		bool CanUserViewMessage(string userId, int messageId);
		bool CanUserViewAndEditCallLog(string userId, int callLogId);
		bool CanUserViewAndEditWorkLog(string userId, int logId);
		bool CanUserViewPayment(string userId, int paymentId);
		bool CanUserEditDepartmentGroup(string userId, int departmentGroupId);
		bool CanUserEditRole(string userId, int personnelRoleId);
		bool CanUserViewRole(string userId, int personnelRoleId);
		bool IsUserValidWithinLimits(string userId, int departmentId);
		bool CanUserModifyUnit(string userId, int unitId);
		bool CanUserViewUnit(string userId, int unitId);
		bool CanUserViewUser(string viewerUserId, string targetUserId);
		bool CanGroupAdminsAddUsers(int departmentId);
		bool CanGroupAdminsRemoveUsers(int departmentId);
		bool CanUserAddNewUser(int departmentId, string userId);
		bool CanUserDeleteUser(int departmentId, string userId, string UserIdToDelete);
		bool CanUserViewMessage(string userId, Message message);
		bool CanUserCreateCall(string userId, int departmentId);
		bool CanUserViewPII(string userId, int departmentId);
		bool CanUserCreateNote(string userId, int departmentId);
		bool CanUserModifyCalendarEntry(string userId, int calendarItemId);
		bool CanUserEditProfile(string userId, int departmentId, string editingProfileId);
	}
}
