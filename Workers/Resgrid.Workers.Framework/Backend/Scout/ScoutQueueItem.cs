using Resgrid.Model;

namespace Resgrid.Workers.Framework.Backend.Scout
{
	public class ScoutQueueItem : QueueItem
	{
		public int DepartmentId { get; set; }
		public string Username { get; set; }
		public string DepartmentCode { get; set; }
	}
}