using System;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Backend.Heartbeat
{
	public class HeartbeatCommand : ICommand<HeartbeatQueueItem>
	{
		private IJobsService _jobsService;
		private IDistributionListsService _distributionListsService;

		public HeartbeatCommand(/*IJobsService jobsService, IDistributionListsService distributionListsService*/)
		{
			//_jobsService = jobsService;
			//_distributionListsService = distributionListsService;

			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(HeartbeatQueueItem item)
		{
			_jobsService = Bootstrapper.GetKernel().Resolve<IJobsService>();
			await _jobsService.SetJobAsCheckedAsync(JobTypes.Heartbeat);

			if (item != null)
			{
				_distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();

				if (item.Type == HeartbeatTypes.Worker)
				{
					dynamic dynamicData = JsonConvert.DeserializeObject(item.Data);

					await _jobsService.SetJobAsCheckedAsync((JobTypes)int.Parse(dynamicData.WorkerType.ToString()), DateTime.Parse(dynamicData.TimeStamp.ToString()));
				}
				else if (item.Type == HeartbeatTypes.DListCheck)
				{
					dynamic dynamicData = JsonConvert.DeserializeObject(item.Data);

					var dlist = await _distributionListsService.GetDistributionListByIdAsync(int.Parse(dynamicData.ListId.ToString()));

					dlist.IsFailure = bool.Parse(dynamicData.IsFailure.ToString());
					dlist.ErrorMessage = dynamicData.ErrorMessage.ToString();
					dlist.LastCheck = DateTime.Parse(dynamicData.TimeStamp.ToString());

					await _distributionListsService.SaveDistributionListOnlyAsync(dlist);
				}
			}

			_jobsService = null;
			_distributionListsService = null;

			return true;
		}
	}
}
