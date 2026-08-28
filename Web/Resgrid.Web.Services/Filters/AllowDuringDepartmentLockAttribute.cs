using System;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// Exempts an endpoint (or a whole controller) from <see cref="DepartmentLockActionFilter"/>
	/// mutation blocking. Use ONLY for operations that must stay available while a department
	/// operation lock is active: authentication/session flows, capability/status reads that happen
	/// to use POST, and the ADP lock-abort path itself (dispatch beats migration — the abort can
	/// never be blocked by the very lock it releases).
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
	public sealed class AllowDuringDepartmentLockAttribute : Attribute
	{
	}
}
