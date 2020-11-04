using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Workers.Framework.Helpers;

namespace Resgrid.Workers.Framework
{
	public static class QueueHandler
	{
		public static QueueHandler<T> For<T>(IQueue<T> queue)// where T : QueueItem
		{
			return QueueHandler<T>.For(queue);
		}
	}

	public class QueueHandler<T> : GenericQueueHandler<T>// where T : QueueItem
	{
		private readonly IQueue<T> queue;
		private TimeSpan interval;

		protected QueueHandler(IQueue<T> queue)
		{
			this.queue = queue;
			this.interval = TimeSpan.FromMilliseconds(200);
		}

		public static QueueHandler<T> For(IQueue<T> queue)
		{
			if (queue == null)
			{
				throw new ArgumentNullException("queue");
			}

			return new QueueHandler<T>(queue);
		}

		public QueueHandler<T> Every(TimeSpan intervalBetweenRuns)
		{
			this.interval = intervalBetweenRuns;

			return this;
		}

		public virtual void Do(ICommand<T> command, CancellationToken token)
		{
			Task.Factory.StartNew(
					() =>
					{
						//AppDomain.CurrentDomain.FirstChanceException += (source, e) => Resgrid.Framework.Logging.LogException(e.Exception);
						//Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

						///while (command.Continue)
						while (!token.IsCancellationRequested)
						{
							this.Cycle(command);
						}

						queue.Clear();

					}, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		}

		protected async Task<bool> Cycle(ICommand<T> command)
		{
			try
			{
				while (this.queue.IsLocked)
				{
					Sleep(new TimeSpan(0,0,0,2));
				}

				await ProcessMessages(this.queue, await this.queue.GetItems(50), command.Run);

				this.Sleep(this.interval);
			}
			catch (TimeoutException) { }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			return true;
		}
	}
}
