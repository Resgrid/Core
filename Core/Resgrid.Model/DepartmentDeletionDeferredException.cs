using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Thrown inside the department deletion transaction when the deletion may not run yet: an active Records
	/// legal hold blocks department purge (RMS plan, legal hold). The transaction rolls back and the pending
	/// request stays pending without spending a retry attempt, so it completes once the hold is released.
	/// </summary>
	public class DepartmentDeletionDeferredException : InvalidOperationException
	{
		public DepartmentDeletionDeferredException(string message) : base(message) { }
	}
}
