using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Firebase
{
	public class FirebaseProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<FirebaseAuthProvider>().As<IFirebaseAuthProvider>().SingleInstance();
		}
	}
}
