using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public abstract class GenericQueueHandler<T>// where T : QueueItem
	{
		protected static async Task<bool> ProcessMessages(IQueue<T> queue, IEnumerable<T> items, Func<T, Task> action)
		{
			if (queue == null)
				throw new ArgumentNullException(nameof(queue));

			if (action == null)
				throw new ArgumentNullException(nameof(action));

			if (items == null)
				return false;

			foreach (var item in items)
			{
				var success = false;

				try
				{
					await action(item);
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

			return true;
		}

		protected virtual void Sleep(TimeSpan interval)
		{
			Thread.Sleep(interval);
		}
	}
}
