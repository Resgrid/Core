namespace Resgrid.Model.Services
{
	/// <summary>
	/// The Protected Data Grant the current unit of work is acting under (ADP plan 3.3 / 7.2). Web and API
	/// hosts populate it from the <c>X-Resgrid-Protected-Grant</c> header per request; workers and tests carry
	/// no grant and identify themselves as workload callers, which the write seam maps onto the broker's
	/// encrypt-only lane. RMS services read it instead of threading a token through every command, because
	/// the grant is a property of the caller, not of the record being changed.
	/// </summary>
	public interface IProtectedGrantContext
	{
		/// <summary>The caller's grant token, or null when none was presented.</summary>
		string GrantToken { get; }

		/// <summary>The authenticated user behind the call, or null for a workload.</summary>
		string UserId { get; }

		/// <summary>True when no attended user is behind the call (worker, system principal, relay).</summary>
		bool IsWorkloadCaller { get; }
	}
}
