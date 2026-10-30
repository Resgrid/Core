using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>What a KML or KMZ upload yielded.</summary>
	public class KmlImportResult
	{
		/// <summary>Placemarks with a usable position.</summary>
		public List<Coordinates> Coordinates { get; set; } = new List<Coordinates>();

		/// <summary>Placemarks passed over because they have no position, or one outside the valid latitude/longitude range.</summary>
		public int SkippedCount { get; set; }

		/// <summary>Set when the file could not be read at all: a message the uploader can act on.</summary>
		public string Error { get; set; }
	}
}
