using System;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Exempts an MVC action (or a whole controller) from <see cref="DepartmentLockActionFilter"/>
	/// mutation blocking. Use ONLY for flows that must stay available while a department operation
	/// lock is active: authentication/session, billing/subscription management, and the ADP
	/// lock-abort path itself — the abort can never be blocked by the very lock it releases.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
	public sealed class AllowDuringDepartmentLockAttribute : Attribute
	{
	}
}
