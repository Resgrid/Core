using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.Maintenance
{
	public class MaintenanceCommand : ICommand<MaintenanceQueueItem>
	{
		public MaintenanceCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(MaintenanceQueueItem item)
		{
			var logic = new MaintenanceLogic();
			logic.FixMissingUserProfiles();
			logic.FixMissingUserNames();
			logic.CleanUpCallDispatchAudio();
		}
	}
}
