using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
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
			// Skip is clamped to the candidate ceiling before the addition: an unbounded offset would overflow the window
			// negative and IndexSearcher.Search rejects a non-positive hit count instead of returning an empty page.
			var max = MaxWindow(query);
			var take = query.Take <= 0 ? 50 : Math.Min(query.Take, max);
			var skip = Math.Min(Math.Max(0, query.Skip), max);
			var window = Math.Min(skip + take, max);

			var searcher = manager.Acquire();
			try
			{
				var hasText = !string.IsNullOrWhiteSpace(query.Text);
				var sort = SearchSortOrders.Normalize(query.Sort);
				var topDocs = hasText && sort == SearchSortOrders.Relevance
					? searcher.Search(lucene, window)
					: searcher.Search(lucene, window, new Sort(new SortField(GlobalIndexFields.OccurredOnSort, SortFieldType.INT64, sort != SearchSortOrders.Oldest)));

				result.Total = topDocs.TotalHits;
				result.Truncated = topDocs.TotalHits > max;

				foreach (var scoreDoc in topDocs.ScoreDocs.Skip(skip).Take(take))
				{
					var doc = searcher.Doc(scoreDoc.Doc);
					result.Hits.Add(new GlobalSearchHit
					{
						DepartmentId = int.TryParse(doc.Get(GlobalIndexFields.DepartmentId), out var department) ? department : 0,
						Generation = doc.Get(GlobalIndexFields.Generation),
						RowVersion = long.TryParse(doc.Get(GlobalIndexFields.RowVersion), out var version) ? version : 0,
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

		/// <summary>The deepest hit a query may reach: SearchConfig.MaxResults, or the caller's larger window up to SearchConfig.MaxPageWindow.</summary>
		public static int MaxWindow(GlobalSearchQuery query)
		{
			var max = Math.Max(1, SearchConfig.MaxResults);
			if (query != null && query.MaxWindow > max)
				max = Math.Max(max, Math.Min(query.MaxWindow, Math.Max(1, SearchConfig.MaxPageWindow)));
			return max;
		}

		/// <summary>Visible for tests: the exact query the service runs.</summary>
		public static Query BuildQuery(int departmentId, GlobalSearchQuery request)
		{
			var query = new BooleanQuery
			{
				{ new TermQuery(new Term(GlobalIndexFields.DepartmentId, departmentId.ToString())), Occur.MUST }
			};

			if (!string.IsNullOrWhiteSpace(request.Generation))
				query.Add(new TermQuery(new Term(GlobalIndexFields.Generation, request.Generation)), Occur.MUST);

			if (request.EntityTypes != null && request.EntityTypes.Count > 0)
			{
				var types = new BooleanQuery();
				foreach (var type in request.EntityTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
					types.Add(new TermQuery(new Term(GlobalIndexFields.EntityType, type.Trim())), Occur.SHOULD);
				if (types.Clauses.Count > 0)
					query.Add(types, Occur.MUST);
			}

			// Viewer-scoped families: only the owner or a participant may see them. Messages always (sender or
			// recipient); the orchestrator adds the families that are membership-scoped for this caller, e.g.
			// deployments for a member without the claim (rostered user ids are the participants).
			// (+scopedType +(owner OR participant)) OR (NOT scopedType).
			var scopedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SearchEntityTypes.Message };
			foreach (var type in request.ViewerScopedEntityTypes ?? Enumerable.Empty<string>())
				if (!string.IsNullOrWhiteSpace(type)) scopedTypes.Add(type.Trim());
			BooleanQuery ScopedTypes()
			{
				var types = new BooleanQuery();
				foreach (var type in scopedTypes) types.Add(new TermQuery(new Term(GlobalIndexFields.EntityType, type)), Occur.SHOULD);
				return types;
			}
			var viewer = request.ViewerUserId ?? string.Empty;
			var viewerScope = new BooleanQuery();
			var scopedForViewer = new BooleanQuery { { ScopedTypes(), Occur.MUST } };
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
			scopedForViewer.Add(viewerMatch, Occur.MUST);
			viewerScope.Add(scopedForViewer, Occur.SHOULD);
			viewerScope.Add(new BooleanQuery
			{
				{ new MatchAllDocsQuery(), Occur.MUST },
				{ ScopedTypes(), Occur.MUST_NOT }
			}, Occur.SHOULD);
			query.Add(viewerScope, Occur.MUST);

			if (!request.IncludeAdminOnly)
				query.Add(new TermQuery(new Term(GlobalIndexFields.IsAdminOnly, "1")), Occur.MUST_NOT);

			if (request.FromUtc.HasValue || request.ToUtc.HasValue)
			{
				long? from = request.FromUtc.HasValue ? request.FromUtc.Value.Ticks : (long?)null;
				long? to = request.ToUtc.HasValue ? request.ToUtc.Value.Ticks : (long?)null;
				query.Add(NumericRangeQuery.NewInt64Range(GlobalIndexFields.OccurredOn, from, to, true, true), Occur.MUST);
			}

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
					// Every word, and every "quoted phrase" as a phrase, must occur in some text field (title, keywords,
					// summary or the full text, which carries call notes). The clauses are built here from analyzed tokens
					// rather than handed to a query parser, so no request syntax beyond the quotes reaches Lucene (plan R2.4).
					var clauses = ParseClauses(text);
					if (clauses.Count > 0)
					{
						var all = new BooleanQuery();
						foreach (var clause in clauses)
							all.Add(TextClause(clause), Occur.MUST);
						textQuery.Add(all, Occur.SHOULD);

						// A final unquoted word still prefix-matches (the user is mid-word): titles and identifiers through
						// their edge n-grams, the summary and full text through a prefix query once the stub is long enough.
						var last = clauses[clauses.Count - 1];
						if (!last.Quoted && last.Tokens.Count == 1)
						{
							var stub = last.Tokens[0].Text;
							var lastPrefix = new BooleanQuery();
							foreach (var clause in clauses.Take(clauses.Count - 1))
								lastPrefix.Add(TextClause(clause), Occur.MUST);
							var stubQuery = new BooleanQuery
							{
								{ new TermQuery(new Term(GlobalIndexFields.TitlePrefix, stub)) { Boost = 2f }, Occur.SHOULD },
								{ new TermQuery(new Term(GlobalIndexFields.KeywordsPrefix, stub)) { Boost = 3f }, Occur.SHOULD }
							};
							if (stub.Length >= MinTextPrefixLength)
							{
								stubQuery.Add(new PrefixQuery(new Term(GlobalIndexFields.Summary, stub)) { Boost = 1.5f }, Occur.SHOULD);
								stubQuery.Add(new PrefixQuery(new Term(GlobalIndexFields.SearchText, stub)), Occur.SHOULD);
							}
							lastPrefix.Add(stubQuery, Occur.MUST);
							textQuery.Add(lastPrefix, Occur.SHOULD);
						}
					}
				}

				textQuery.Add(new TermQuery(new Term(GlobalIndexFields.KeywordExact, text.ToLowerInvariant())) { Boost = 6f }, Occur.SHOULD);
				query.Add(textQuery, Occur.MUST);
			}

			return query;
		}

		/// <summary>Shortest mid-word stub expanded against the summary and full text; shorter stubs match titles and identifiers only.</summary>
		public const int MinTextPrefixLength = 3;

		private const int MaxClauses = 12;

		private static readonly Regex QueryParts = new Regex("\"([^\"]*)\"|([^\\s\"]+)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		/// <summary>One analyzed token and its position inside its clause (stop words leave gaps).</summary>
		public sealed class ClauseToken
		{
			public string Text { get; set; }
			public int Position { get; set; }
		}

		/// <summary>A word or a phrase of the user's query, already analyzed.</summary>
		public sealed class QueryClause
		{
			public bool Quoted { get; set; }
			public List<ClauseToken> Tokens { get; set; } = new List<ClauseToken>();
		}

		/// <summary>
		/// Splits the user text into clauses: each "quoted phrase" is one clause, each other word is one clause (a word the
		/// analyzer splits, such as 2026-000123, stays one phrase clause). Stop words vanish; an unmatched quote is ignored.
		/// </summary>
		public static List<QueryClause> ParseClauses(string text)
		{
			var clauses = new List<QueryClause>();
			if (string.IsNullOrWhiteSpace(text))
				return clauses;

			foreach (Match match in QueryParts.Matches(text))
			{
				if (clauses.Count >= MaxClauses)
					break;
				var quoted = match.Groups[1].Success;
				var tokens = AnalyzeWithPositions(quoted ? match.Groups[1].Value : match.Groups[2].Value);
				if (tokens.Count > 0)
					clauses.Add(new QueryClause { Quoted = quoted, Tokens = tokens });
			}

			return clauses;
		}

		private static Query TextClause(QueryClause clause)
		{
			var any = new BooleanQuery();
			foreach (var field in TextFields)
			{
				Query fieldQuery;
				if (clause.Tokens.Count == 1)
				{
					fieldQuery = new TermQuery(new Term(field, clause.Tokens[0].Text));
				}
				else
				{
					var phrase = new PhraseQuery();
					foreach (var token in clause.Tokens)
						phrase.Add(new Term(field, token.Text), token.Position);
					fieldQuery = phrase;
				}
				fieldQuery.Boost = TextBoosts[field];
				any.Add(fieldQuery, Occur.SHOULD);
			}
			return any;
		}

		private static List<ClauseToken> AnalyzeWithPositions(string text)
		{
			var tokens = new List<ClauseToken>();
			if (string.IsNullOrWhiteSpace(text))
				return tokens;

			using var analyzer = GlobalIndexFields.CreateQueryAnalyzer();
			using var stream = analyzer.GetTokenStream(GlobalIndexFields.SearchText, new StringReader(text));
			var term = stream.AddAttribute<ICharTermAttribute>();
			var increment = stream.AddAttribute<IPositionIncrementAttribute>();
			stream.Reset();
			var position = -1;
			while (stream.IncrementToken() && tokens.Count < 16)
			{
				position += Math.Max(1, increment.PositionIncrement);
				var value = term.ToString();
				if (value.Length > 0)
					tokens.Add(new ClauseToken { Text = value, Position = position });
			}
			stream.End();

			// Positions relative to the clause: a leading stop word must not shift the whole phrase.
			if (tokens.Count > 0)
			{
				var first = tokens[0].Position;
				foreach (var token in tokens)
					token.Position -= first;
			}
			return tokens;
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
