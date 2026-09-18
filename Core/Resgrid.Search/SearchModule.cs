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
		}
	}
}
