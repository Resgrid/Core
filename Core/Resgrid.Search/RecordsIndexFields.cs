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

		public static string BuildKey(int departmentId, int sourceType, string sourceId)
		{
			return departmentId + "|" + sourceType + "|" + sourceId;
		}
	}
}
