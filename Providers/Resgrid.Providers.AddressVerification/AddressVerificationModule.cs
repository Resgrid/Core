using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.AddressVerification
{
	public class AddressVerificationModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<LoqateProvider>().As<IAddressVerificationProvider>().InstancePerLifetimeScope();
		}
	}
}