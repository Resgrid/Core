using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Web.Services.App_Start;
using System;
using System.Threading.Tasks;
using System.Web;
using Resgrid.Providers.Cache;

namespace Resgrid.Web.Services.ApplicationCore
{

	public class SignalRAuthenticaitonHttpModule : IHttpModule
	{
		public void Dispose()
		{
		}

		public void Init(HttpApplication context)
		{
			// It wraps the Task-based method
			EventHandlerTaskAsyncHelper asyncHelper =
			   new EventHandlerTaskAsyncHelper(AuthRequest);

			//asyncHelper's BeginEventHandler and EndEventHandler eventhandler that is used
			//as Begin and End methods for Asynchronous HTTP modules
			context.AddOnPostAuthorizeRequestAsync(
				asyncHelper.BeginEventHandler,
				asyncHelper.EndEventHandler);

		}

		private async Task AuthRequest(object sender, EventArgs e)
		{
			var context = ((HttpApplication)sender).Context;
			if (context.Request.AppRelativeCurrentExecutionFilePath.StartsWith("~/signalR-webEvents", StringComparison.OrdinalIgnoreCase))
			{
				await Task.Factory.StartNew(() =>
				{
					//var y = (IDepartmentsService)WebApiApplication.Kernel.GetService(typeof(IDepartmentsService));
					var y = new DepartmentsRepository(new DataContext(), new StandardIsolation());
					var z = new AzureRedisCacheProvider();
					AuthTokenMessageHandler.AuthAndSetPrinciple(z, y, context.Request.Headers, new HttpContextWrapper(context));
				});
			}

			return;
		}
	}
}