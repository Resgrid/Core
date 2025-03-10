using System;
using Autofac;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Providers.Cache;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.Firebase;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.Messaging;
using Resgrid.Providers.NumberProvider;
using Resgrid.Providers.PdfProvider;
using Resgrid.Providers.Voip;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;

namespace Resgrid.Workers.Framework
{
	public static class Bootstrapper
	{
		public static AutofacServiceLocator Locator { get; private set; }
		private static IContainer _container;

		public static void Initialize()
		{
			if (_container == null)
			{
				//var serviceProvider = new ServiceCollection()
				//	.BuildServiceProvider();

				var builder = new ContainerBuilder();
				builder.RegisterModule(new DataModule());
				builder.RegisterModule(new NoSqlDataModule());
				builder.RegisterModule(new ServicesModule());
				builder.RegisterModule(new ProviderModule());
				builder.RegisterModule(new EmailProviderModule());
				builder.RegisterModule(new WorkerFrameworkModule());
				builder.RegisterModule(new BusModule());
				builder.RegisterModule(new RabbitBusModule());
				builder.RegisterModule(new AddressVerificationModule());
				builder.RegisterModule(new NumbersProviderModule());
				builder.RegisterModule(new CacheProviderModule());
				builder.RegisterModule(new MarketingModule());
				builder.RegisterModule(new PdfProviderModule());
				builder.RegisterModule(new FirebaseProviderModule());
				builder.RegisterModule(new VoipProviderModule());
				builder.RegisterModule(new MessagingProviderModule());

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
