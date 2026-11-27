using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Search.Knowledge
{
	/// <summary>
	/// K1 evaluation helpers (§16.5): retrieval recall over a golden set (the Academy system set, and later a department's own), citation
	/// accuracy of an answer, and the share of citations answered from a version older than the one in force. Pure; no model is called.
	/// </summary>
	public static class KnowledgeEvaluation
	{
		/// <summary>One golden question and the items (kind:id) a good retrieval returns for it.</summary>
		public sealed class GoldenQuestion
		{
			public GoldenQuestion(string question, params string[] expectedItemKeys)
			{
				Question = question;
				ExpectedItemKeys = expectedItemKeys ?? Array.Empty<string>();
			}

			public string Question { get; }

			public IReadOnlyList<string> ExpectedItemKeys { get; }
		}

		/// <summary>Share of questions with at least one expected item among the first <paramref name="k"/> retrieved items.</summary>
		public static double RecallAtK(IEnumerable<(IReadOnlyList<string> Retrieved, IReadOnlyList<string> Expected)> results, int k)
		{
			var list = (results ?? Enumerable.Empty<(IReadOnlyList<string>, IReadOnlyList<string>)>()).ToList();
			if (list.Count == 0)
				return 0;
			var hits = list.Count(r => (r.Retrieved ?? Array.Empty<string>()).Take(Math.Max(1, k)).Intersect(r.Expected ?? Array.Empty<string>(), StringComparer.Ordinal).Any());
			return (double)hits / list.Count;
		}

		/// <summary>
		/// Share of (part, reference) pairs in an answer that resolve to a citation carrying item, version and anchor (or an Academy lesson)
		/// and that <paramref name="supports"/> confirms (the cited text supports the part). 1 for an answer without parts.
		/// </summary>
		public static double CitationAccuracy(KnowledgeAnswer answer, Func<KnowledgeCitation, string, bool> supports)
		{
			var parts = answer?.Parts ?? new List<KnowledgeAnswerPart>();
			var pairs = parts.SelectMany(p => (p.Refs ?? new List<string>()).Select(r => (Part: p, Ref: r))).ToList();
			if (parts.Count == 0)
				return 1;
			if (pairs.Count == 0)
				return 0;
			var byRef = (answer.Citations ?? new List<KnowledgeCitation>()).GroupBy(c => c.Ref, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			var good = pairs.Count(pair =>
				byRef.TryGetValue(pair.Ref, out var citation) && !string.IsNullOrWhiteSpace(citation.ItemId) &&
				(citation.SourceKind == (int)KnowledgeSourceKinds.Academy || !string.IsNullOrWhiteSpace(citation.VersionId)) &&
				(supports == null || supports(citation, pair.Part.Text)));
			return (double)good / pairs.Count;
		}

		/// <summary>Share of an answer's citations whose version is not the one in force (<paramref name="currentVersions"/>: item key → version id).</summary>
		public static double StaleShare(KnowledgeAnswer answer, IReadOnlyDictionary<string, string> currentVersions)
		{
			var citations = answer?.Citations ?? new List<KnowledgeCitation>();
			if (citations.Count == 0 || currentVersions == null)
				return 0;
			var stale = citations.Count(c => currentVersions.TryGetValue(c.SourceKind + ":" + c.ItemId, out var current) && !string.Equals(current, c.VersionId, StringComparison.Ordinal));
			return (double)stale / citations.Count;
		}
	}
}
