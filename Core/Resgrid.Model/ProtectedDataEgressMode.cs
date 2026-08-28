namespace Resgrid.Model
{
	/// <summary>
	/// Per-channel egress mode for protected content in DepartmentProtectedDataEgressPolicies. Every
	/// channel defaults to GenericOnly ("A protected dispatch is available. Sign in to Resgrid to view
	/// details."). ProtectedAfterPin (SMS/voice only) releases a minimum approved subset after a
	/// one-time PIN challenge. AllowProtectedContent requires an explicit, versioned administrator
	/// warning acknowledgement. Legacy clients and payloads always fall back to GenericOnly.
	/// </summary>
	public enum ProtectedDataEgressMode
	{
		GenericOnly = 0,
		ProtectedAfterPin = 1,
		AllowProtectedContent = 2
	}
}
