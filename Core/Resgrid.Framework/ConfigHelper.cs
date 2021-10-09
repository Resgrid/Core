using Resgrid.Config;

namespace Resgrid.Framework
{
	public static class ConfigHelper
	{
		public static bool CanTransmit(int departmentId)
		{
			if (SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(departmentId))
				return true;

			return !SystemBehaviorConfig.DoNotBroadcast;
		}
	}
}
