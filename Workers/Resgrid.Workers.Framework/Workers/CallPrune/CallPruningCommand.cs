using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework
{
	public class CallPruningCommand : ICommand<CallPruneQueueItem>
	{
		public CallPruningCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(CallPruneQueueItem item)
		{
			var logic = new CallPruneLogic();
			logic.Process(item);
		}
	}
}