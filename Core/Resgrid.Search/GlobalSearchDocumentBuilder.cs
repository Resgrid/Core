using System;
using System.Collections.Generic;
using Lucene.Net.Documents;
using Resgrid.Model.Search;
using Document = Lucene.Net.Documents.Document;

namespace Resgrid.Search
{
	/// <summary>
	/// SearchProjection to Lucene Document. The projection is already the safe field set (plan R2.3, R2.15); nothing is
	/// read from the source entity here, so the index can never hold more than the projection table does.
	/// </summary>
	public static class GlobalSearchDocumentBuilder
	{
		public static Document Build(SearchProjection p, string generation)
		{
			if (p == null)
				throw new ArgumentNullException(nameof(p));

			var doc = new Document
			{
				new StringField(GlobalIndexFields.Key, SearchProjection.BuildKey(p.DepartmentId, p.EntityType, p.EntityId), Field.Store.YES),
				new StringField(GlobalIndexFields.ProjectionId, p.SearchProjectionId ?? string.Empty, Field.Store.YES),
				new StringField(GlobalIndexFields.DepartmentId, p.DepartmentId.ToString(), Field.Store.YES),
				new StringField(GlobalIndexFields.EntityType, p.EntityType ?? string.Empty, Field.Store.YES),
				new StringField(GlobalIndexFields.EntityId, p.EntityId ?? string.Empty, Field.Store.YES),
				new StringField(GlobalIndexFields.IsAdminOnly, p.IsAdminOnly ? "1" : "0", Field.Store.NO),
				new StringField(GlobalIndexFields.IsActive, p.IsActive ? "1" : "0", Field.Store.NO),
				new StringField(GlobalIndexFields.Generation, generation ?? string.Empty, Field.Store.YES),
				new StoredField(GlobalIndexFields.RowVersion, p.RowVersion),
				new Int64Field(GlobalIndexFields.OccurredOn, p.OccurredOn.Ticks, Field.Store.YES),
				new NumericDocValuesField(GlobalIndexFields.OccurredOnSort, p.OccurredOn.Ticks)
			};

			if (!string.IsNullOrWhiteSpace(p.Title))
			{
				doc.Add(new TextField(GlobalIndexFields.Title, p.Title, Field.Store.YES));
				doc.Add(new TextField(GlobalIndexFields.TitlePrefix, p.Title, Field.Store.NO));
			}
			if (!string.IsNullOrWhiteSpace(p.Summary))
				doc.Add(new TextField(GlobalIndexFields.Summary, p.Summary, Field.Store.YES));
			if (!string.IsNullOrWhiteSpace(p.SearchText))
				doc.Add(new TextField(GlobalIndexFields.SearchText, p.SearchText, Field.Store.NO));
			if (!string.IsNullOrWhiteSpace(p.Keywords))
			{
				doc.Add(new TextField(GlobalIndexFields.Keywords, p.Keywords, Field.Store.NO));
				doc.Add(new TextField(GlobalIndexFields.KeywordsPrefix, p.Keywords, Field.Store.NO));
				foreach (var keyword in SplitKeywords(p.Keywords))
					doc.Add(new StringField(GlobalIndexFields.KeywordExact, keyword, Field.Store.NO));
			}
			if (!string.IsNullOrWhiteSpace(p.Category))
				doc.Add(new StringField(GlobalIndexFields.Category, p.Category, Field.Store.YES));
			if (!string.IsNullOrWhiteSpace(p.Status))
				doc.Add(new StringField(GlobalIndexFields.Status, p.Status, Field.Store.YES));
			if (p.Priority.HasValue)
				doc.Add(new StringField(GlobalIndexFields.Priority, p.Priority.Value.ToString(), Field.Store.YES));
			if (p.GroupId.HasValue)
				doc.Add(new StringField(GlobalIndexFields.GroupId, p.GroupId.Value.ToString(), Field.Store.YES));
			if (!string.IsNullOrWhiteSpace(p.OwnerUserId))
				doc.Add(new StringField(GlobalIndexFields.OwnerUserId, p.OwnerUserId, Field.Store.NO));
			foreach (var id in SplitCsv(p.ParticipantUserIds))
				doc.Add(new StringField(GlobalIndexFields.ParticipantUserIds, id, Field.Store.NO));
			if (!string.IsNullOrWhiteSpace(p.Url))
				doc.Add(new StoredField(GlobalIndexFields.Url, p.Url));
			if (!string.IsNullOrWhiteSpace(p.MetadataJson))
				doc.Add(new StoredField(GlobalIndexFields.MetadataJson, p.MetadataJson));

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

		/// <summary>Keywords are separated by whitespace or commas; each becomes one lower-cased exact term.</summary>
		public static IEnumerable<string> SplitKeywords(string keywords)
		{
			if (string.IsNullOrWhiteSpace(keywords))
				yield break;

			foreach (var part in keywords.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var trimmed = part.Trim().ToLowerInvariant();
				if (trimmed.Length > 0)
					yield return trimmed;
			}
		}
	}
}
