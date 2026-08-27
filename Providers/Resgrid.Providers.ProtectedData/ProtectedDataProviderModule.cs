using Autofac;
using Resgrid.Config;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.ProtectedData
{
	/// <summary>
	/// Registers the real KMS adapter. Load this module ONLY in the Protected Data Broker host —
	/// never in Web, API, worker, or BackOffice composition roots (plan section 2.2: those hosts have
	/// no KMS role, no route, and keep the fail-closed NotConfiguredKeyWrappingProvider that
	/// ServicesModule registers). Autofac's last-registration-wins means loading this module after
	/// ServicesModule replaces that placeholder on the broker host.
	/// </summary>
	public class ProtectedDataProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			if (string.Equals(DataProtectionConfig.KeyWrappingProviderType, "OpenBaoTransit", System.StringComparison.OrdinalIgnoreCase))
				builder.RegisterType<OpenBaoTransitKeyWrappingProvider>().As<IKeyWrappingProvider>().SingleInstance();
		}
	}
}
