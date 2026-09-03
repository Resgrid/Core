using System;
using System.Collections.Generic;
using Lucene.Net.Documents;
using Resgrid.Model;
using Document = Lucene.Net.Documents.Document;

namespace Resgrid.Search
{
	/// <summary>
	/// RmsRecordSearchProjection to Lucene Document (RMS plan section 5.10 index contract). The projection is
	/// already the safe field set; narrative is added only when the caller was allowed to supply it. Nothing from
	/// the restricted sections, attachments, submission payloads or protected-candidate fields ever lands here.
	/// </summary>
	public static class RecordsSearchDocumentBuilder
	{
		public static Document Build(RecordsSearchDocumentSource source)
		{
			if (source?.Projection == null)
				throw new ArgumentNullException(nameof(source));

			var p = source.Projection;
			var doc = new Document
			{
				new StringField(RecordsIndexFields.Key, RecordsIndexFields.BuildKey(p.DepartmentId, p.SourceType, p.SourceId), Field.Store.YES),
				new StringField(RecordsIndexFields.DepartmentId, p.DepartmentId.ToString(), Field.Store.YES),
				new StringField(RecordsIndexFields.SourceType, p.SourceType.ToString(), Field.Store.YES),
				new StringField(RecordsIndexFields.SourceId, p.SourceId ?? string.Empty, Field.Store.YES),
				new StringField(RecordsIndexFields.DefinitionKey, p.DefinitionKey ?? string.Empty, Field.Store.YES),
				new StringField(RecordsIndexFields.DefinitionVersion, p.DefinitionVersion.ToString(), Field.Store.YES),
				new StringField(RecordsIndexFields.RecordType, p.RecordType.HasValue ? p.RecordType.Value.ToString() : string.Empty, Field.Store.YES),
				new StringField(RecordsIndexFields.State, p.State.ToString(), Field.Store.YES),
				new StringField(RecordsIndexFields.IsLegacy, p.IsLegacy ? "1" : "0", Field.Store.YES),
				new StringField(RecordsIndexFields.Generation, source.Generation ?? string.Empty, Field.Store.YES),
				new StringField(RecordsIndexFields.AuthorUserId, p.AuthorUserId ?? string.Empty, Field.Store.YES),
				new StringField(RecordsIndexFields.OwnerUserId, p.OwnerUserId ?? string.Empty, Field.Store.YES),
				new StringField(RecordsIndexFields.ReviewerUserId, p.ReviewerUserId ?? string.Empty, Field.Store.YES)
			};

			var occurred = p.OccurredOn ?? p.RecordCreatedOn;
			doc.Add(new StringField(RecordsIndexFields.Year, occurred.Year.ToString(), Field.Store.YES));
			doc.Add(new Int64Field(RecordsIndexFields.OccurredOn, occurred.Ticks, Field.Store.YES));
			doc.Add(new NumericDocValuesField(RecordsIndexFields.OccurredOnSort, occurred.Ticks));

			if (!string.IsNullOrWhiteSpace(p.RecordNumber))
			{
				doc.Add(new TextField(RecordsIndexFields.RecordNumber, p.RecordNumber, Field.Store.YES));
				doc.Add(new StringField(RecordsIndexFields.RecordNumberExact, p.RecordNumber.Trim().ToLowerInvariant(), Field.Store.NO));
			}
			if (!string.IsNullOrWhiteSpace(p.DraftReference))
				doc.Add(new TextField(RecordsIndexFields.DraftReference, p.DraftReference, Field.Store.YES));
			if (p.StationGroupId.HasValue)
				doc.Add(new StringField(RecordsIndexFields.StationGroupId, p.StationGroupId.Value.ToString(), Field.Store.YES));
			if (p.CallId.HasValue)
				doc.Add(new StringField(RecordsIndexFields.CallId, p.CallId.Value.ToString(), Field.Store.YES));
			if (!string.IsNullOrWhiteSpace(p.CallNumber))
				doc.Add(new TextField(RecordsIndexFields.CallNumber, p.CallNumber, Field.Store.YES));
			if (!string.IsNullOrWhiteSpace(p.DisplaySummary))
				doc.Add(new TextField(RecordsIndexFields.Summary, p.DisplaySummary, Field.Store.YES));
			if (!string.IsNullOrWhiteSpace(p.SearchText))
				doc.Add(new TextField(RecordsIndexFields.SearchText, p.SearchText, Field.Store.NO));
			if (!string.IsNullOrWhiteSpace(source.Narrative))
				doc.Add(new TextField(RecordsIndexFields.Narrative, source.Narrative, Field.Store.NO));

			foreach (var id in SplitCsv(p.ParticipantUserIds))
				doc.Add(new StringField(RecordsIndexFields.ParticipantUserIds, id, Field.Store.NO));
			foreach (var id in SplitCsv(p.UnitIds))
				doc.Add(new StringField(RecordsIndexFields.UnitIds, id, Field.Store.NO));
			foreach (var id in SplitCsv(p.GroupScopeIds))
				doc.Add(new StringField(RecordsIndexFields.GroupScopeIds, id, Field.Store.NO));

			return doc;
		}

		public static IEnumerable<string> SplitCsv(string csv)
		{
			if (string.IsNullOrWhiteSpace(csv))
				yield break;

			foreach (var part in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var trimmed = part.Trim();
				if (trimmed.Length > 0)
					yield return trimmed;
			}
		}
	}
}
