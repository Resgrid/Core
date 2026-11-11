using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Where a background POI import is. Persisted: never renumber.</summary>
	public enum PoiImportStatus
	{
		/// <summary>Uploaded and waiting for the POI import worker (worker 79).</summary>
		Queued = 0,
		/// <summary>Geocoding the rows that carry an address but no position.</summary>
		Locating = 1,
		/// <summary>Saving POIs; each carries the import's id, so a resumed import skips the ones already saved.</summary>
		Saving = 2,
		Complete = 3,
		Failed = 4
	}

	/// <summary>
	/// A POI upload (KML, KMZ or CSV) the worker imports in the background (PoiImports, M0313). The upload request only
	/// reads the file; geocoding addresses and saving POIs can take minutes for a large spreadsheet, longer than a web
	/// request may run behind a proxy.
	/// </summary>
	public class PoiImport : IEntity
	{
		public string PoiImportId { get; set; }

		public int DepartmentId { get; set; }

		public int PoiTypeId { get; set; }

		/// <summary>The type's name when the file was uploaded, so the imports list never loads the type (and its POIs).</summary>
		public string PoiTypeName { get; set; }

		/// <summary>Who uploaded the file.</summary>
		public string UserId { get; set; }

		public string FileName { get; set; }

		/// <summary><see cref="PoiImportStatus"/>.</summary>
		public int Status { get; set; }

		/// <summary>Rows (placemarks or CSV rows) with a position or an address.</summary>
		public int TotalRows { get; set; }

		/// <summary>Rows with neither a position nor an address, left out when the file was read.</summary>
		public int SkippedCount { get; set; }

		public int ImportedCount { get; set; }

		/// <summary>Imported rows whose position came from geocoding their address.</summary>
		public int GeocodedCount { get; set; }

		/// <summary>Rows whose address a geocoder answered for and none could find (permanent); not imported.</summary>
		public int UnlocatedCount { get; set; }

		/// <summary>Rows whose address lookup no geocoder answered, even after the retries; not imported.</summary>
		public int LookupFailedCount { get; set; }

		/// <summary>The lookups failed because the install has no geocoder configured (no retries).</summary>
		public bool LookupNotConfigured { get; set; }

		/// <summary>Rows past the per-import lookup limit, never looked up; not imported.</summary>
		public int NotTriedCount { get; set; }

		public bool GeocodingLimitReached { get; set; }

		/// <summary>While locating: rows whose lookup no geocoder answered and that wait for <see cref="RetryAfter"/>.</summary>
		public int PendingRetryCount { get; set; }

		/// <summary>When a locating import waiting on unanswered lookups may run again; null when it is not waiting.</summary>
		public DateTime? RetryAfter { get; set; }

		/// <summary>The rows still to import, as JSON <see cref="PoiImportRow"/>s; cleared once the import completes.</summary>
		public string RowsJson { get; set; }

		/// <summary>The first unlocated rows (JSON <see cref="PoiImportRow"/>s), named in the import report.</summary>
		public string UnlocatedJson { get; set; }

		public string Error { get; set; }

		public DateTime AddedOn { get; set; }

		public DateTime? StartedOn { get; set; }

		/// <summary>Written while a worker runs the import; a stale heartbeat means the worker stopped and the import is resumed.</summary>
		public DateTime? HeartbeatOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public int Attempts { get; set; }

		[NotMapped]
		public bool IsFinished => Status == (int)PoiImportStatus.Complete || Status == (int)PoiImportStatus.Failed;

		[NotMapped]
		public string TableName => "PoiImports";

		[NotMapped]
		public string IdName => "PoiImportId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PoiImportId; }
			set { PoiImportId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsFinished" };
	}
}
