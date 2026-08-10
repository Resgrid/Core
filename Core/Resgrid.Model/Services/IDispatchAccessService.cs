using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Who is allowed to work the dispatch desk, per the <see cref="PermissionTypes.DispatchAppLogin"/>
	/// permission.
	///
	/// This is the single authority for that question. It gates signing in to the Dispatch app AND
	/// membership of the incident dispatch chat channel — the app is only a client of the shared API, so
	/// a client-side check alone would keep nothing private. Anyone the department hasn't authorized
	/// simply resolves to no dispatch channel, whichever app they are running.
	///
	/// Defaults to allowing everyone in the department: departments that are entirely dispatchers, or
	/// that have never configured the permission, keep working unchanged.
	/// </summary>
	public interface IDispatchAccessService
	{
		/// <summary>True when this user may work dispatch for the department.</summary>
		Task<bool> CanUseDispatchAsync(int departmentId, string userId);

		/// <summary>
		/// Every user in the department who may work dispatch — the audience for anything addressed to
		/// "Dispatch", since whichever dispatcher is on shift needs to see it.
		/// </summary>
		Task<List<string>> GetDispatchUserIdsAsync(int departmentId);
	}
}
