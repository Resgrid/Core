using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IAuthorizationService
	/// </summary>
	public interface IAuthorizationService
	{
		/// <summary>
		/// Determines whether this instance [can user manage invite asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="inviteId">The invite identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserManageInviteAsync(string userId, int inviteId);

		/// <summary>
		/// Determines whether this instance [can user view call asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewCallAsync(string userId, int callId);

		/// <summary>
		/// Determines whether this instance [can user edit call asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserEditCallAsync(string userId, int callId);

		/// <summary>
		/// Determines whether this instance [can user view message asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="messageId">The message identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewMessageAsync(string userId, int messageId);

		/// <summary>
		/// Determines whether this instance [can user view message] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="message">The message.</param>
		/// <returns><c>true</c> if this instance [can user view message] the specified user identifier; otherwise, <c>false</c>.</returns>
		bool CanUserViewMessage(string userId, Message message);

		/// <summary>
		/// Determines whether this instance [can user view and edit call log asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="callLogId">The call log identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewAndEditCallLogAsync(string userId, int callLogId);

		/// <summary>
		/// Determines whether this instance [can user view and edit work log asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewAndEditWorkLogAsync(string userId, int logId);

		/// <summary>
		/// Determines whether this instance [can user view payment asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="paymentId">The payment identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewPaymentAsync(string userId, int paymentId);

		/// <summary>
		/// Determines whether this instance [can user edit department group asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserEditDepartmentGroupAsync(string userId, int departmentGroupId);

		/// <summary>
		/// Determines whether this instance [can user edit role asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="personnelRoleId">The personnel role identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserEditRoleAsync(string userId, int personnelRoleId);

		/// <summary>
		/// Determines whether this instance [can user view role asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="personnelRoleId">The personnel role identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewRoleAsync(string userId, int personnelRoleId);

		/// <summary>
		/// Determines whether [is user valid within limits asynchronous] [the specified user identifier].
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> IsUserValidWithinLimitsAsync(string userId, int departmentId);

		/// <summary>
		/// Determines whether this instance [can user modify unit asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserModifyUnitAsync(string userId, int unitId);

		/// <summary>
		/// Determines whether this instance [can user view unit asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewUnitAsync(string userId, int unitId);

		/// <summary>
		/// Determines whether this instance [can user view user asynchronous] the specified viewer user identifier.
		/// </summary>
		/// <param name="viewerUserId">The viewer user identifier.</param>
		/// <param name="targetUserId">The target user identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewUserAsync(string viewerUserId, string targetUserId);

		/// <summary>
		/// Determines whether this instance [can group admins add users asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanGroupAdminsAddUsersAsync(int departmentId);

		/// <summary>
		/// Determines whether this instance [can group admins remove users asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanGroupAdminsRemoveUsersAsync(int departmentId);

		/// <summary>
		/// Determines whether this instance [can user add new user asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserAddNewUserAsync(int departmentId, string userId);

		/// <summary>
		/// Determines whether this instance [can user delete user asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="userIdToDelete">The user identifier to delete.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserDeleteUserAsync(int departmentId, string userId, string userIdToDelete);

		/// <summary>
		/// Determines whether this instance [can user create call asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserCreateCallAsync(string userId, int departmentId);

		/// <summary>
		/// Determines whether this instance [can user view pii asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewPIIAsync(string userId, int departmentId);

		/// <summary>
		/// Determines whether this instance [can user create note asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserCreateNoteAsync(string userId, int departmentId);

		/// <summary>
		/// Determines whether this instance [can user modify calendar entry asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="calendarItemId">The calendar item identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserModifyCalendarEntryAsync(string userId, int calendarItemId);

		/// <summary>
		/// Determines whether this instance [can user edit profile asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="editingProfileId">The editing profile identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserEditProfileAsync(string userId, int departmentId, string editingProfileId);

		/// <summary>
		/// Determines whether this instance [can user modify protocol asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="protocolId">The protocol identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserModifyProtocolAsync(string userId, int protocolId);

		/// <summary>
		/// Determines whether this instance [can user view protocol asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="protocolId">The protocol identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewProtocolAsync(string userId, int protocolId);

		/// <summary>
		/// Determines whether this instance [can user manage subscription asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserManageSubscriptionAsync(string userId, int departmentId);

		/// <summary>
		/// Determines whether this instance [can delete work log asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserDeleteWorkLogAsync(string userId, int logId);

		/// <summary>
		/// Determines whether this instance [can delete shift signup asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="shiftSignupId">The shift signup identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserDeleteShiftSignupAsync(string userId, int departmentId, int shiftSignupId);

		/// <summary>
		/// Determines whether this instance [can view unit location asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewUnitLocationAsync(string userId, int unitId, int departmentId);

		/// <summary>
		/// Determines whether this instance [can view person location asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The current user identifier.</param>
		/// <param name="targetUserId">The target user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewPersonLocationAsync(string userId, string targetUserId, int departmentId);

		/// <summary>
		/// Determines whether this instance [can view person asynchronous] the specified user identifier.
		/// </summary>
		/// <param name="userId">The current user identifier.</param>
		/// <param name="targetUserId">The target user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanUserViewPersonAsync(string userId, string targetUserId, int departmentId);

		Task<bool> CanUserDeleteCallAsync(string userId, int callId, int departmentId);

		Task<bool> CanUserCloseCallAsync(string userId, int callId, int departmentId);

		Task<bool> CanUserAddCallDataAsync(string userId, int callId, int departmentId);

		Task<bool> CanUserDeleteDepartmentAsync(string userId, int departmentId);

		Task<bool> CanUserModifyCustomStatusAsync(string userId, int customStatusId);

		Task<bool> CanUserModifyCustomStateDetailAsync(string userId, int customStateDetailId);

		Task<bool> CanUserModifyCallTypeAsync(string userId, int callTypeId);

		Task<bool> CanUserAddCallTypeAsync(string userId);

		Task<bool> CanUserAddCallPriorityAsync(string userId);

		Task<bool> CanUserDeleteCallPriorityAsync(string userId, int priorityId);

		Task<bool> CanUserEditCallPriorityAsync(string userId, int priorityId);

		Task<bool> CanUserAddUnitTypeAsync(string userId);

		Task<bool> CanUserEditUnitTypeAsync(string userId, int unitTypeId);

		Task<bool> CanUserAddCertificationTypeAsync(string userId);

		Task<bool> CanUserAddDocumentTypeAsync(string userId);

		Task<bool> CanUserDeleteCertificationTypeAsync(string userId, int certificationTypeId);

		Task<bool> CanUserDeleteDocumentTypeAsync(string userId, string documentTypeId);

		Task<bool> CanUserAddNoteTypeAsync(string userId);

		Task<bool> CanUserDeleteNoteTypeAsync(string userId, string noteTypeId);

		Task<bool> CanUserAddNoteAsync(string userId);

		Task<bool> CanUserEditNoteAsync(string userId, int noteId);

		Task<bool> CanUserViewPersonViaMatrixAsync(string userToView, string userId, int departmentId);

		Task<bool> CanUserViewPersonLocationViaMatrixAsync(string userToView, string userId, int departmentId);

		Task<bool> CanUserViewUnitViaMatrixAsync(int unitToView, string userId, int departmentId);

		Task<bool> CanUserViewUnitLocationViaMatrixAsync(int unitToView, string userId, int departmentId);

		Task<bool> CanUserViewAllPeopleAsync(string userId, int departmentId);

		Task<bool> CanUserDeleteContactNoteTypeAsync(string userId, string contactNoteTypeId);

		Task<bool> CanUserEditContactNoteTypeAsync(string userId, string contactNoteTypeId);

		Task<bool> CanUserDeleteContactAsync(string userId, int departmentId);

		Task<bool> CanUserAddOrEditContactAsync(string userId, int departmentId);
	}
}
