using System.Threading.Tasks;
using Autofac;
using CommonServiceLocator;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	public static class CallPriorityHelper
	{
		public static async Task<string> CallPriorityToString(int departmentId, int priority)
		{
			var callsService = ServiceLocator.Current.GetInstance<ICallsService>(); // WebBootstrapper.GetKernel().Resolve<ICallsService>();

			return await callsService.CallPriorityToStringAsync(priority, departmentId);
		}

		public static async Task<string> CallPriorityToColor(int departmentId, int priority)
		{
			var callsService = ServiceLocator.Current.GetInstance<ICallsService>(); //WebBootstrapper.GetKernel().Resolve<ICallsService>();

			return await callsService.CallPriorityToColorAsync(priority, departmentId);
		}
	}
}
