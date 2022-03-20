using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IRabbitInboundEventProvider
	{
		void RegisterForEvents(Func<int, int, Task> personnelStatusChanged, Func<int, int, Task> unitStatusChanged,
							  Func<int, int, Task> callStatusChanged, Func<int, int, Task> personnelStaffingChanged,
							  Func<int, int, Task> callAdded, Func<int, int, Task> callClosed);
	}
}
