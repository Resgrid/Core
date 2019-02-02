using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.EmailProvider
{
	public class EmailProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<PostmarkTemplateProvider>().As<IEmailProvider>().InstancePerLifetimeScope();
			builder.RegisterType<CallEmailProvider>().As<ICallEmailProvider>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListProvider>().As<IDistributionListProvider>().InstancePerLifetimeScope();
			builder.RegisterType<PostmarkEmailSender>().As<IEmailSender>().InstancePerLifetimeScope();
			builder.RegisterType<AmazonEmailSender>().As<IAmazonEmailSender>().InstancePerLifetimeScope();
		}
	}
}
