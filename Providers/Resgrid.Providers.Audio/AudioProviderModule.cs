using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Audio
{
	public class AudioProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<AudioValidatorProvider>().As<IAudioValidatorProvider>().SingleInstance();
		}
	}
}