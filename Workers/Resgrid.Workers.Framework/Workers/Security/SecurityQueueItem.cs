using Resgrid.Model;

namespace Resgrid.Workers.Framework.Workers.Security
{
	public class SecurityQueueItem : QueueItem
	{
		public SecurityCacheTypes Type { get; set; }
		public int DepartmentId { get; set; }
	}
}
