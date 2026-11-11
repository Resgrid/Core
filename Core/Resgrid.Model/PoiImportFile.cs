using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>One POI read from an import file: a position from the file, or an address to locate.</summary>
	public class PoiImportRow
	{
		public string Name { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		/// <summary>The address the file gave ("street, city, state, postal code"); geocoded when the row has no position.</summary>
		public string Address { get; set; }

		public string Note { get; set; }

		/// <summary>Address lookup state (<see cref="PoiImportRowLookup"/>) of a row the file gave no position.</summary>
		public int Lookup { get; set; }

		/// <summary>Lookups tried for this row's address.</summary>
		public int LookupAttempts { get; set; }

		[JsonIgnore]
		public bool HasPosition => Latitude.HasValue && Longitude.HasValue;
	}

	/// <summary>Where a POI import row's address lookup stands. Stored in PoiImports.RowsJson: never renumber.</summary>
	public enum PoiImportRowLookup
	{
		/// <summary>Not looked up (yet), or the file gave a position.</summary>
		None = 0,
		Found = 1,
		/// <summary>A geocoder answered and none found the address: reported, never retried.</summary>
		NotFound = 2,
		/// <summary>No geocoder answered: retried after a back-off, reported once the retries run out.</summary>
		Unavailable = 3,
		/// <summary>No geocoder is configured: reported, never retried.</summary>
		NotConfigured = 4,
		/// <summary>Past the per-import lookup limit: reported, never looked up.</summary>
		OverLimit = 5
	}

	/// <summary>The POIs read from an upload (KML, KMZ or CSV), before any address is geocoded.</summary>
	public class PoiImportFile
	{
		/// <summary>Rows with a position, or with an address the import will geocode, in file order.</summary>
		public List<PoiImportRow> Rows { get; set; } = new List<PoiImportRow>();

		/// <summary>Rows or placemarks with neither a usable position nor an address.</summary>
		public int SkippedCount { get; set; }

		/// <summary>Set when the file could not be read at all: a message the uploader can act on.</summary>
		public string Error { get; set; }
	}
}
