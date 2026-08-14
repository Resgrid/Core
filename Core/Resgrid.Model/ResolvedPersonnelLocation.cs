using System;

namespace Resgrid.Model
{
	/// <summary>
	/// A person's freshest known position after arbitrating between the
	/// PersonnelLocation document store and ActionLog coordinates. Personnel-side
	/// counterpart of ResolvedUnitLocation.
	/// </summary>
	public sealed class ResolvedPersonnelLocation
	{
		public string UserId { get; set; }

		public double Latitude { get; set; }

		public double Longitude { get; set; }

		public DateTime Timestamp { get; set; }

		/// <summary>True when the fix is older than the caller's max-age constraint.</summary>
		public bool IsStale { get; set; }
	}
}
