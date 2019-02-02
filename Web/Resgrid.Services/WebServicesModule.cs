using System.Web;
using Autofac;
using Resgrid.Web.Services.Hubs;
using Resgrid.Services.CoreWeb;

namespace Resgrid.Web.Services
{
	public class WebServicesModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<SignalRWebEventPublisher>().As<IWebEventPublisher>().SingleInstance();
		}
	}
}