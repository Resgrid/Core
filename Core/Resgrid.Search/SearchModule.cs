using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Search
{
	/// <summary>
	/// Registers the shared Lucene host (one per process) and the records index services. Reader and writer
	/// share the host; which side a process uses is decided by who calls it, so the same module serves Web,
	/// API and Worker.
	/// </summary>
	public class SearchModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<LuceneRecordsIndexHost>().AsSelf().SingleInstance();
			builder.RegisterType<LuceneRecordsSearchService>().As<IRecordsSearchService>().InstancePerLifetimeScope();
			builder.RegisterType<LuceneRecordsIndexer>().As<IRecordsSearchIndexer>().InstancePerLifetimeScope();
		}
	}
}
