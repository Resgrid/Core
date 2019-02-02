using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Marketing
{
	public class MarketingModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<MailerliteEmailMarketing>().As<IEmailMarketingProvider>().InstancePerLifetimeScope();
			builder.RegisterType<ShortenUrlProvider>().As<IShortenUrlProvider>().InstancePerLifetimeScope();
		}
	}
}
