using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Voip
{
	public class VoipProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<VoipProvider>().As<IVoipProvider>().InstancePerLifetimeScope();
		}
	}
}
