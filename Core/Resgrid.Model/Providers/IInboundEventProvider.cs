using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IInboundEventProvider
	{
		void RegisterForEvents(Func<int, int, Task> personnelStatusChanged, Func<int, int, Task> unitStatusChanged, Func<int, int, Task> callStatusChanged, Func<int, int, Task> personnelStaffingChanged);
	}
}
