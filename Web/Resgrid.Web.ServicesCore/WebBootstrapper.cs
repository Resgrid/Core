using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.CommonServiceLocator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Practices.ServiceLocation;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Audio;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Cache;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.Firebase;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.NumberProvider;
using Resgrid.Providers.PdfProvider;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;

namespace Resgrid.Web.ServicesCore
{
	public static class WebBootstrapper
	{
		private static IContainer _container;
		private static AutofacServiceLocator _locator;

		public static void Initialize(IServiceCollection services)
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
				builder.RegisterModule(new AudioProviderModule());
				builder.RegisterModule(new FirebaseProviderModule());

				if (services != null)
					builder.Populate(services);

				_container = builder.Build();

				// Prime these Singletons
				var eventAggregator = _container.Resolve<IEventAggregator>();
				var outbound = _container.Resolve<IOutboundEventProvider>();
				var eventService = _container.Resolve<ICoreEventService>();

				_locator = new AutofacServiceLocator(_container);
				ServiceLocator.SetLocatorProvider(() => _locator);
			}
		}

		public static IContainer GetKernel()
		{
			if (_container == null)
				Initialize(null);

			return _container;
		}

		public static void SetLocator()
		{
			if (_locator != null)
				ServiceLocator.SetLocatorProvider(() => _locator);
		}
	}
}
