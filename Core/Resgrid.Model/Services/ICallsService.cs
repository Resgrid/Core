using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface ICallsService
	/// </summary>
	public interface ICallsService
	{
		/// <summary>
		/// Saves the call asynchronous.
		/// </summary>
		/// <param name="call">The call.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Call&gt;.</returns>
		Task<Call> SaveCallAsync(Call call, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Regenerates the call numbers asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="year">The local year to regenerate call numbers for.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> RegenerateCallNumbersAsync(int departmentId, int year,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the current call number asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetCurrentCallNumberAsync(DateTime utcDate, int departmentId);

		/// <summary>
		/// Gets all calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Call&gt;&gt;.</returns>
		Task<List<Call>> GetAllCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all calls by department date range asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;List&lt;Call&gt;&gt;.</returns>
		Task<List<Call>> GetAllCallsByDepartmentDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets the active calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Call&gt;&gt;.</returns>
		Task<List<Call>> GetActiveCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the latest10 active calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Call&gt;&gt;.</returns>
		Task<List<Call>> GetLatest10ActiveCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the closed calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Call&gt;&gt;.</returns>
		Task<List<Call>> GetClosedCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the closed calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// /// <param name="year">The year.</param>
		/// <returns>Task&lt;List&lt;Call&gt;&gt;.</returns>
		Task<List<Call>> GetClosedCallsByDepartmentYearAsync(int departmentId, string year);

		/// <summary>
		/// Deletes the call by identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteCallByIdAsync(int callId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Res the open call by identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Call&gt;.</returns>
		Task<Call> ReOpenCallByIdAsync(int callId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the call by identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;Call&gt;.</returns>
		Task<Call> GetCallByIdAsync(int callId, bool bypassCache = true);

		/// <summary>
		/// Gets the today calls count asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetTodayCallsCountAsync(int departmentId);

		/// <summary>
		/// Gets the active calls for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetActiveCallsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Deletes the dispatches asynchronous.
		/// </summary>
		/// <param name="dispatches">The dispatches.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteDispatchesAsync(List<CallDispatch> dispatches,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the group dispatches asynchronous.
		/// </summary>
		/// <param name="dispatches">The dispatches.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteGroupDispatchesAsync(List<CallDispatchGroup> dispatches,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the role dispatches asynchronous.
		/// </summary>
		/// <param name="dispatches">The dispatches.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteRoleDispatchesAsync(List<CallDispatchRole> dispatches,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the unit dispatches asynchronous.
		/// </summary>
		/// <param name="dispatches">The dispatches.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteUnitDispatchesAsync(List<CallDispatchUnit> dispatches,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the last2 month calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Call&gt;&gt;.</returns>
		Task<List<Call>> GetLast2MonthCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the call types for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CallType&gt;&gt;.</returns>
		Task<List<CallType>> GetCallTypesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Deletes the call type asynchronous.
		/// </summary>
		/// <param name="callTypeId">The call type identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteCallTypeAsync(int callTypeId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the call type by identifier asynchronous.
		/// </summary>
		/// <param name="callTypeId">The call type identifier.</param>
		/// <returns>Task&lt;CallType&gt;.</returns>
		Task<CallType> GetCallTypeByIdAsync(int callTypeId);

		/// <summary>
		/// Saves the new call type asynchronous.
		/// </summary>
		/// <param name="callType">Type of the call.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CallType&gt;.</returns>
		Task<CallType> SaveCallTypeAsync(CallType callType, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Generates the call from email.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="email">The email.</param>
		/// <param name="managingUser">The managing user.</param>
		/// <param name="users">The users.</param>
		/// <param name="department">The department.</param>
		/// <param name="activeCalls">The active calls.</param>
		/// <param name="units">The units.</param>
		/// <param name="priority">The priority.</param>
		/// <param name="activePriorities">The active priorities.</param>
		/// <param name="callTypes">The active call types.</param>
		/// <returns>Call.</returns>
		Task<Call> GenerateCallFromEmail(int type, CallEmail email, string managingUser, List<IdentityUser> users,
			Department department, List<Call> activeCalls, List<Unit> units, int priority,
			List<DepartmentCallPriority> activePriorities, List<CallType> callTypes);

		/// <summary>
		/// Saves the call note asynchronous.
		/// </summary>
		/// <param name="note">The note.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CallNote&gt;.</returns>
		Task<CallNote> SaveCallNoteAsync(CallNote note, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the call attachment asynchronous.
		/// </summary>
		/// <param name="callAttachmentId">The call attachment identifier.</param>
		/// <returns>Task&lt;CallAttachment&gt;.</returns>
		Task<CallAttachment> GetCallAttachmentAsync(int callAttachmentId);

		/// <summary>
		/// Saves the call attachment asynchronous.
		/// </summary>
		/// <param name="attachment">The attachment.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CallAttachment&gt;.</returns>
		Task<CallAttachment> SaveCallAttachmentAsync(CallAttachment attachment,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Marks the call dispatches as sent asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="usersToMark">The users to mark.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> MarkCallDispatchesAsSentAsync(int callId, List<Guid> usersToMark);

		/// <summary>
		/// Gets all call priorities asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;DepartmentCallPriority&gt;&gt;.</returns>
		Task<List<DepartmentCallPriority>> GetAllCallPrioritiesAsync();

		/// <summary>
		/// Gets the call priorities by identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="priorityId">The priority identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;DepartmentCallPriority&gt;.</returns>
		Task<DepartmentCallPriority> GetCallPrioritiesByIdAsync(int departmentId, int priorityId,
			bool bypassCache = false);

		/// <summary>
		/// Gets the call priorities for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;List&lt;DepartmentCallPriority&gt;&gt;.</returns>
		Task<List<DepartmentCallPriority>> GetCallPrioritiesForDepartmentAsync(int departmentId,
			bool bypassCache = false);

		/// <summary>
		/// Gets the active call priorities for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;List&lt;DepartmentCallPriority&gt;&gt;.</returns>
		Task<List<DepartmentCallPriority>> GetActiveCallPrioritiesForDepartmentAsync(int departmentId,
			bool bypassCache = false);

		/// <summary>
		/// Gets the default call priorities.
		/// </summary>
		/// <returns>List&lt;DepartmentCallPriority&gt;.</returns>
		List<DepartmentCallPriority> GetDefaultCallPriorities();

		/// <summary>
		/// Saves the call priority asynchronous.
		/// </summary>
		/// <param name="callPriority">The call priority.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentCallPriority&gt;.</returns>
		Task<DepartmentCallPriority> SaveCallPriorityAsync(DepartmentCallPriority callPriority,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the call protocols by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;List&lt;CallProtocol&gt;&gt;.</returns>
		Task<List<CallProtocol>> GetCallProtocolsByCallIdAsync(int callId);

		/// <summary>
		/// Gets all the years that contain calls for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;string&gt;&gt;.</returns>
		Task<List<string>> GetCallYearsByDeptartmentAsync(int departmentId);

		/// <summary>
		/// Invalidates the call priorities for department in cache.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		void InvalidateCallPrioritiesForDepartmentInCache(int departmentId);

		/// <summary>
		/// Gets the shortened audio URL asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="callAttachmentId">The call attachment identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetShortenedAudioUrlAsync(int callId, int callAttachmentId);

		/// <summary>
		/// Gets the shortened call link URL.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="pdf">if set to <c>true</c> [PDF].</param>
		/// <param name="stationId">The station identifier.</param>
		/// <returns>System.String.</returns>
		Task<string> GetShortenedCallLinkUrl(int callId, bool pdf = false, int? stationId = null);

		/// <summary>
		/// Gets the shortened call PDF URL.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="pdf">if set to <c>true</c> [PDF].</param>
		/// <param name="stationId">The station identifier.</param>
		/// <returns>System.String.</returns>
		Task<string> GetShortenedCallPdfUrl(int callId, bool pdf = false, int? stationId = null);

		/// <summary>
		/// Gets the call PDF URL.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="pdf">if set to <c>true</c> [PDF].</param>
		/// <param name="stationId">The station identifier.</param>
		/// <returns>System.String.</returns>
		string GetCallPdfUrl(int callId, bool pdf = false, int? stationId = null);

		/// <summary>
		/// Clears the group for dispatches asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ClearGroupForDispatchesAsync(int departmentGroupId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Calls the state to string.
		/// </summary>
		/// <param name="state">The state.</param>
		/// <returns>System.String.</returns>
		string CallStateToString(CallStates state);

		/// <summary>
		/// Calls the color of the state to.
		/// </summary>
		/// <param name="state">The state.</param>
		/// <returns>System.String.</returns>
		string CallStateToColor(CallStates state);

		/// <summary>
		/// Calls the priority to string asynchronous.
		/// </summary>
		/// <param name="priority">The priority.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> CallPriorityToStringAsync(int priority, int departmentId);

		/// <summary>
		/// Calls the priority to color asynchronous.
		/// </summary>
		/// <param name="priority">The priority.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> CallPriorityToColorAsync(int priority, int departmentId);

		/// <summary>
		/// Populates the call data.
		/// </summary>
		/// <param name="call">The call.</param>
		/// <param name="getDispatches">if set to <c>true</c> [get dispatches].</param>
		/// <param name="getAttachments">if set to <c>true</c> [get attachments].</param>
		/// <param name="getNotes">if set to <c>true</c> [get notes].</param>
		/// <param name="getGroupDispatches">if set to <c>true</c> [get group dispatches].</param>
		/// <param name="getUnitDispatches">if set to <c>true</c> [get unit dispatches].</param>
		/// <param name="getRoleDispatches">if set to <c>true</c> [get role dispatches].</param>
		/// <param name="getProtocols">if set to <c>true</c> [get protocols].</param>
		/// <param name="getReferences">if set to <c>true</c> [get references].</param>
		/// <returns>Task&lt;Call&gt;.</returns>
		Task<Call> PopulateCallData(Call call, bool getDispatches, bool getAttachments, bool getNotes, bool getGroupDispatches, bool getUnitDispatches, bool getRoleDispatches, bool getProtocols, bool getReferences, bool getContacts);

		Task<List<Call>> GetAllNonDispatchedScheduledCallsWithinDateRange(DateTime startDate, DateTime endDate);


		Task<List<Call>> GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(int departmentId);

		Task<List<CallReference>> GetChildCallsForCallAsync(int callId);

		Task<bool> DeleteCallReferenceAsync(CallReference callReference, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<Call>> GetCallsByContactIdAsync(string contactId, int departmentId);

		Task<bool> DeleteCallContactsAsync(int callId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
