using System.Collections.Generic;
using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.NGram;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace Resgrid.Search
{
	/// <summary>Field names of the global index (GlobalSearchGeneration.SchemaVersion governs changes here).</summary>
	public static class GlobalIndexFields
	{
		public const LuceneVersion Version = LuceneIndexVersion.Version;

		public const string Key = "Key";
		public const string ProjectionId = "ProjectionId";
		public const string DepartmentId = "DepartmentId";
		public const string EntityType = "EntityType";
		public const string EntityId = "EntityId";
		public const string Title = "Title";
		/// <summary>Edge n-grams of the title tokens: typeahead.</summary>
		public const string TitlePrefix = "TitlePrefix";
		public const string Summary = "Summary";
		public const string SearchText = "SearchText";
		public const string Keywords = "Keywords";
		/// <summary>Edge n-grams of identifier tokens (call numbers, callsigns).</summary>
		public const string KeywordsPrefix = "KeywordsPrefix";
		/// <summary>Whole keywords lower-cased, one term each: exact identifier match.</summary>
		public const string KeywordExact = "KeywordExact";
		public const string Category = "Category";
		public const string Status = "Status";
		public const string Priority = "Priority";
		public const string GroupId = "GroupId";
		public const string OwnerUserId = "OwnerUserId";
		public const string ParticipantUserIds = "ParticipantUserIds";
		public const string IsAdminOnly = "IsAdminOnly";
		public const string IsActive = "IsActive";
		public const string OccurredOn = "OccurredOn";
		public const string OccurredOnSort = "OccurredOnSort";
		public const string Url = "Url";
		public const string MetadataJson = "MetadataJson";
		public const string Generation = "Generation";

		public const int PrefixMinGram = 1;
		public const int PrefixMaxGram = 20;

		/// <summary>Index-time analyzer: standard for text, edge n-grams for the prefix fields. Query time uses the plain standard analyzer.</summary>
		public static Analyzer CreateIndexAnalyzer()
		{
			var standard = new StandardAnalyzer(Version);
			var perField = new Dictionary<string, Analyzer>
			{
				[TitlePrefix] = new EdgeNGramAnalyzer(),
				[KeywordsPrefix] = new EdgeNGramAnalyzer()
			};
			return new PerFieldAnalyzerWrapper(standard, perField);
		}

		public static Analyzer CreateQueryAnalyzer() => new StandardAnalyzer(Version);
	}

	/// <summary>Standard tokens, lower-cased, expanded to edge n-grams (1..20) so "eng" matches "Engine".</summary>
	public sealed class EdgeNGramAnalyzer : Analyzer
	{
		protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
		{
			var source = new StandardTokenizer(GlobalIndexFields.Version, reader);
			TokenStream stream = new LowerCaseFilter(GlobalIndexFields.Version, source);
			stream = new EdgeNGramTokenFilter(GlobalIndexFields.Version, stream, GlobalIndexFields.PrefixMinGram, GlobalIndexFields.PrefixMaxGram);
			return new TokenStreamComponents(source, stream);
		}
	}
}
