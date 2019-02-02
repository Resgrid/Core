using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework
{
	public class CallEmailCommand : ICommand<CallEmailQueueItem>
	{
		public CallEmailCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(CallEmailQueueItem item)
		{
			var logic = new CallEmailImporterLogic();
			logic.Process(item);
		}
	}
}