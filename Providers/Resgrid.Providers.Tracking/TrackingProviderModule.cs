using Autofac;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Gt06;
using Resgrid.Providers.Tracking.Protocols.Queclink;
using Resgrid.Providers.Tracking.Protocols.Teltonika;

namespace Resgrid.Providers.Tracking
{
	public class TrackingProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<QueclinkProtocolModule>()
				.As<ITrackingProtocolModule>()
				.SingleInstance();
			builder.RegisterType<Gt06ProtocolModule>()
				.As<ITrackingProtocolModule>()
				.SingleInstance();
			builder.RegisterType<TeltonikaCodec8ProtocolModule>()
				.As<ITrackingProtocolModule>()
				.SingleInstance();
			builder.RegisterType<TrackingProtocolModuleRegistry>()
				.As<ITrackingProtocolModuleRegistry>()
				.SingleInstance();
		}
	}
}
