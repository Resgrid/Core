using Resgrid.Model.Queue;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.MessageBroadcast
{
	public class MessageBroadcastCommand : ICommand<MessageQueueItem>
	{
		private BroadcastMessageLogic _roadcastMessageLogic;

		public MessageBroadcastCommand()
		{
			_roadcastMessageLogic = new BroadcastMessageLogic();

			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(MessageQueueItem item)
		{
			_roadcastMessageLogic.Process(item);
		}
	}
}