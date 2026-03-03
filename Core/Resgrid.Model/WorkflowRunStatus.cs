namespace Resgrid.Model
{
	public enum WorkflowRunStatus
	{
		Pending = 0,
		Running = 1,
		Completed = 2,
		Failed = 3,
		Cancelled = 4,
		Retrying = 5,
		Skipped = 6
	}
}

