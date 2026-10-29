using Lucene.Net.Util;

namespace Resgrid.Search
{
	/// <summary>Field names of the records index (RecordsSearchGeneration.SchemaVersion governs changes here).</summary>
	public static class RecordsIndexFields
	{
		public const LuceneVersion Version = LuceneVersion.LUCENE_48;
		public const string IndexName = "records";

		public const string Key = "Key";
		public const string DepartmentId = "DepartmentId";
		public const string SourceType = "SourceType";
		public const string SourceId = "SourceId";
		public const string RecordNumber = "RecordNumber";
		public const string RecordNumberExact = "RecordNumberExact";
		/// <summary>IdentifierSearchKeys of the record and call numbers, one term each: custom-format numbers by any part.</summary>
		public const string NumberKeys = "NumberKeys";
		public const string DraftReference = "DraftReference";
		public const string DefinitionKey = "DefinitionKey";
		public const string DefinitionVersion = "DefinitionVersion";
		public const string RecordType = "RecordType";
		public const string State = "State";
		public const string Year = "Year";
		public const string OccurredOn = "OccurredOn";
		public const string OccurredOnSort = "OccurredOnSort";
		public const string StationGroupId = "StationGroupId";
		public const string CallId = "CallId";
		public const string CallNumber = "CallNumber";
		public const string AuthorUserId = "AuthorUserId";
		public const string OwnerUserId = "OwnerUserId";
		public const string ReviewerUserId = "ReviewerUserId";
		public const string ParticipantUserIds = "ParticipantUserIds";
		public const string UnitIds = "UnitIds";
		public const string GroupScopeIds = "GroupScopeIds";
		public const string Summary = "Summary";
		public const string SearchText = "SearchText";
		public const string Narrative = "Narrative";
		public const string IsLegacy = "IsLegacy";
		public const string Generation = "Generation";
		/// <summary>
		/// Stored only, never queried, so it needs no schema bump: a document indexed before it existed reads as held at an
		/// unknown version and is re-indexed the first time the catch-up overlap window re-reads its row.
		/// </summary>
		public const string RowVersion = "RowVersion";

		public static string BuildKey(int departmentId, int sourceType, string sourceId)
		{
			return departmentId + "|" + sourceType + "|" + sourceId;
		}
	}
}
