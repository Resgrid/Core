using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// Registers the NERIS boundary (RMS plan section 5.5): profile/value sets/crosswalks, pure mapping, local +
	/// remote validation, the HTTP client, and submission orchestration. Loaded after ServicesModule in every
	/// process, like the other Resgrid.Providers.* modules.
	/// </summary>
	public class NerisProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<NerisProfileService>().As<INerisProfileService>().InstancePerLifetimeScope();
			builder.RegisterType<NerisMappingService>().As<INerisMappingService>().SingleInstance();
			builder.RegisterType<NerisValidationService>().As<INerisValidationService>().InstancePerLifetimeScope();
			builder.Register(c => new NerisApiClient()).As<INerisApiClient>().SingleInstance();
			builder.RegisterType<NerisSubmissionService>().As<INerisSubmissionService>().InstancePerLifetimeScope();
		}
	}
}
