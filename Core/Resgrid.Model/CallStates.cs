namespace Resgrid.Model
{
	public enum CallStates
	{
		Active	= 0,
		Closed = 1,
		Cancelled = 2,
		Unfounded = 3,
		Founded = 4,
		Minor = 5
	}

	public enum ClosedOnlyCallStates
	{
		Closed = 1,
		Cancelled = 2,
		Unfounded = 3,
		Founded = 4,
		Minor = 5
	}
}
