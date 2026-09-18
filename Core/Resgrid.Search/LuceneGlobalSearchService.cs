using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Search
{
	/// <summary>
	/// Query side of the global index. The department clause is always injected; message documents are visible only
	/// to their sender or a recipient, and admin-only documents/notes only to department admins — both resolve inside
	/// the query so counts cannot disclose rows the viewer cannot open. User text is escaped, so wildcards, ranges and
	/// field selectors from request input never reach the parser (plan R2.4). Every hit is still re-checked by the
	/// caller (UnifiedSearchService) before it is shown.
	/// </summary>
	public class LuceneGlobalSearchService : IGlobalSearchService
	{
		private static readonly string[] TextFields =
		{
			GlobalIndexFields.Title, GlobalIndexFields.Keywords, GlobalIndexFields.Summary, GlobalIndexFields.SearchText
		};

		private static readonly IDictionary<string, float> TextBoosts = new Dictionary<string, float>
		{
			[GlobalIndexFields.Title] = 5f,
			[GlobalIndexFields.Keywords] = 4f,
			[GlobalIndexFields.Summary] = 2f,
			[GlobalIndexFields.SearchText] = 1f
		};

		private readonly LuceneGlobalIndexHost _host;

		public LuceneGlobalSearchService(LuceneGlobalIndexHost host)
		{
			_host = host ?? throw new ArgumentNullException(nameof(host));
		}

		public bool IsAvailable => _host.Enabled && _host.IndexExists;

		public Task<GlobalSearchResult> SearchAsync(int departmentId, GlobalSearchQuery query, CancellationToken cancellationToken = default)
		{
			query = query ?? new GlobalSearchQuery();
			var result = new GlobalSearchResult();

			if (!_host.Enabled || departmentId <= 0)
			{
				result.Available = false;
				return Task.FromResult(result);
			}

			SearcherManager manager;
			try
			{
				manager = _host.GetSearcherManager();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Global search reader could not be opened.");
				result.Available = false;
				return Task.FromResult(result);
			}

			if (manager == null)
			{
				result.Available = false;
				return Task.FromResult(result);
			}

			cancellationToken.ThrowIfCancellationRequested();
			_host.MaybeRefresh();

			var lucene = BuildQuery(departmentId, query);
			var take = query.Take <= 0 ? 50 : Math.Min(query.Take, Math.Max(1, SearchConfig.MaxResults));
			var skip = Math.Max(0, query.Skip);
			var window = Math.Min(skip + take, Math.Max(1, SearchConfig.MaxResults));

			var searcher = manager.Acquire();
			try
			{
				var hasText = !string.IsNullOrWhiteSpace(query.Text);
				var topDocs = hasText
					? searcher.Search(lucene, window)
					: searcher.Search(lucene, window, new Sort(new SortField(GlobalIndexFields.OccurredOnSort, SortFieldType.INT64, true)));

				result.Total = topDocs.TotalHits;
				result.Truncated = topDocs.TotalHits > SearchConfig.MaxResults;

				foreach (var scoreDoc in topDocs.ScoreDocs.Skip(skip).Take(take))
				{
					var doc = searcher.Doc(scoreDoc.Doc);
					result.Hits.Add(new GlobalSearchHit
					{
						ProjectionId = doc.Get(GlobalIndexFields.ProjectionId),
						EntityType = doc.Get(GlobalIndexFields.EntityType),
						EntityId = doc.Get(GlobalIndexFields.EntityId),
						Title = doc.Get(GlobalIndexFields.Title),
						Summary = doc.Get(GlobalIndexFields.Summary),
						Url = doc.Get(GlobalIndexFields.Url),
						Category = doc.Get(GlobalIndexFields.Category),
						Status = doc.Get(GlobalIndexFields.Status),
						MetadataJson = doc.Get(GlobalIndexFields.MetadataJson),
						OccurredOnTicks = long.TryParse(doc.Get(GlobalIndexFields.OccurredOn), out var ticks) ? ticks : 0,
						Score = float.IsNaN(scoreDoc.Score) ? 0f : scoreDoc.Score
					});
				}
			}
			finally
			{
				manager.Release(searcher);
			}

			return Task.FromResult(result);
		}

		public Task<SearchIndexHealth> GetHealthAsync()
		{
			var health = new SearchIndexHealth
			{
				IndexName = _host.IndexName,
				Enabled = _host.Enabled,
				IndexPath = _host.IndexPath,
				StoreEnabled = _host.StoreEnabled,
				LastSyncedRevision = _host.LastSyncedRevision,
				LastSyncedOnUtc = _host.LastSyncedOnUtc
			};
			if (!_host.Enabled)
				return Task.FromResult(health);

			try
			{
				var manager = _host.GetSearcherManager();
				if (manager == null)
				{
					health.Online = false;
					health.Error = "Index not created yet.";
					return Task.FromResult(health);
				}

				_host.MaybeRefresh();
				var searcher = manager.Acquire();
				try
				{
					health.Online = true;
					health.DocumentCount = searcher.IndexReader.NumDocs;
				}
				finally
				{
					manager.Release(searcher);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Global search health check failed.");
				health.Online = false;
				health.Error = ex.Message;
			}

			return Task.FromResult(health);
		}

		/// <summary>Visible for tests: the exact query the service runs.</summary>
		public static Query BuildQuery(int departmentId, GlobalSearchQuery request)
		{
			var query = new BooleanQuery
			{
				{ new TermQuery(new Term(GlobalIndexFields.DepartmentId, departmentId.ToString())), Occur.MUST }
			};

			if (request.EntityTypes != null && request.EntityTypes.Count > 0)
			{
				var types = new BooleanQuery();
				foreach (var type in request.EntityTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
					types.Add(new TermQuery(new Term(GlobalIndexFields.EntityType, type.Trim())), Occur.SHOULD);
				if (types.Clauses.Count > 0)
					query.Add(types, Occur.MUST);
			}

			// Messages: only the sender or a recipient may see them. (+Message +(owner OR participant)) OR (NOT Message).
			var viewer = request.ViewerUserId ?? string.Empty;
			var messageScope = new BooleanQuery();
			var messageForViewer = new BooleanQuery { { new TermQuery(new Term(GlobalIndexFields.EntityType, SearchEntityTypes.Message)), Occur.MUST } };
			var viewerMatch = new BooleanQuery();
			if (viewer.Length > 0)
			{
				viewerMatch.Add(new TermQuery(new Term(GlobalIndexFields.OwnerUserId, viewer)), Occur.SHOULD);
				viewerMatch.Add(new TermQuery(new Term(GlobalIndexFields.ParticipantUserIds, viewer)), Occur.SHOULD);
			}
			else
			{
				viewerMatch.Add(new TermQuery(new Term(GlobalIndexFields.Key, " none")), Occur.SHOULD);
			}
			messageForViewer.Add(viewerMatch, Occur.MUST);
			messageScope.Add(messageForViewer, Occur.SHOULD);
			messageScope.Add(new BooleanQuery
			{
				{ new MatchAllDocsQuery(), Occur.MUST },
				{ new TermQuery(new Term(GlobalIndexFields.EntityType, SearchEntityTypes.Message)), Occur.MUST_NOT }
			}, Occur.SHOULD);
			query.Add(messageScope, Occur.MUST);

			if (!request.IncludeAdminOnly)
				query.Add(new TermQuery(new Term(GlobalIndexFields.IsAdminOnly, "1")), Occur.MUST_NOT);

			if (!string.IsNullOrWhiteSpace(request.Text))
			{
				var text = request.Text.Trim();
				var tokens = Tokenize(text);
				var textQuery = new BooleanQuery();

				if (request.Prefix)
				{
					// Typeahead: every token must prefix-match a title or keyword token.
					var prefix = new BooleanQuery();
					foreach (var token in tokens)
					{
						var either = new BooleanQuery
						{
							{ new TermQuery(new Term(GlobalIndexFields.TitlePrefix, token)) { Boost = 3f }, Occur.SHOULD },
							{ new TermQuery(new Term(GlobalIndexFields.KeywordsPrefix, token)) { Boost = 4f }, Occur.SHOULD }
						};
						prefix.Add(either, Occur.MUST);
					}
					if (prefix.Clauses.Count > 0)
						textQuery.Add(prefix, Occur.SHOULD);
				}
				else
				{
					using var analyzer = GlobalIndexFields.CreateQueryAnalyzer();
					var parser = new MultiFieldQueryParser(GlobalIndexFields.Version, TextFields, analyzer, TextBoosts)
					{
						DefaultOperator = Operator.AND,
						AllowLeadingWildcard = false
					};
					Query parsed;
					try
					{
						parsed = parser.Parse(QueryParserBase.Escape(text));
					}
					catch (ParseException)
					{
						parsed = null;
					}
					if (parsed != null)
						textQuery.Add(parsed, Occur.SHOULD);

					// A final partial token still prefix-matches (the user is mid-word).
					if (tokens.Count > 0)
					{
						var last = tokens[tokens.Count - 1];
						var lastPrefix = new BooleanQuery();
						foreach (var token in tokens.Take(tokens.Count - 1))
						{
							lastPrefix.Add(new BooleanQuery
							{
								{ new TermQuery(new Term(GlobalIndexFields.TitlePrefix, token)), Occur.SHOULD },
								{ new TermQuery(new Term(GlobalIndexFields.KeywordsPrefix, token)), Occur.SHOULD }
							}, Occur.MUST);
						}
						lastPrefix.Add(new BooleanQuery
						{
							{ new TermQuery(new Term(GlobalIndexFields.TitlePrefix, last)) { Boost = 2f }, Occur.SHOULD },
							{ new TermQuery(new Term(GlobalIndexFields.KeywordsPrefix, last)) { Boost = 3f }, Occur.SHOULD }
						}, Occur.MUST);
						textQuery.Add(lastPrefix, Occur.SHOULD);
					}
				}

				textQuery.Add(new TermQuery(new Term(GlobalIndexFields.KeywordExact, text.ToLowerInvariant())) { Boost = 6f }, Occur.SHOULD);
				query.Add(textQuery, Occur.MUST);
			}

			return query;
		}

		/// <summary>Standard-analyzer tokens of the user text (lower-cased, stop words removed), never more than 12.</summary>
		public static List<string> Tokenize(string text)
		{
			var tokens = new List<string>();
			if (string.IsNullOrWhiteSpace(text))
				return tokens;

			using var analyzer = GlobalIndexFields.CreateQueryAnalyzer();
			using var stream = analyzer.GetTokenStream(GlobalIndexFields.Title, new StringReader(text));
			var term = stream.AddAttribute<ICharTermAttribute>();
			stream.Reset();
			while (stream.IncrementToken() && tokens.Count < 12)
			{
				var value = term.ToString();
				if (value.Length > 0)
					tokens.Add(value);
			}
			stream.End();
			return tokens;
		}
	}
}
