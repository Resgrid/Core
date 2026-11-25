using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Search.Knowledge
{
	/// <summary>Embedding storage and exact cosine (§25.4a item 11: brute force, one code path for both databases).</summary>
	public static class KnowledgeVectors
	{
		public static byte[] Encode(float[] vector) => KnowledgeVectorCodec.Encode(vector);

		public static float[] Decode(byte[] bytes) => KnowledgeVectorCodec.Decode(bytes);

		/// <summary>
		/// Exact cosine similarity, accumulated in double in index order so the same inputs always give the same value. Null when either
		/// vector is missing, of another length, zero, or not finite.
		/// </summary>
		public static double? Cosine(float[] a, float[] b)
		{
			if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
				return null;
			double dot = 0, na = 0, nb = 0;
			for (var i = 0; i < a.Length; i++)
			{
				double x = a[i], y = b[i];
				dot += x * y;
				na += x * x;
				nb += y * y;
			}
			if (na <= 0 || nb <= 0 || double.IsNaN(dot) || double.IsInfinity(dot))
				return null;
			return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
		}
	}

	/// <summary>Tokenizing as the search index does (Lucene StandardAnalyzer), with English plural folding (EnglishMinimalStemmer).</summary>
	public static class KnowledgeTokens
	{
		private static readonly StandardAnalyzer Analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
		private static readonly EnglishMinimalStemmer Stemmer = new EnglishMinimalStemmer();

		public static List<string> Tokenize(string text, int max = 4000)
		{
			var tokens = new List<string>();
			if (string.IsNullOrWhiteSpace(text))
				return tokens;

			using var stream = Analyzer.GetTokenStream("body", new StringReader(text));
			var term = stream.AddAttribute<ICharTermAttribute>();
			stream.Reset();
			while (tokens.Count < max && stream.IncrementToken())
			{
				var buffer = term.Buffer;
				var length = Stemmer.Stem(buffer, term.Length);
				if (length > 0 && length <= 64)
					tokens.Add(new string(buffer, 0, length));
			}
			stream.End();
			return tokens;
		}
	}

	/// <summary>A chunk ready to score: its decoded vector and its term counts (prepared once per corpus revision).</summary>
	public sealed class PreparedKnowledgeChunk
	{
		public KnowledgeChunk Chunk { get; set; }
		public float[] Vector { get; set; }
		public Dictionary<string, int> Terms { get; set; }
		public int Length { get; set; }
		public HashSet<string> HeadingTerms { get; set; }

		public static PreparedKnowledgeChunk From(KnowledgeChunk chunk)
		{
			var tokens = KnowledgeTokens.Tokenize(KnowledgeChunker.EmbeddingInput(chunk));
			var terms = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var token in tokens)
				terms[token] = terms.TryGetValue(token, out var count) ? count + 1 : 1;
			return new PreparedKnowledgeChunk
			{
				Chunk = chunk,
				Vector = KnowledgeVectors.Decode(chunk?.Embedding),
				Terms = terms,
				Length = tokens.Count,
				HeadingTerms = new HashSet<string>(KnowledgeTokens.Tokenize((chunk?.Title ?? string.Empty) + " " + (chunk?.HeadingPath ?? string.Empty)), StringComparer.Ordinal)
			};
		}
	}

	/// <summary>
	/// Hybrid ranking, pure and deterministic: exact cosine and BM25 are computed over the candidates the caller already filtered (department,
	/// audience, effective dates), fused by reciprocal rank, re-ranked by how much of the question the section's headings name, ordered
	/// with the chunk id as the final tie-break, and cut to the top k with at most <c>maxPerItem</c> chunks from any one item.
	/// </summary>
	public static class KnowledgeRanker
	{
		public const double Bm25K1 = 1.2;
		public const double Bm25B = 0.75;
		public const int FusionK = 60;
		public const double HeadingBoost = 0.01;

		public sealed class Options
		{
			public float[] QueryVector { get; set; }
			public string QueryModel { get; set; }
			public int TopK { get; set; } = 6;
			public int MaxPerItem { get; set; } = 2;
			public double MinCosine { get; set; } = 0.5;
			public double MinCoverage { get; set; } = 0.6;
		}

		public static KnowledgeRetrievalResult Rank(IReadOnlyList<PreparedKnowledgeChunk> candidates, string question, Options options)
		{
			options ??= new Options();
			var result = new KnowledgeRetrievalResult { Candidates = candidates?.Count ?? 0, Mode = KnowledgeRetrievalModes.KeywordOnly };
			if (candidates == null || candidates.Count == 0)
				return result;

			var queryTerms = KnowledgeTokens.Tokenize(question, 256).Distinct(StringComparer.Ordinal).ToList();
			var scored = candidates.Select(c => new KnowledgeScoredChunk { Chunk = c.Chunk }).ToList();

			// Vector half: exact cosine against vectors of the question's model only.
			var anyCosine = false;
			if (options.QueryVector != null && options.QueryVector.Length > 0)
			{
				for (var i = 0; i < candidates.Count; i++)
				{
					var chunk = candidates[i].Chunk;
					if (candidates[i].Vector == null || !string.Equals(chunk.EmbeddingModel, options.QueryModel, StringComparison.Ordinal))
						continue;
					scored[i].Cosine = KnowledgeVectors.Cosine(options.QueryVector, candidates[i].Vector);
					anyCosine |= scored[i].Cosine.HasValue;
				}
			}

			// Keyword half: BM25 over the same candidates (document frequencies counted among them only).
			if (queryTerms.Count > 0)
			{
				var n = candidates.Count;
				var averageLength = Math.Max(1.0, candidates.Average(c => (double)c.Length));
				var idf = queryTerms.ToDictionary(t => t, t =>
				{
					var df = candidates.Count(c => c.Terms.ContainsKey(t));
					return Math.Log(1 + (n - df + 0.5) / (df + 0.5));
				}, StringComparer.Ordinal);

				for (var i = 0; i < candidates.Count; i++)
				{
					var c = candidates[i];
					double bm25 = 0;
					var matched = 0;
					foreach (var t in queryTerms)
					{
						if (!c.Terms.TryGetValue(t, out var f))
							continue;
						matched++;
						bm25 += idf[t] * (f * (Bm25K1 + 1)) / (f + Bm25K1 * (1 - Bm25B + Bm25B * c.Length / averageLength));
					}
					scored[i].Keyword = bm25;
					scored[i].Coverage = (double)matched / queryTerms.Count;
				}
			}

			result.Mode = anyCosine ? KnowledgeRetrievalModes.Hybrid : KnowledgeRetrievalModes.KeywordOnly;

			// Reciprocal rank fusion: ranks, not raw scores, so neither half's scale dominates.
			var vectorRank = Ranks(scored, s => s.Cosine, candidates);
			var keywordRank = Ranks(scored, s => s.Keyword > 0 ? s.Keyword : (double?)null, candidates);
			for (var i = 0; i < scored.Count; i++)
			{
				double fused = 0;
				if (vectorRank.TryGetValue(i, out var vr))
					fused += 1.0 / (FusionK + vr);
				if (keywordRank.TryGetValue(i, out var kr))
					fused += 1.0 / (FusionK + kr);
				if (fused > 0 && queryTerms.Count > 0)
				{
					var headingHits = queryTerms.Count(t => candidates[i].HeadingTerms.Contains(t));
					fused += HeadingBoost * headingHits / queryTerms.Count;
				}
				scored[i].Score = fused;
			}

			var ordered = scored.Where(s => s.Score > 0)
				.OrderByDescending(s => s.Score)
				.ThenBy(s => s.Chunk.KnowledgeChunkId)
				.ThenBy(s => s.Chunk.ItemKey, StringComparer.Ordinal)
				.ThenBy(s => s.Chunk.Ordinal)
				.ToList();

			var perItem = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var s in ordered)
			{
				if (result.Selected.Count >= Math.Max(1, options.TopK))
					break;
				perItem.TryGetValue(s.Chunk.ItemKey, out var taken);
				if (taken >= Math.Max(1, options.MaxPerItem))
					continue;
				perItem[s.Chunk.ItemKey] = taken + 1;
				result.Selected.Add(s);
			}

			result.Adequate = result.Selected.Any(s => s.Cosine.HasValue && s.Cosine.Value >= options.MinCosine || s.Coverage >= options.MinCoverage && s.Keyword > 0);
			return result;
		}

		/// <summary>1-based ranks of the candidates that have a value, best first, ties broken by chunk id.</summary>
		private static Dictionary<int, int> Ranks(List<KnowledgeScoredChunk> scored, Func<KnowledgeScoredChunk, double?> value, IReadOnlyList<PreparedKnowledgeChunk> candidates)
		{
			var order = Enumerable.Range(0, scored.Count)
				.Where(i => value(scored[i]).HasValue)
				.OrderByDescending(i => value(scored[i]).Value)
				.ThenBy(i => candidates[i].Chunk.KnowledgeChunkId)
				.ThenBy(i => candidates[i].Chunk.ItemKey, StringComparer.Ordinal)
				.ThenBy(i => candidates[i].Chunk.Ordinal)
				.ToList();
			var ranks = new Dictionary<int, int>();
			for (var r = 0; r < order.Count; r++)
				ranks[order[r]] = r + 1;
			return ranks;
		}
	}
}
