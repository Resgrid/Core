using System.Threading.Tasks;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.ShiftNotifier
{
	public class ShiftNotifierCommand : ICommand<ShiftNotifierQueueItem>
	{
		public ShiftNotifierCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(ShiftNotifierQueueItem item)
		{
			var logic = new ShiftNotifierLogic();
			await logic.Process(item);
			return true;
		}
	}
}
