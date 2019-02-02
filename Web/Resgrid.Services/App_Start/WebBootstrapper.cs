using System.Web.Mvc;
using Autofac;
using Autofac.Integration.Mvc;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Cache;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.NumberProvider;
using Resgrid.Providers.PdfProvider;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;
using Autofac.Integration.WebApi;
using Resgrid.Providers.Audio;
using Resgrid.Providers.Firebase;

namespace Resgrid.Web.Services
{
	public static class WebBootstrapper
	{
		private static IContainer _container;

		public static void Initialize()
		{
			if (_container == null)
			{
				var builder = new ContainerBuilder();
				builder.RegisterModule(new DataModule());
				builder.RegisterModule(new ServicesModule());
				builder.RegisterModule(new ProviderModule());
				builder.RegisterModule(new EmailProviderModule());
				builder.RegisterModule(new BusModule());
				builder.RegisterModule(new AddressVerificationModule());
				builder.RegisterModule(new NumbersProviderModule());
				builder.RegisterModule(new CacheProviderModule());
				builder.RegisterModule(new MarketingModule());
				builder.RegisterModule(new PdfProviderModule());
				builder.RegisterModule(new WebServicesModule());
				builder.RegisterModule(new AudioProviderModule());
				builder.RegisterModule(new FirebaseProviderModule());

				// Register your MVC controllers. (MvcApplication is the name of
				// the class in Global.asax.)
				//builder.RegisterControllers(typeof(WebBootstrapper).Assembly);
				builder.RegisterApiControllers(typeof(WebBootstrapper).Assembly);

				// OPTIONAL: Register model binders that require DI.
				builder.RegisterModelBinders(typeof(WebBootstrapper).Assembly);
				builder.RegisterModelBinderProvider();

				// OPTIONAL: Register web abstractions like HttpContextBase.
				builder.RegisterModule<AutofacWebTypesModule>();

				// OPTIONAL: Enable property injection in view pages.
				builder.RegisterSource(new ViewRegistrationSource());

				// OPTIONAL: Enable property injection into action filters.
				builder.RegisterFilterProvider();

				// Set the dependency resolver to be Autofac.
				_container = builder.Build();

				// Prime these Singletons
				var eventAggregator = _container.Resolve<IEventAggregator>();
				var outbound = _container.Resolve<IOutboundEventProvider>();
				var eventService = _container.Resolve<ICoreEventService>();

				DependencyResolver.SetResolver(new AutofacDependencyResolver(_container));
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
