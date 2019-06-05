using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model.Services
{
	public interface ICallsService
	{
		Call SaveCall(Call call);
		List<Call> GetAllCallsByDepartment(int departmentId);
		void DeleteCallById(int callId);
		Call GetCallById(int callId, bool bypassCache = true);
		int GetTodayCallsCount(int departmentId);
		int GetActiveCallsForDepartment(int departmentId);
		void DeleteDispatchesForUserAndRemapCalls(string remapToUserId, string UserIdToDelete);
		Dictionary<string, int> GetNewCallsCountForLast5Days();
		List<Call> GetActiveCallsByDepartment(int departmentId);
		void DeleteDispatches(List<CallDispatch> dispatches);
		List<Call> GetLast2MonthCallsByDepartment(int departmentId);
		List<CallType> GetCallTypesForDepartment(int departmentId);
		void DeleteCallType(int callTypeId);
		CallType SaveNewCallType(string callType, int departmentId);
		Call GenerateCallFromEmail(int type, CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority);
		List<Call> GetClosedCallsByDepartment(int departmentId);
		CallType GetCallTypeById(int callTypeId);
		List<int> GetCallsCountForLast7DaysForDepartment(int departmentId);
		string GetCurrentCallNumber(int departmentId);
		List<Call> GetAllCallsByDepartmentDateRange(int departmentId, DateTime startDate, DateTime endDate);
		string CallStateToString(CallStates state);
		string CallStateToColor(CallStates state);
		string CallPriorityToString(int priority, int departmentId);
		string CallPriorityToColor(int priority, int departmentId);
		CallNote SaveCallNote(CallNote note);
		CallAttachment GetCallAttachment(int callAttachmentId);
		CallAttachment SaveCallAttachment(CallAttachment attachment);
		void DeleteGroupDispatches(List<CallDispatchGroup> dispatches);
		void DeleteRoleDispatches(List<CallDispatchRole> dispatches);
		void DeleteUnitDispatches(List<CallDispatchUnit> dispatches);
		void RegenerateCallNumbers(int departmentId);
		List<DepartmentCallPriority> GetCallPrioritesForDepartment(int departmentId, bool bypassCache = false);
		DepartmentCallPriority SaveCallPriority(DepartmentCallPriority callPriority);
		void InvalidateCallPrioritiesForDepartmentInCache(int departmentId);
		List<DepartmentCallPriority> GetDefaultCallPriorites();
		DepartmentCallPriority GetCallPrioritesById(int departmentId, int priorityId, bool bypassCache = false);
		void ReOpenCallById(int callId);
		List<Call> GetLatest10ActiveCallsByDepartment(int departmentId);
		List<DepartmentCallPriority> GetActiveCallPrioritesForDepartment(int departmentId, bool bypassCache = false);
		Task<List<CallType>> GetCallTypesForDepartmentAsync(int departmentId);
		Task<string> GetShortenedAudioUrlAsync(int callId, int callAttachmentId);
		string GetShortenedAudioUrl(int callId, int callAttachmentId);
		void ClearGroupForDispatches(int departmentGroupId);
		List<Call> GetActiveCallsByDepartmentForUpdate(int departmentId);
		string GetShortenedCallLinkUrl(int callId);
	}
}
