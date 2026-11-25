using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Academy;
using Resgrid.Search.Knowledge;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>
	/// The K1 system golden set (§16.5) over the Academy corpus: retrieval-only assertions, no model. Keyword retrieval alone and hybrid
	/// retrieval with a deterministic stand-in embedding (bag of hashed tokens, no service) must both find the expected lesson in the top 3.
	/// </summary>
	[TestFixture]
	public class KnowledgeGoldenSetTests
	{
		/// <summary>The fixture: member-style questions and the Academy lesson that answers each.</summary>
		public static readonly KnowledgeEvaluation.GoldenQuestion[] AcademyGoldenSet =
		{
			new KnowledgeEvaluation.GoldenQuestion("How do I set up shifts and work shifts?", "5:08-shifts"),
			new KnowledgeEvaluation.GoldenQuestion("Where are run cards and the dispatcher desk explained?", "5:05-calls"),
			new KnowledgeEvaluation.GoldenQuestion("How does push-to-talk voice work?", "5:18-ptt"),
			new KnowledgeEvaluation.GoldenQuestion("Records, incident reports and occupancies", "5:14-records"),
			new KnowledgeEvaluation.GoldenQuestion("What does data protection do?", "5:19-adp"),
			new KnowledgeEvaluation.GoldenQuestion("Inventory lesson", "5:13-inventory"),
			new KnowledgeEvaluation.GoldenQuestion("Checklists", "5:11-checklists"),
			new KnowledgeEvaluation.GoldenQuestion("Admin Assist walkthrough", "5:14-admin-assist"),
			new KnowledgeEvaluation.GoldenQuestion("Custom statuses walkthrough", "5:03-custom-statuses"),
			new KnowledgeEvaluation.GoldenQuestion("Mapping, indoor maps and road closures", "5:07-mapping")
		};

		private static List<PreparedKnowledgeChunk> Corpus(bool withVectors)
		{
			var id = 1;
			return AcademyCatalog.Lessons.Select(l =>
			{
				var chunk = AcademyKnowledgeSource.LessonChunk(l);
				chunk.KnowledgeChunkId = id++;
				if (withVectors)
				{
					chunk.Embedding = KnowledgeVectorCodec.Encode(StandIn(KnowledgeChunker.EmbeddingInput(chunk)));
					chunk.EmbeddingModel = "stand-in";
				}
				return PreparedKnowledgeChunk.From(chunk);
			}).ToList();
		}

		/// <summary>A deterministic stand-in for an embedding: tokens hashed into 256 buckets, then normalized. Never a live service.</summary>
		private static float[] StandIn(string text)
		{
			var vector = new float[256];
			foreach (var token in KnowledgeTokens.Tokenize(text))
			{
				var hash = 17;
				foreach (var c in token)
					hash = unchecked(hash * 31 + c);
				vector[(hash & 0x7fffffff) % vector.Length] += 1f;
			}
			var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
			return norm == 0 ? vector : vector.Select(v => v / norm).ToArray();
		}

		private static IReadOnlyList<string> Retrieve(List<PreparedKnowledgeChunk> corpus, string question, bool hybrid) =>
			KnowledgeRanker.Rank(corpus, question, new KnowledgeRanker.Options
			{
				QueryVector = hybrid ? StandIn(question) : null,
				QueryModel = "stand-in",
				TopK = 3,
				MaxPerItem = 1
			}).Selected.Select(s => s.Chunk.ItemKey).ToList();

		[TestCase(false)]
		[TestCase(true)]
		public void Every_golden_question_finds_its_lesson_in_the_top_three(bool hybrid)
		{
			var corpus = Corpus(hybrid);
			var results = AcademyGoldenSet.Select(g => (Retrieved: Retrieve(corpus, g.Question, hybrid), Expected: g.ExpectedItemKeys)).ToList();

			foreach (var (retrieved, expected) in results)
				retrieved.Intersect(expected).Should().NotBeEmpty("expected {0} in [{1}]", string.Join(",", expected), string.Join(",", retrieved));
			KnowledgeEvaluation.RecallAtK(results, 3).Should().Be(1.0);
		}

		[Test]
		public void The_golden_set_runs_the_same_twice()
		{
			var corpus = Corpus(true);
			AcademyGoldenSet.Select(g => Retrieve(corpus, g.Question, true)).Should()
				.BeEquivalentTo(AcademyGoldenSet.Select(g => Retrieve(corpus, g.Question, true)), o => o.WithStrictOrdering());
		}

		[Test]
		public void Citation_accuracy_counts_only_references_that_resolve_to_a_versioned_source_that_supports_the_part()
		{
			var answer = new KnowledgeAnswer
			{
				Parts = new List<KnowledgeAnswerPart>
				{
					new KnowledgeAnswerPart { Text = "Stage uphill.", Refs = new List<string> { "S1" } },
					new KnowledgeAnswerPart { Text = "Wear gloves.", Refs = new List<string> { "S2", "S9" } }
				},
				Citations = new List<KnowledgeCitation>
				{
					new KnowledgeCitation { Ref = "S1", SourceKind = 0, ItemId = "4", VersionId = "40", Excerpt = "Stage uphill and upwind." },
					new KnowledgeCitation { Ref = "S2", SourceKind = 0, ItemId = "5", VersionId = null }
				}
			};

			KnowledgeEvaluation.CitationAccuracy(answer, (c, part) => (c.Excerpt ?? "").Contains(part.TrimEnd('.'))).Should().BeApproximately(1.0 / 3, 1e-9);
			KnowledgeEvaluation.CitationAccuracy(new KnowledgeAnswer(), null).Should().Be(1);
			KnowledgeEvaluation.StaleShare(answer, new Dictionary<string, string> { ["0:4"] = "41" }).Should().Be(0.5);
		}
	}
}
