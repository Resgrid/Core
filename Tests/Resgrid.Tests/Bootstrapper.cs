using Autofac;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Moq;
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

				// ADP repositories are not part of the testing data module; loose mocks keep the
				// protection/projection/lock services resolvable. Un-setup members return null, which
				// reads as "no policy row" = Disabled — every safe view is then the original value.
				builder.RegisterInstance(new Moq.Mock<IDepartmentDataProtectionPolicyRepository>().Object)
					.As<IDepartmentDataProtectionPolicyRepository>();
				builder.RegisterInstance(new Moq.Mock<IDepartmentDataProtectionKeyRepository>().Object)
					.As<IDepartmentDataProtectionKeyRepository>();
				builder.RegisterInstance(new Moq.Mock<IDepartmentDataProtectionMigrationRepository>().Object)
					.As<IDepartmentDataProtectionMigrationRepository>();
				builder.RegisterInstance(new Moq.Mock<IDepartmentProtectedDataEgressPolicyRepository>().Object)
					.As<IDepartmentProtectedDataEgressPolicyRepository>();
				builder.RegisterInstance(new Moq.Mock<IDepartmentMemberSensitiveDataRepository>().Object)
					.As<IDepartmentMemberSensitiveDataRepository>();
				builder.RegisterInstance(new Moq.Mock<IDepartmentOperationLockRepository>().Object)
					.As<IDepartmentOperationLockRepository>();
				builder.RegisterInstance(new Moq.Mock<IDepartmentDataProtectionBulkRepository>().Object)
					.As<IDepartmentDataProtectionBulkRepository>();

				// CallsService's write safety net resolves IProtectedWriteService lazily. The real
				// ProtectedReadService needs the broker client (not registered here), and a LOOSE mock
				// would return a null Task from Prepare* (NRE at the await) — so the stub is set up to
				// answer every call with Allowed(): departments in this container are never protected.
				var protectedWriteStub = new Moq.Mock<Resgrid.Model.Services.IProtectedWriteService>();
				protectedWriteStub.Setup(x => x.PreflightWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareCallWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.Call>(), Moq.It.IsAny<Resgrid.Model.Call>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareCallNoteWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.CallNote>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareCallAttachmentWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.CallAttachment>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareContactWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.Contact>(), Moq.It.IsAny<Resgrid.Model.Contact>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareContactNoteWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.ContactNote>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareUnitStateWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.UnitState>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareUdfFieldValueWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.UdfFieldValue>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareLogWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.Log>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareMemberSensitiveDataWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.DepartmentMemberSensitiveData>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				protectedWriteStub.Setup(x => x.PrepareMemberEmergencyContactWriteAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<Resgrid.Model.DepartmentMemberEmergencyContact>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
					.ReturnsAsync(Resgrid.Model.ProtectedWriteResult.Allowed());
				builder.RegisterInstance(protectedWriteStub.Object)
					.As<Resgrid.Model.Services.IProtectedWriteService>();

				// The real FeatureToggleService's repository graph is not in the testing data module;
				// the protection service consumes it only for the enrollment admission gate, which no
				// container-driven test exercises. Loose mock: every flag reads as absent (fail closed).
				builder.RegisterInstance(new Moq.Mock<Resgrid.Model.Services.IFeatureToggleService>().Object)
					.As<Resgrid.Model.Services.IFeatureToggleService>();

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
