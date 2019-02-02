using Resgrid.Model;

namespace Resgrid.Workers.Framework.Workers.CalendarNotifier
{
	public class CalendarNotifierQueueItem : QueueItem
	{
		public CalendarItem CalendarItem { get; set; }
	}
}