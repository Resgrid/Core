namespace Resgrid.Config
{
	/// <summary>
	/// Department knowledge index and answers (K1, protocols-training-knowledge-plan §16). Embeddings come from text-embeddings-inference
	/// (TEI) serving bge-m3 on the operator's inference host (enhanced-ai-addon-analysis §6.3; TEI may run on CPU). No key lives in code:
	/// set EmbeddingsApiKey from configuration when the endpoint sits behind an authenticating gateway. Answers use the AiConfig model.
	/// </summary>
	public static class KnowledgeConfig
	{
		/// <summary>The TEI embed route, e.g. http://ai-dispatch-1:8080/embed. Blank: no embeddings, keyword retrieval only.</summary>
		public static string EmbeddingsEndpoint = "";

		/// <summary>Bearer token for the embeddings gateway; blank sends none.</summary>
		public static string EmbeddingsApiKey = "";

		/// <summary>The TEI endpoint is an operator-owned private address (http allowed, RFC 1918 ranges allowed).</summary>
		public static bool EmbeddingsAllowPrivateEndpoint = false;

		/// <summary>Recorded with every stored vector; vectors of another model are never compared, and a change re-embeds on the repair pass.</summary>
		public static string EmbeddingsModel = "BAAI/bge-m3";

		public static int EmbeddingDimensions = 1024;

		/// <summary>Texts per TEI call (TEI's default max client batch size is 32).</summary>
		public static int EmbeddingsBatchSize = 16;

		/// <summary>Deadline for one indexing batch on worker 89.</summary>
		public static int EmbeddingsTimeoutSeconds = 30;

		/// <summary>Deadline for embedding the asker's question in the request; past it the answer uses keyword retrieval and says so.</summary>
		public static int QueryEmbeddingTimeoutMilliseconds = 1500;

		/// <summary>Longest chunk, in characters, before a section is cut into parts.</summary>
		public static int MaxChunkChars = 1800;

		/// <summary>Chunks handed to the model per answer.</summary>
		public static int TopK = 6;

		/// <summary>The per-source diversity cap: most chunks taken from one item.</summary>
		public static int MaxChunksPerItem = 2;

		/// <summary>Relevance floor on the exact cosine (bge-m3, normalized) for a source to count as covering the question.</summary>
		public static double MinCosine = 0.5;

		/// <summary>Relevance floor on keyword coverage (share of the question's distinct terms a chunk holds) when scoring by keywords.</summary>
		public static double MinKeywordCoverage = 0.6;

		/// <summary>Departments worker 89 processes per run, oldest due work first.</summary>
		public static int WorkerDepartmentsPerRun = 10;

		/// <summary>Queued items one department pass rebuilds per run; the rest wait for the next run.</summary>
		public static int WorkerItemsPerDepartment = 200;

		/// <summary>UTC hour the nightly repair pass is due (each department is spread over the following hour).</summary>
		public static int RepairHourUtc = 3;

		/// <summary>Departments whose corpus a process keeps in memory for scoring (least recently used dropped first).</summary>
		public static int CorpusCacheDepartments = 64;

		/// <summary>Most output tokens for one answer.</summary>
		public static int AnswerMaxOutputTokens = 900;

		/// <summary>Deadline for the answer model call.</summary>
		public static int AnswerTimeoutSeconds = 45;
	}
}
