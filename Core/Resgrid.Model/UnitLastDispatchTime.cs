using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Projection row: the most recent DispatchedOn for a unit across all calls in a
	/// department. Feeds the rest-period deprioritization in dispatch recommendations.
	/// </summary>
	public class UnitLastDispatchTime
	{
		public int UnitId { get; set; }

		public DateTime LastDispatchedOn { get; set; }
	}
}
