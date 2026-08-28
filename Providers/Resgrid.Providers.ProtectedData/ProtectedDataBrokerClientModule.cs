using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.ProtectedData
{
	/// <summary>
	/// Registers the application-tier broker CLIENT. Unlike ProtectedDataProviderModule (broker
	/// hosts only), this module is safe anywhere: the client holds no key material and can only ask
	/// the broker to act on a caller's grant. Load it in Web/API composition roots that serve
	/// protected reads/writes.
	/// </summary>
	public class ProtectedDataBrokerClientModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<ProtectedDataBrokerClient>().As<IProtectedDataBrokerClient>().SingleInstance();
		}
	}
}
