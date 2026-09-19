using Autofac;
using Resgrid.Config;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Payments
{
	/// <summary>
	/// Registers the department payment-provider adapters (Workforce &amp; Business Operations plan, B2.3). v1 is Stripe
	/// Connect only. When PaymentConnectConfig.Enabled is false (the EU cluster at launch) the null adapter is served
	/// instead, so no connection, request or webhook can ever reach a provider from that process. The choice is made
	/// at resolve time so configuration loaded after container build still counts.
	/// </summary>
	public class PaymentsProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.Register(c => PaymentConnectConfig.Enabled
					? (IPaymentConnectProvider)new StripeConnectPaymentProvider()
					: new NullPaymentConnectProvider())
				.As<IPaymentConnectProvider>()
				.InstancePerLifetimeScope();
		}
	}
}
