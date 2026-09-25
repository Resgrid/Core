using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Search
{
	/// <summary>CPU BM25 over the immutable release-pinned public reference corpus only. Never queries tenant records.</summary>
	public sealed class AdminAssistReferenceSearch : IAdminAssistReferenceSearch, IDisposable
	{
		private readonly Lazy<ReferenceIndex> _index;
		public AdminAssistReferenceSearch(IAdminAssistCatalog catalog) => _index = new Lazy<ReferenceIndex>(() => new ReferenceIndex(catalog));
		public IReadOnlyList<AdminAssistSearchHit> Search(string query, string locale, int take = 20)
		{
			if (string.IsNullOrWhiteSpace(query)) return Array.Empty<AdminAssistSearchHit>();
			if (query.Length > 256) throw new ArgumentException("Reference query too long.");
			return _index.Value.Search(query, locale, Math.Clamp(take, 1, 25));
		}
		public void Dispose() { if (_index.IsValueCreated) _index.Value.Dispose(); }
		private sealed class ReferenceIndex : IDisposable
		{
			private const LuceneVersion Version = LuceneVersion.LUCENE_48;
			private readonly RAMDirectory _directory = new();
			private readonly StandardAnalyzer _analyzer = new(Version);
			private readonly DirectoryReader _reader;
			private readonly IReadOnlyDictionary<string, KnowledgeArticle> _articles;
			public ReferenceIndex(IAdminAssistCatalog catalog)
			{
				_articles = catalog.Articles.ToDictionary(a => a.Locale + ":" + a.Id, StringComparer.Ordinal);
				using (var writer = new IndexWriter(_directory, new IndexWriterConfig(Version, _analyzer) { Similarity = new BM25Similarity() }))
				{
					foreach (var (id, article) in _articles)
					{
						if (article.PackVersion != catalog.Version || string.IsNullOrWhiteSpace(article.Anchor) || !article.SourcePath.StartsWith("docs/admin-assist/", StringComparison.Ordinal))
							throw new InvalidOperationException("Unreviewed reference source.");
						writer.AddDocument(new Document { new StringField("id", id, Field.Store.YES), new StringField("locale", article.Locale, Field.Store.NO),
							new TextField("body", article.Body, Field.Store.NO) });
					}
					writer.Commit();
				}
				_reader = DirectoryReader.Open(_directory);
			}
			public IReadOnlyList<AdminAssistSearchHit> Search(string query, string locale, int take)
			{
				var selectedLocale = (locale ?? "en").Split('-')[0].ToLowerInvariant();
				if (!_articles.Values.Any(a => a.Locale == selectedLocale)) selectedLocale = "en";
				// Analyze literal text rather than exposing Lucene query syntax. Escaping punctuation alone
				// still interprets words such as AND/OR/NOT as operators and can throw on normal questions.
				var words = new BooleanQuery();
				using (var tokens = _analyzer.GetTokenStream("body", new StringReader(query)))
				{
					var term = tokens.AddAttribute<ICharTermAttribute>(); tokens.Reset();
					while (tokens.IncrementToken()) words.Add(new TermQuery(new Term("body", term.ToString())), Occur.SHOULD);
					tokens.End();
				}
				if (words.Clauses.Count == 0) return Array.Empty<AdminAssistSearchHit>();
				var filter = new BooleanQuery { { words, Occur.MUST }, { new TermQuery(new Term("locale", selectedLocale)), Occur.MUST } };
				var searcher = new IndexSearcher(_reader) { Similarity = new BM25Similarity() };
				return searcher.Search(filter, take).ScoreDocs.Select(hit => _articles[searcher.Doc(hit.Doc).Get("id")]).Select(a =>
					new AdminAssistSearchHit(a.Id, a.TitleKey, a.Body.Length > 500 ? a.Body[..500] + "…" : a.Body, a.SourcePath, a.Anchor, a.PackVersion, a.Locale)).ToArray();
			}
			public void Dispose() { _reader.Dispose(); _analyzer.Dispose(); _directory.Dispose(); }
		}
	}
}
