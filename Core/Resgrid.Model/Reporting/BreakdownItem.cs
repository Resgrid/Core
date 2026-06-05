namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// One slice of a <see cref="Breakdown"/> (e.g. a call type, a priority, a canonical state).
	/// Breakdowns are capped to top-N with a synthetic "Other" bucket so payloads stay bounded.
	/// </summary>
	public class BreakdownItem
	{
		/// <summary>Resolved, human-readable label for the slice.</summary>
		public string Label { get; set; }

		/// <summary>The underlying id (type/priority/state id); null for the synthetic "Other" bucket.</summary>
		public int? Id { get; set; }

		public long Count { get; set; }

		/// <summary>True for the synthetic aggregate "Other" bucket.</summary>
		public bool IsOther { get; set; }

		/// <summary>
		/// Canonical availability of this slice; populated only for personnel/unit state breakdowns.
		/// </summary>
		public AvailabilityClass? Availability { get; set; }
	}
}
