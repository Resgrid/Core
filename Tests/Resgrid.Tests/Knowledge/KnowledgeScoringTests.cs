using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Search.Knowledge;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>K1 scoring inside Resgrid.Search: exact cosine, deterministic order, hybrid merge, the diversity cap, and filtering before scoring.</summary>
	[TestFixture]
	public class KnowledgeScoringTests
	{
		private const int DepartmentId = 7;
		private const string Model = "BAAI/bge-m3";

		private static KnowledgeChunk Chunk(long id, string itemId, string text, float[] vector = null, int ordinal = 0, string title = "Protocol",
			KnowledgeSourceKinds kind = KnowledgeSourceKinds.Protocol) => new KnowledgeChunk
		{
			KnowledgeChunkId = id,
			DepartmentId = DepartmentId,
			SourceKind = (int)kind,
			ItemId = itemId,
			Ordinal = ordinal,
			Title = title,
			Text = text,
			Embedding = KnowledgeVectorCodec.Encode(vector),
			EmbeddingModel = vector == null ? null : Model
		};

		private static List<PreparedKnowledgeChunk> Prepare(params KnowledgeChunk[] chunks) => chunks.Select(PreparedKnowledgeChunk.From).ToList();

		[Test]
		public void Cosine_is_exact_and_refuses_mismatched_or_empty_vectors()
		{
			KnowledgeVectors.Cosine(new[] { 1f, 0f }, new[] { 1f, 1f }).Should().BeApproximately(1 / Math.Sqrt(2), 1e-12);
			KnowledgeVectors.Cosine(new[] { 3f, 4f }, new[] { 3f, 4f }).Should().BeApproximately(1.0, 1e-12);
			KnowledgeVectors.Cosine(new[] { 1f, 0f }, new[] { -1f, 0f }).Should().BeApproximately(-1.0, 1e-12);
			KnowledgeVectors.Cosine(new[] { 1f, 0f }, new[] { 1f, 0f, 0f }).Should().BeNull();
			KnowledgeVectors.Cosine(new[] { 0f, 0f }, new[] { 1f, 0f }).Should().BeNull();
			KnowledgeVectors.Cosine(null, new[] { 1f }).Should().BeNull();
		}

		[Test]
		public void Stored_vectors_round_trip_as_little_endian_float32_bytes()
		{
			var vector = new[] { 0.5f, -1.25f, float.Epsilon, 3.14159f };
			var bytes = KnowledgeVectorCodec.Encode(vector);
			bytes.Should().HaveCount(16);
			bytes.Take(4).Should().Equal(BitConverter.IsLittleEndian ? BitConverter.GetBytes(0.5f) : BitConverter.GetBytes(0.5f).Reverse());
			KnowledgeVectorCodec.Decode(bytes).Should().Equal(vector);
			KnowledgeVectorCodec.Decode(new byte[] { 1, 2, 3 }).Should().BeNull();
		}

		[Test]
		public void The_same_question_over_the_same_chunks_always_gives_the_same_order_with_ties_broken_by_chunk_id()
		{
			var chunks = Prepare(
				Chunk(30, "a", "Tourniquet placement above the wound."),
				Chunk(10, "b", "Tourniquet placement above the wound."),
				Chunk(20, "c", "Tourniquet placement above the wound."));
			var options = new KnowledgeRanker.Options { TopK = 3, MaxPerItem = 2 };

			var first = KnowledgeRanker.Rank(chunks, "tourniquet placement", options).Selected.Select(s => s.Chunk.KnowledgeChunkId).ToList();
			var second = KnowledgeRanker.Rank(chunks.AsEnumerable().Reverse().ToList(), "tourniquet placement", options).Selected.Select(s => s.Chunk.KnowledgeChunkId).ToList();

			first.Should().Equal(10, 20, 30);
			second.Should().Equal(first);
		}

		[Test]
		public void Hybrid_ranking_fuses_the_vector_and_keyword_ranks_and_reports_the_mode()
		{
			var query = new[] { 1f, 0f, 0f };
			var chunks = Prepare(
				Chunk(1, "vector-only", "Unrelated words entirely.", new[] { 1f, 0f, 0f }),
				Chunk(2, "both", "Airway management with a head tilt.", new[] { 0.9f, 0.1f, 0f }),
				Chunk(3, "keyword-only", "Airway management with a head tilt chin lift.", null),
				Chunk(4, "neither", "Parking rules.", new[] { 0f, 0f, 1f }));

			var result = KnowledgeRanker.Rank(chunks, "airway head tilt", new KnowledgeRanker.Options { QueryVector = query, QueryModel = Model, TopK = 4, MaxPerItem = 2 });

			result.Mode.Should().Be(KnowledgeRetrievalModes.Hybrid);
			result.Selected.First().Chunk.ItemId.Should().Be("both", "it ranks well on both halves");
			result.Selected.Select(s => s.Chunk.ItemId).Should().Contain(new[] { "vector-only", "keyword-only" });
			result.Selected.Single(s => s.Chunk.ItemId == "both").Cosine.Should().BeApproximately(KnowledgeVectors.Cosine(query, new[] { 0.9f, 0.1f, 0f }).Value, 1e-12);
			result.Selected.Single(s => s.Chunk.ItemId == "keyword-only").Cosine.Should().BeNull();
			result.Adequate.Should().BeTrue();
		}

		[Test]
		public void Vectors_of_another_model_are_never_compared()
		{
			var chunk = Chunk(1, "a", "Something else.", new[] { 1f, 0f });
			chunk.EmbeddingModel = "other/model";

			var result = KnowledgeRanker.Rank(Prepare(chunk), "question", new KnowledgeRanker.Options { QueryVector = new[] { 1f, 0f }, QueryModel = Model });

			result.Mode.Should().Be(KnowledgeRetrievalModes.KeywordOnly);
			result.Selected.Should().BeEmpty();
			result.Adequate.Should().BeFalse();
		}

		[Test]
		public void The_diversity_cap_takes_at_most_n_chunks_from_one_item()
		{
			var chunks = Prepare(Enumerable.Range(1, 6).Select(i => Chunk(i, "big", "Hazmat decontamination step " + i + ".", ordinal: i))
				.Concat(new[] { Chunk(20, "small", "Hazmat decontamination corridor.") }).ToArray());

			var result = KnowledgeRanker.Rank(chunks, "hazmat decontamination", new KnowledgeRanker.Options { TopK = 6, MaxPerItem = 2 });

			result.Selected.Count(s => s.Chunk.ItemId == "big").Should().Be(2);
			result.Selected.Select(s => s.Chunk.ItemId).Should().Contain("small");
			result.Selected.Should().HaveCount(3);
		}

		[Test]
		public void Keyword_coverage_below_the_floor_is_not_adequate()
		{
			var chunks = Prepare(Chunk(1, "a", "Ladder placement and climbing angle."));

			KnowledgeRanker.Rank(chunks, "ladder angle", new KnowledgeRanker.Options()).Adequate.Should().BeTrue();
			KnowledgeRanker.Rank(chunks, "ladder rescue knots harness rope", new KnowledgeRanker.Options()).Adequate.Should().BeFalse();
			KnowledgeRanker.Rank(chunks, "zzzz", new KnowledgeRanker.Options()).Selected.Should().BeEmpty();
		}

		[Test]
		public async Task The_retriever_filters_by_department_audience_and_dates_before_anything_is_scored()
		{
			var now = DateTime.UtcNow;
			var readable = Chunk(1, "10", "Tourniquet placement above the wound.");
			var unreadable = Chunk(2, "11", "Tourniquet placement above the wound, high and tight. Tourniquet tourniquet.");
			var future = Chunk(3, "10", "Tourniquet placement future version.");
			future.EffectiveOn = now.AddDays(5);
			var expired = Chunk(4, "10", "Tourniquet placement expired version.");
			expired.ExpiresOn = now.AddDays(-1);
			var otherDepartment = Chunk(5, "10", "Tourniquet placement in another department.");
			otherDepartment.DepartmentId = 8;
			var academy = new KnowledgeChunk { KnowledgeChunkId = 6, DepartmentId = 0, SourceKind = (int)KnowledgeSourceKinds.Academy, ItemId = "05-calls", Title = "Academy", Text = "Tourniquet lesson." };

			var repository = new Mock<IKnowledgeIndexRepository>();
			repository.Setup(r => r.GetRevisionsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Dictionary<int, long> { [DepartmentId] = 1, [0] = 1 });
			repository.Setup(r => r.GetCorpusAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunk> { readable, unreadable, future, expired, otherDepartment });
			repository.Setup(r => r.GetCorpusAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunk> { academy });

			var scope = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = "u", IsCurrentMember = true };
			scope.Allow(KnowledgeSourceKinds.Protocol, new[] { "10" });

			var retriever = new KnowledgeRetriever(repository.Object, new KnowledgeCorpusCache());
			var result = await retriever.RetrieveAsync(new KnowledgeRetrievalRequest { Scope = scope, Question = "tourniquet placement", TopK = 10, MaxPerItem = 5 });

			result.Candidates.Should().Be(2, "only the readable, in-effect chunk and the Academy lesson survive the filter");
			result.Selected.Select(s => s.Chunk.KnowledgeChunkId).Should().BeEquivalentTo(new long[] { 1, 6 });

			var scoped = await retriever.RetrieveAsync(new KnowledgeRetrievalRequest
			{
				Scope = scope, Question = "tourniquet placement", OnlyKind = (int)KnowledgeSourceKinds.Protocol, OnlyItemId = "10"
			});
			scoped.Selected.Select(s => s.Chunk.KnowledgeChunkId).Should().Equal(1);

			// The prepared corpus is cached per revision: a second question does not reload it.
			repository.Verify(r => r.GetCorpusAsync(DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_asker_is_answered_from_the_translation_in_their_language_and_never_from_two_languages_of_one_item()
		{
			// Round 5F: the source (English) and its approved French variant of protocol 10; protocol 11 has no translation.
			var english = Chunk(1, "10", "Tourniquet placement above the wound.");
			english.Locale = "en";
			var french = Chunk(2, "10", "Tourniquet placement : au-dessus de la plaie.");
			french.Locale = "fr";
			french.AccessFlags = (int)KnowledgeAccessFlags.LocaleVariant;
			var other = Chunk(3, "11", "Tourniquet placement for children.");
			var hidden = Chunk(4, "12", "Tourniquet placement : réservé.");
			hidden.Locale = "fr";
			hidden.AccessFlags = (int)KnowledgeAccessFlags.LocaleVariant;

			var repository = new Mock<IKnowledgeIndexRepository>();
			repository.Setup(r => r.GetRevisionsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Dictionary<int, long> { [DepartmentId] = 1, [0] = 1 });
			repository.Setup(r => r.GetCorpusAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunk> { english, french, other, hidden });
			repository.Setup(r => r.GetCorpusAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunk>());
			var scope = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = "u", IsCurrentMember = true };
			scope.Allow(KnowledgeSourceKinds.Protocol, new[] { "10", "11" });
			var retriever = new KnowledgeRetriever(repository.Object, new KnowledgeCorpusCache());

			var inFrench = await retriever.RetrieveAsync(new KnowledgeRetrievalRequest { Scope = scope, Question = "tourniquet placement", TopK = 10, MaxPerItem = 5, Locale = "fr-CA" });
			inFrench.Selected.Select(s => s.Chunk.KnowledgeChunkId).Should().BeEquivalentTo(new long[] { 2, 3 }, "the French variant replaces its source; protocol 12 is not readable");

			var inEnglish = await retriever.RetrieveAsync(new KnowledgeRetrievalRequest { Scope = scope, Question = "tourniquet placement", TopK = 10, MaxPerItem = 5, Locale = "en" });
			inEnglish.Selected.Select(s => s.Chunk.KnowledgeChunkId).Should().BeEquivalentTo(new long[] { 1, 3 });

			var noLocale = await retriever.RetrieveAsync(new KnowledgeRetrievalRequest { Scope = scope, Question = "tourniquet placement", TopK = 10, MaxPerItem = 5 });
			noLocale.Selected.Select(s => s.Chunk.KnowledgeChunkId).Should().BeEquivalentTo(new long[] { 1, 3 }, "no language asked: the source");
		}

		[Test]
		public async Task A_non_member_retrieves_nothing_at_all()
		{
			var repository = new Mock<IKnowledgeIndexRepository>(MockBehavior.Strict);
			var retriever = new KnowledgeRetriever(repository.Object, new KnowledgeCorpusCache());

			var result = await retriever.RetrieveAsync(new KnowledgeRetrievalRequest { Scope = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = "u" }, Question = "anything" });

			result.Selected.Should().BeEmpty();
			result.Candidates.Should().Be(0);
		}
	}
}
