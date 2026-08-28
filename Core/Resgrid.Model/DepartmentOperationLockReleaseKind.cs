namespace Resgrid.Model
{
	/// <summary>
	/// How a DepartmentOperationLocks row was released. Completed = migration finished; Checkpoint =
	/// nightly window closed with the cursor durably checkpointed; Aborted = managing member or operator
	/// break-glass abort (dispatch beats migration); Expired = worker heartbeat went stale past
	/// ExpiresUtc and enforcement ended automatically.
	/// </summary>
	public enum DepartmentOperationLockReleaseKind
	{
		Completed = 1,
		Checkpoint = 2,
		Aborted = 3,
		Expired = 4
	}
}
