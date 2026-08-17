namespace Resgrid.Model
{
	/// <summary>
	/// What a <see cref="CommunicationTestTarget"/> row points at. A test with no target rows
	/// covers every member of the department (the original behavior).
	/// </summary>
	public enum CommunicationTestTargetType
	{
		Group = 0,
		Role = 1,
		User = 2
	}
}
