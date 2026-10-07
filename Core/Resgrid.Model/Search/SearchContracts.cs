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
		// Workforce & Business Operations plan (decision 41): allowlisted identifier / title / status rows only. Rate schedules,
		// DTRs, expenses, compliance documents, certification records, Cal OES MARS and every Phase E table are never projected.
		public const string Invoice = "Invoice";
		public const string RateCard = "RateCard";
		public const string Bid = "Bid";
		public const string ServiceContract = "ServiceContract";
		public const string Deployment = "Deployment";
		public const string CertificationType = "CertificationType";
		// Operations reference families (plan R3 Tier 2): names, codes and descriptions; legacy logs carry their narrative
		// under the same protection rule as call notes.
		public const string Protocol = "Protocol";
		public const string Training = "Training";
		public const string CalendarEvent = "CalendarEvent";
		public const string Log = "Log";
		public const string Poi = "Poi";
		public const string Shift = "Shift";
		public const string Group = "Group";
		// RMS occupancy master (pre-plans): name, number, address and the alarm company, under the Records gates.
		public const string Occupancy = "Occupancy";

		public static readonly IReadOnlyList<string> Indexed = new[]
		{
			Call, Unit, Personnel, Contact, Message, Document, Note, Invoice, RateCard, Bid, ServiceContract, Deployment, CertificationType,
			Protocol, Training, CalendarEvent, Log, Poi, Shift, Group, Occupancy
		};
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
		/// <summary>
		/// Bump when GlobalSearchDocumentBuilder or the projection allowlist changes. 3: call notes in the call full text.
		/// A bump blanks every department's results until its own rebuild runs (hits must carry the current generation), so
		/// changes that only add rows go through a backfill instead (the M0261 call history backfill).
		/// </summary>
		public const int SchemaVersion = 3;

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

	/// <summary>
	/// A file named by a manifest is not in the object store. Normally the writer pruned it after publishing a newer
	/// manifest while a reader was still pulling the older one; the reader re-reads the manifest and starts over.
	/// </summary>
	public class SearchIndexObjectNotFoundException : Exception
	{
		public SearchIndexObjectNotFoundException(string indexName, string fileName, string message, Exception innerException = null)
			: base(message, innerException)
		{
			IndexName = indexName;
			FileName = fileName;
		}

		public string IndexName { get; }

		public string FileName { get; }
	}

	/// <summary>Outcome of one maintenance sweep of the global index (worker 70).</summary>
	public class SearchIndexSweepResult
	{
		public int DepartmentsChecked { get; set; }
		public int DepartmentsRebuilt { get; set; }
		public int ProjectionsRebuilt { get; set; }
		public int DocumentsIndexed { get; set; }
		public int DocumentsDeleted { get; set; }
		/// <summary>Calls the call history backfill projected this sweep (they reach the index on the next sweep's catch-up).</summary>
		public int CallsBackfilled { get; set; }
		/// <summary>Departments whose call history backfill reached the oldest call this sweep.</summary>
		public int CallBackfillsCompleted { get; set; }
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
