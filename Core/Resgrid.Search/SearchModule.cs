using Autofac;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Search
{
	/// <summary>
	/// Registers the object store (one per process), one shared Lucene host per index (records, global) and the read /
	/// write services over them. Reader and writer share the host; which side a process uses is decided by who calls
	/// it, so the same module serves Web, API and Worker.
	/// </summary>
	public class SearchModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<AdminAssistReferenceSearch>().As<Resgrid.Model.AdminAssist.IAdminAssistReferenceSearch>().SingleInstance();
			builder.Register(c =>
			{
				var s3 = new S3SearchIndexStore();
				return s3.Enabled ? (ISearchIndexStore)s3 : NullSearchIndexStore.Instance;
			}).As<ISearchIndexStore>().SingleInstance();

			builder.RegisterType<LuceneRecordsIndexHost>().AsSelf().SingleInstance();
			builder.RegisterType<LuceneGlobalIndexHost>().AsSelf().SingleInstance();

			builder.RegisterType<LuceneRecordsSearchService>().As<IRecordsSearchService>().InstancePerLifetimeScope();
			builder.RegisterType<LuceneRecordsIndexer>().As<IRecordsSearchIndexer>().InstancePerLifetimeScope();

			builder.RegisterType<LuceneGlobalSearchService>().As<IGlobalSearchService>().InstancePerLifetimeScope();
			builder.RegisterType<LuceneGlobalSearchIndexer>().As<IGlobalSearchIndexer>().InstancePerLifetimeScope();

			// K1 department knowledge: brute-force exact scoring over the department's chunks (decision §25.4a item 11), the prepared
			// corpus cache (one per process) and the TEI bge-m3 embeddings client.
			builder.RegisterType<Knowledge.KnowledgeCorpusCache>().AsSelf().SingleInstance();
			builder.RegisterType<Knowledge.KnowledgeRetriever>().As<IKnowledgeRetriever>().InstancePerLifetimeScope();
			builder.RegisterType<Knowledge.TeiEmbeddingProvider>().As<IEmbeddingProvider>().SingleInstance();
		}
	}
}
