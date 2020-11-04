using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Backend
{
	public class GuardianCommand : ICommand<GuardianQueueItem>
	{
		private readonly IJobsService _jobsService;

		public GuardianCommand(IJobsService jobsService)
		{
			_jobsService = jobsService;

			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(GuardianQueueItem item)
		{
			if (item != null && item.Job != null)
			{
				if (item.Job.LastCheckTimestamp.HasValue && (item.Job.DoRestart.HasValue == false || item.Job.DoRestart == false))
				{
					if (item.Job.LastCheckTimestamp.Value.AddSeconds(item.Job.CheckInterval * 3.5) < DateTime.UtcNow)
					{
						await _jobsService.MarkJobForResetAsync((JobTypes)item.Job.JobType);
					}
				}
			}

			return true;
		}
	}
}
