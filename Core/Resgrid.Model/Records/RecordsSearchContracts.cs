using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The RMS-owned Lucene index contract (RMS plan section 5.10). The index generation key is
	/// (schemaVersion, protectedCatalogVersion, policyEpoch): any change rebuilds the department's documents so
	/// an enrollment or permission change cannot serve stale hits.
	/// </summary>
	public static class RecordsSearchGeneration
	{
		/// <summary>Bump when the document schema in RecordsSearchDocumentBuilder changes.</summary>
		public const int SchemaVersion = 1;

		public static string Compute(int protectedCatalogVersion, long policyEpoch)
		{
			return RmsSearchIndexState.BuildGeneration(SchemaVersion, protectedCatalogVersion, policyEpoch);
		}
	}

	/// <summary>Index state values stored on RmsSearchIndexState.State.</summary>
	public enum RmsSearchIndexBuildState
	{
		Unknown = 0,
		Ready = 1,
		Rebuilding = 2,
		Failed = 3
	}

	/// <summary>One document to index: the safe projection plus, only when the department may index it, the narrative.</summary>
	public class RecordsSearchDocumentSource
	{
		public RmsRecordSearchProjection Projection { get; set; }

		/// <summary>Null unless narrative indexing is permitted (unprotected department with IndexNarrative on).</summary>
		public string Narrative { get; set; }

		public string Generation { get; set; }
	}

	public class RecordsSearchRequest
	{
		public string Text { get; set; }

		/// <summary>Null means unrestricted; otherwise the viewer's visible group ids plus the always-visible cases.</summary>
		public List<int> VisibleGroupIds { get; set; }
		public string ViewerUserId { get; set; }
		public List<int> States { get; set; }
		public string DefinitionKey { get; set; }
		public int? Year { get; set; }
		public int? CallId { get; set; }
		public bool IncludeLegacy { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
	}

	public class RecordsSearchHit
	{
		public string SourceType { get; set; }
		public string SourceId { get; set; }
		public string RecordNumber { get; set; }
		public float Score { get; set; }
	}

	public class RecordsSearchResult
	{
		public List<RecordsSearchHit> Hits { get; set; } = new List<RecordsSearchHit>();

		/// <summary>Index-side total before post-retrieval authorization; callers suppress it when a hit is dropped.</summary>
		public int Total { get; set; }
		public bool Truncated { get; set; }
		public bool Available { get; set; } = true;
	}

	public class RecordsSearchHealth
	{
		public bool Enabled { get; set; }
		public bool Online { get; set; }
		public string IndexPath { get; set; }
		public int DocumentCount { get; set; }
		public string Error { get; set; }
	}

	public class RecordsSearchIndexSweepResult
	{
		public int DepartmentsChecked { get; set; }
		public int DepartmentsRebuilt { get; set; }
		public int DocumentsIndexed { get; set; }
		public int DocumentsDeleted { get; set; }
		public int Errors { get; set; }
		public bool Skipped { get; set; }
		public string Message { get; set; }
	}
}
