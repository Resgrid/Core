using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Projection row: the most recent DispatchedOn for a user across all calls in a
	/// department. Feeds the rest-period deprioritization in dispatch recommendations.
	/// </summary>
	public class UserLastDispatchTime
	{
		public string UserId { get; set; }

		public DateTime LastDispatchedOn { get; set; }
	}
}
