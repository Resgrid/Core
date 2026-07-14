using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Thrown when a resource assignment (or move) targets a command board lane whose source template
	/// role has <c>ForceRequirements</c> enabled and the resource does not satisfy the lane's required
	/// unit types / personnel roles. The message is safe to surface to the caller.
	/// </summary>
	public class CommandRequirementsNotMetException : Exception
	{
		public CommandRequirementsNotMetException(string message) : base(message)
		{
		}
	}
}
