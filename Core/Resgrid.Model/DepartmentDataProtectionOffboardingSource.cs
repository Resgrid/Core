namespace Resgrid.Model
{
	/// <summary>
	/// What triggered scheduling of ADP offboarding for a department. UserCancelled = the managing
	/// member cancelled the addon (offboarding at end of paid cycle, revocable until the first
	/// offboarding window opens); DunningExhausted = payment failure dunning ran out (paid period plus
	/// fixed grace); Chargeback = chargeback/refund treated as cancellation with immediate effective
	/// date — offboarding still runs through the normal worker path, never an instant crypto flip.
	/// </summary>
	public enum DepartmentDataProtectionOffboardingSource
	{
		UserCancelled = 1,
		DunningExhausted = 2,
		Chargeback = 3
	}
}
