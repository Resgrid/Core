using Autofac;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Cache;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.Messaging;
using Resgrid.Providers.NumberProvider;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;
using Resgrid.Tests.Mocks;
using Resgrid.Workers.Framework;

namespace Resgrid.Tests
{
	public static class Bootstrapper
	{
		public static AutofacServiceLocator Locator { get; private set; }
		private static IContainer _container;

		public static void Initialize()
		{
			if (_container == null)
			{
				var builder = new ContainerBuilder();
				builder.RegisterModule(new TestingDataModule());
				builder.RegisterModule(new NoSqlDataModule());
				builder.RegisterModule(new ServicesModule());
				builder.RegisterModule(new ProviderModule());
				builder.RegisterModule(new EmailProviderModule());
				builder.RegisterModule(new WorkerFrameworkModule());
				builder.RegisterModule(new BusModule());
				builder.RegisterModule(new AddressVerificationModule());
				builder.RegisterModule(new NumbersProviderModule());
				builder.RegisterModule(new CacheProviderModule());
				builder.RegisterModule(new MarketingModule());
				builder.RegisterModule(new MessagingProviderModule());
				builder.RegisterModule(new Resgrid.Providers.Workflow.WorkflowProviderModule());

				// Override real repository registrations with in-memory mocks so that
				// tests do not require a live database connection.
				builder.RegisterType<MockScheduledTasksRepository>()
					.As<IScheduledTasksRepository>()
					.InstancePerLifetimeScope();
				builder.RegisterType<MockScheduledTaskLogsRepository>()
					.As<IScheduledTaskLogsRepository>()
					.InstancePerLifetimeScope();

				// No-op unit of work — prevents real SQL transaction usage in tests
				builder.RegisterType<MockUnitOfWork>()
					.As<IUnitOfWork>()
					.InstancePerLifetimeScope();

				// IncidentCommandService resolves chat services lazily through the ServiceLocator for
				// its best-effort lane channel hooks. The real ChatChannelService can't activate in
				// this container (its repository graph isn't registered), which logged an activation
				// error on every lane save/delete test. Loose mocks turn the hooks into no-ops:
				// un-setup async members return completed tasks with null results.
				builder.RegisterInstance(new Moq.Mock<Resgrid.Model.Services.IChatChannelService>().Object)
					.As<Resgrid.Model.Services.IChatChannelService>();
				builder.RegisterInstance(new Moq.Mock<IChatChannelRepository>().Object)
					.As<IChatChannelRepository>();

				// UDF mock repositories
				builder.RegisterType<MockUdfDefinitionRepository>()
					.As<IUdfDefinitionRepository>()
					.InstancePerLifetimeScope();
				builder.RegisterType<MockUdfFieldRepository>()
					.As<IUdfFieldRepository>()
					.InstancePerLifetimeScope();
				builder.RegisterType<MockUdfFieldValueRepository>()
					.As<IUdfFieldValueRepository>()
					.InstancePerLifetimeScope();

				_container = builder.Build();

				Locator = new AutofacServiceLocator(_container);
				ServiceLocator.SetLocatorProvider(() => Locator);
			}
		}

		public static IContainer GetKernel()
		{
			if (_container == null)
				Initialize();

			return _container;
		}
	}
}
