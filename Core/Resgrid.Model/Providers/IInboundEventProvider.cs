using System;

namespace Resgrid.Model.Providers
{
	public interface IInboundEventProvider
	{
		void RegisterForEvents(Action<int, int> personnelStatusChanged, Action<int, int> unitStatusChanged,
			Action<int, int> callStatusChanged, Action<int, int> personnelStaffingChanged);
	}
}