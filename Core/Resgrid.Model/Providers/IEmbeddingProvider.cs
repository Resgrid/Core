using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Text embeddings for the department knowledge index (K1): text-embeddings-inference (TEI) serving bge-m3 (1024 dimensions) on the
	/// operator's inference host (enhanced-ai-addon-analysis §6.3). Every call is bounded by a deadline. Indexing calls it only from
	/// worker 89; a request thread embeds only the asker's question, with a short deadline, and falls back to keyword retrieval when it
	/// fails. Vectors come back L2-normalized.
	/// </summary>
	public interface IEmbeddingProvider
	{
		/// <summary>An endpoint and a model are configured (KnowledgeConfig); false means keyword-only retrieval.</summary>
		bool IsConfigured { get; }

		/// <summary>The model id recorded with each stored vector; vectors of another model are never compared.</summary>
		string ModelId { get; }

		int Dimensions { get; }

		/// <summary>
		/// One vector per text, in order. Throws <see cref="EmbeddingUnavailableException"/> when the service is not configured, does not
		/// answer within <paramref name="deadline"/>, or answers with the wrong shape.
		/// </summary>
		Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, TimeSpan deadline, CancellationToken cancellationToken);
	}

	/// <summary>The embeddings service could not be used; callers fall back to keyword retrieval or leave the chunk for the next pass.</summary>
	public sealed class EmbeddingUnavailableException : Exception
	{
		public EmbeddingUnavailableException(string message, Exception inner = null) : base(message, inner) { }
	}
}
