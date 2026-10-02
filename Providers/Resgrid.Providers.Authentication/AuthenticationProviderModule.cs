using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Authentication
{
	/// <summary>
	/// Registers the WebAuthn protocol adapter and the brokered-SSO OIDC client. Load it in the hosts that run passkey
	/// ceremonies and SSO (Web and API); it holds no secrets, only each client's relying-party configuration.
	/// </summary>
	public class AuthenticationProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<Fido2PasskeyProvider>().As<IPasskeyProvider>().SingleInstance();
			builder.RegisterType<OidcProviderClient>().As<IOidcProviderClient>().SingleInstance();
		}
	}
}
