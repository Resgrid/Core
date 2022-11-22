using Autofac;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.NoSqlRepository;

namespace Resgrid.Repositories.DataRepository
{
	public class NoSqlDataModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterGeneric(typeof(MongoRepository<>)).As(typeof(IMongoRepository<>)).InstancePerLifetimeScope();
		}
	}
}
