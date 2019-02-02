using Resgrid.Model.Queue;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework
{
	public class CallBroadcastCommand : ICommand<CallQueueItem>
	{
		private BroadcastCallLogic _broadcastCallLogic;

		public CallBroadcastCommand()
		{
			_broadcastCallLogic = new BroadcastCallLogic();

			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(CallQueueItem item)
		{
			_broadcastCallLogic.Process(item);
		}
	}
}