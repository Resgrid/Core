using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	public static class CallPriorityHelper
	{
		public static string CallPriorityToString(int departmentId, int priority)
		{
			var callsService = WebBootstrapper.GetKernel().Resolve<ICallsService>();

			return callsService.CallPriorityToString(priority, departmentId);
		}

		public static string CallPriorityToColor(int departmentId, int priority)
		{
			var callsService = WebBootstrapper.GetKernel().Resolve<ICallsService>();

			return callsService.CallPriorityToColor(priority, departmentId);
		}
	}
}
