using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Who is allowed to act as a commander, per the <see cref="PermissionTypes.CommandAppLogin"/>
	/// permission: signing in to the IC app, establishing command on a call, and reading command boards.
	///
	/// The mirror of <see cref="IDispatchAccessService"/>, and enforced the same way — on the server, not
	/// just in the app. A command board is only a client of the shared API, so a client-side check alone
	/// would keep nothing private.
	///
	/// Defaults to allowing everyone in the department, so departments that never configure it are
	/// unaffected.
	/// </summary>
	public interface ICommandAccessService
	{
		/// <summary>True when this user may act as a commander for the department.</summary>
		Task<bool> CanUseCommandAsync(int departmentId, string userId);

		/// <summary>Every user in the department who may act as a commander.</summary>
		Task<List<string>> GetCommandUserIdsAsync(int departmentId);

		/// <summary>
		/// True when this user may ASSIST on a command board they hold no ICS role on — the capability set
		/// a dispatcher needs to help work an incident.
		///
		/// Stricter than <see cref="CanUseCommandAsync"/> on purpose: it additionally requires the
		/// department to have deliberately narrowed <see cref="PermissionTypes.CommandAppLogin"/>. The
		/// permission defaults to Everyone so nothing breaks on upgrade, and inferring "therefore every
		/// member may move resources on any board" from that open default would hand out authority no one
		/// asked for. Once a department picks who commands, those people are trusted to assist.
		/// </summary>
		Task<bool> CanAssistWithCommandAsync(int departmentId, string userId);
	}
}
