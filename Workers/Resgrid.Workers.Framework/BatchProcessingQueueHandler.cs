using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public static class BatchProcessingQueueHandler
	{
		public static BatchProcessingQueueHandler<T> For<T>(IQueue<T> queue) where T : QueueItem
		{
			return BatchProcessingQueueHandler<T>.For(queue);
		}
	}

	public class BatchProcessingQueueHandler<T> : GenericQueueHandler<T> where T : QueueItem
	{
		private readonly IQueue<T> queue;
		private TimeSpan interval;

		protected BatchProcessingQueueHandler(IQueue<T> queue)
		{
			this.queue = queue;
			this.interval = TimeSpan.FromMilliseconds(200);
		}

		public static BatchProcessingQueueHandler<T> For(IQueue<T> queue)
		{
			if (queue == null)
			{
				throw new ArgumentNullException("queue");
			}

			return new BatchProcessingQueueHandler<T>(queue);
		}

		public BatchProcessingQueueHandler<T> Every(TimeSpan intervalBetweenRuns)
		{
			this.interval = intervalBetweenRuns;

			return this;
		}

		public virtual void Do(IBatchCommand<T> batchCommand)
		{
			Task.Factory.StartNew(
					() =>
					{
						while (true)
						{
							this.Cycle(batchCommand);
						}
					},
					TaskCreationOptions.LongRunning);
		}

		protected async Task<bool> Cycle(IBatchCommand<T> batchCommand)
		{
			try
			{
				batchCommand.PreRun();

				bool continueProcessing;
				do
				{
					var messages = await this.queue.GetItems(32);
					ProcessMessages(this.queue, messages, batchCommand.Run);

					continueProcessing = messages.Count() > 0;
				}
				while (continueProcessing);

				batchCommand.PostRun();

				this.Sleep(this.interval);
			}
			catch (TimeoutException)
			{
			}

			return true;
		}
	}
}
