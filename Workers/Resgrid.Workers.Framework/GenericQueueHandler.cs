using System;
using System.Collections.Generic;
using System.Threading;
using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public abstract class GenericQueueHandler<T>// where T : QueueItem
	{
		protected static void ProcessMessages(IQueue<T> queue, IEnumerable<T> items, Action<T> action)
		{
			if (queue == null)
				throw new ArgumentNullException("queue");

			if (action == null)
				throw new ArgumentNullException("action");

			if (items == null)
				return;

			foreach (var item in items)
			{
				var success = false;

				try
				{
					action(item);
					success = true;
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex);
					queue.AddItem(item);
					success = false;
				}
				finally
				{
					if (success)
					{
						//queue.DeleteItem(message);
					}
				}
			}
		}

		protected virtual void Sleep(TimeSpan interval)
		{
			Thread.Sleep(interval);
		}
	}
}