using System;
using System.Collections.Generic;

namespace Resgrid.Model.Search
{
	/// <summary>Index names hosted by the shared Lucene host (Unified Search plan R1/R4).</summary>
	public static class SearchIndexNames
	{
		public const string Records = "records";
		public const string Global = "global";
	}

	/// <summary>Entity families the global index and the unified endpoint know about. Stable strings: clients switch on them.</summary>
	public static class SearchEntityTypes
	{
		public const string Call = "Call";
		public const string Unit = "Unit";
		public const string Personnel = "Personnel";
		public const string Contact = "Contact";
		public const string Message = "Message";
		public const string Document = "Document";
		public const string Note = "Note";
		public const string Record = "Record";
		public const string Action = "Action";

		public static readonly IReadOnlyList<string> Indexed = new[] { Call, Unit, Personnel, Contact, Message, Document, Note };
	}

	/// <summary>Index state values stored on SearchIndexState.State (same numbering as the RMS records index).</summary>
	public enum SearchIndexBuildState
	{
		Unknown = 0,
		Ready = 1,
		Rebuilding = 2,
		Failed = 3,
		RebuildRequested = 4
	}

	/// <summary>
	/// The generation key of the global index: (schemaVersion, protectedCatalogVersion, policyEpoch). Any change rebuilds
	/// the department's documents so an enrollment or a permission-policy change can never serve stale hits (plan R2.3, R7).
	/// </summary>
	public static class GlobalSearchGeneration
	{
		/// <summary>Bump when GlobalSearchDocumentBuilder or the projection allowlist changes.</summary>
		public const int SchemaVersion = 1;

		public static string Compute(int protectedCatalogVersion, long policyEpoch)
		{
			return $"{SchemaVersion}.{protectedCatalogVersion}.{policyEpoch}";
		}
	}

	/// <summary>The manifest a writer publishes to the object store after every commit and readers poll (plan R7).</summary>
	public class SearchIndexManifest
	{
		public string IndexName { get; set; }

		/// <summary>Opaque id of this publish; readers compare it to decide whether to sync.</summary>
		public string Revision { get; set; }

		/// <summary>Lucene segments_N generation of the published commit.</summary>
		public long SegmentsGeneration { get; set; }

		public int SchemaVersion { get; set; }

		public List<SearchIndexManifestFile> Files { get; set; } = new List<SearchIndexManifestFile>();

		public DateTime PublishedOnUtc { get; set; }

		public string PublishedBy { get; set; }

		/// <summary>Transport ETag returned by the store; used for the conditional PUT on the next publish. Not serialized.</summary>
		[Newtonsoft.Json.JsonIgnore]
		public string ETag { get; set; }
	}

	public class SearchIndexManifestFile
	{
		public string Name { get; set; }
		public long Length { get; set; }
	}

	/// <summary>A conditional manifest PUT lost: another writer published since this process last read the manifest.</summary>
	public class SearchIndexManifestConflictException : Exception
	{
		public SearchIndexManifestConflictException(string indexName, string message)
			: base(message)
		{
			IndexName = indexName;
		}

		public string IndexName { get; }
	}

	/// <summary>Outcome of one maintenance sweep of the global index (worker 70).</summary>
	public class SearchIndexSweepResult
	{
		public int DepartmentsChecked { get; set; }
		public int DepartmentsRebuilt { get; set; }
		public int ProjectionsRebuilt { get; set; }
		public int DocumentsIndexed { get; set; }
		public int DocumentsDeleted { get; set; }
		public int Errors { get; set; }
		public bool Skipped { get; set; }
		public string Message { get; set; }
	}

	public class SearchIndexHealth
	{
		public string IndexName { get; set; }
		public bool Enabled { get; set; }
		public bool Online { get; set; }
		public string IndexPath { get; set; }
		public int DocumentCount { get; set; }
		public bool StoreEnabled { get; set; }
		public string LastSyncedRevision { get; set; }
		public DateTime? LastSyncedOnUtc { get; set; }
		public string Error { get; set; }
	}
}
