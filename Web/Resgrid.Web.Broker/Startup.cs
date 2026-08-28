using System;
using System.Configuration;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Providers.Cache;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.Messaging;
using Resgrid.Providers.NumberProvider;
using Resgrid.Providers.PdfProvider;
using Resgrid.Providers.ProtectedData;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;
using Resgrid.Web.Broker.Services;

namespace Resgrid.Web.Broker
{
	public class Startup
	{
		public IConfiguration Configuration { get; }

		public ILifetimeScope AutofacContainer { get; private set; }
		public AutofacServiceLocator Locator { get; private set; }

		public Startup(IWebHostEnvironment env)
		{
			var builder = new ConfigurationBuilder()
				.SetBasePath(env.ContentRootPath)
				.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
				.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
				.AddEnvironmentVariables();

			Configuration = builder.Build();
		}

		public void ConfigureServices(IServiceCollection services)
		{
			bool configResult = ConfigProcessor.LoadAndProcessConfig(Configuration["AppOptions:ConfigPath"]);
			bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());

			// Same legacy ConnectionStrings bridge the other web hosts use (in-memory only).
			var settings = System.Configuration.ConfigurationManager.ConnectionStrings;
			var element = typeof(ConfigurationElement).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);
			var collection = typeof(ConfigurationElementCollection).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);

			element.SetValue(settings, false);
			collection.SetValue(settings, false);

			if (!configResult && !envConfigResult)
				settings.Add(new ConnectionStringSettings("ResgridContext", Configuration["ConnectionStrings:ResgridContext"]));
			else
				settings.Add(new ConnectionStringSettings("ResgridContext", DataConfig.ConnectionString));

			collection.SetValue(settings, true);
			element.SetValue(settings, true);

			Framework.Logging.Initialize(ExternalErrorConfig.ExternalErrorServiceUrlForBroker);

			services.AddControllers();
			services.AddMemoryCache();
			services.AddHealthChecks();
			services.AddHostedService<AdpMigrationSweepService>();
		}

		public void ConfigureContainer(ContainerBuilder builder)
		{
			// The broker resolves the same service graph the workers do (the migration coordinator
			// runs here), so it mirrors the Bootstrapper module list...
			builder.RegisterModule(new DataModule());
			builder.RegisterModule(new NoSqlDataModule());
			builder.RegisterModule(new ServicesModule());
			builder.RegisterModule(new ProviderModule());
			builder.RegisterModule(new EmailProviderModule());
			builder.RegisterModule(new BusModule());
			builder.RegisterModule(new RabbitBusModule());
			builder.RegisterModule(new AddressVerificationModule());
			builder.RegisterModule(new NumbersProviderModule());
			builder.RegisterModule(new CacheProviderModule());
			builder.RegisterModule(new MarketingModule());
			builder.RegisterModule(new PdfProviderModule());
			builder.RegisterModule(new MessagingProviderModule());
			builder.RegisterModule(new Resgrid.Providers.Voip.VoipProviderModule());
			builder.RegisterModule(new Resgrid.Providers.Weather.WeatherProviderModule());
			builder.RegisterModule(new Resgrid.Providers.Workflow.WorkflowProviderModule());

			// The broker also registers the CLIENT (pointing at itself) so ServicesModule-resolved
			// services with the write safety net resolve; local engine writes bypass it entirely.
			builder.RegisterModule(new ProtectedDataBrokerClientModule());

			// ...plus the ONE registration no other host may load: the real KMS adapter. Last wins
			// over ServicesModule's fail-closed NotConfigured placeholder.
			builder.RegisterModule(new ProtectedDataProviderModule());

			builder.RegisterType<BrokerOperationService>().AsSelf().SingleInstance();
		}

		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			AutofacContainer = app.ApplicationServices.GetAutofacRoot();

			Locator = new AutofacServiceLocator(AutofacContainer);
			ServiceLocator.SetLocatorProvider(() => Locator);

			ValidateCryptoConfiguration();

			app.UseRouting();

			// Workload gate for every broker API call; /health stays open for k8s probes.
			app.UseMiddleware<Middleware.WorkloadKeyMiddleware>();

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();
				endpoints.MapHealthChecks("/health");
			});
		}

		/// <summary>
		/// Fail startup on invalid production crypto configuration (plan section 13): with the
		/// OpenBao provider selected, resolving it forces the address/certificate checks its
		/// constructor performs — the broker crashes now instead of failing every request later.
		/// Missing grant-validation key material and a missing workload key are logged loudly but
		/// non-fatal: every affected request already fails closed.
		/// </summary>
		private void ValidateCryptoConfiguration()
		{
			var keyWrappingProvider = AutofacContainer.Resolve<IKeyWrappingProvider>();
			if (keyWrappingProvider is NotConfiguredKeyWrappingProvider)
				throw new InvalidOperationException(
					$"The Protected Data Broker has no usable key wrapping provider (configured type: '{DataProtectionConfig.KeyWrappingProviderType}'). Configure OpenBaoTransit (production) or LocalDev (synthetic testing only).");

			var grantService = AutofacContainer.Resolve<Model.Services.IProtectedDataGrantService>();
			if (!grantService.CanValidateGrants)
				Framework.Logging.LogError(
					"Protected Data Broker: no grant validation certificate is configured (DataProtectionConfig.GrantValidationCertificatePath). Every attended field-crypto request will be refused until one is provided.");

			if (string.IsNullOrWhiteSpace(DataProtectionConfig.BrokerApiKey))
				Framework.Logging.LogError(
					"Protected Data Broker: DataProtectionConfig.BrokerApiKey is empty. Every request will be refused (503) until the workload key is provided.");
		}
	}
}
