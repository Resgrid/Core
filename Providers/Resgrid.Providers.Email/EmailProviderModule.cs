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
			// ADP outbound net (plan 7.5): scrubs envelopes out of any email whose sender skipped
			// its protected projection. Wraps whichever sender is registered above.
			builder.RegisterDecorator<ProtectedEmailSenderDecorator, IEmailSender>();
			builder.RegisterType<AmazonEmailSender>().As<IAmazonEmailSender>().InstancePerLifetimeScope();
		}
	}
}
