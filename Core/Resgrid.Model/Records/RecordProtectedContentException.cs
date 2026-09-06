using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Thrown when an RMS operation needs protected content it cannot see or write (ADP plan 3.3): the caller
	/// presented no grant, the grant expired or its epoch was revoked, the workload purpose is not acknowledged,
	/// or the broker is unavailable. <see cref="Reason"/> is the machine-readable code the clients map onto the
	/// step-up flow (step_up_required, grant_expired, grant_revoked, protected_access_denied,
	/// workload_purpose_denied, broker_unavailable); it never carries a value.
	/// </summary>
	public class RecordProtectedContentException : InvalidOperationException
	{
		public RecordProtectedContentException(string reason, string operation)
			: base($"Protected record content is unavailable for '{operation}' ({reason}). Verify with a Protected Data grant and try again.")
		{
			Reason = reason ?? "step_up_required";
			Operation = operation;
		}

		public string Reason { get; }

		public string Operation { get; }
	}
}
