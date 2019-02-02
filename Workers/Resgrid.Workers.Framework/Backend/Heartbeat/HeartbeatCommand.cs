using System;
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

		public void Run(HeartbeatQueueItem item)
		{
			_jobsService = Bootstrapper.GetKernel().Resolve<IJobsService>();
			_jobsService.SetJobAsChecked(JobTypes.Heartbeat);

			if (item != null)
			{
				_distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();

				if (item.Type == HeartbeatTypes.Worker)
				{
					dynamic dynamicData = JsonConvert.DeserializeObject(item.Data);

					_jobsService.SetJobAsChecked((JobTypes)int.Parse(dynamicData.WorkerType.ToString()), DateTime.Parse(dynamicData.TimeStamp.ToString()));
				}
				else if (item.Type == HeartbeatTypes.DListCheck)
				{
					dynamic dynamicData = JsonConvert.DeserializeObject(item.Data);

					var dlist = _distributionListsService.GetDistributionListById(int.Parse(dynamicData.ListId.ToString()));

					dlist.IsFailure = bool.Parse(dynamicData.IsFailure.ToString());
					dlist.ErrorMessage = dynamicData.ErrorMessage.ToString();
					dlist.LastCheck = DateTime.Parse(dynamicData.TimeStamp.ToString());

					_distributionListsService.SaveDistributionListOnly(dlist);
				}
			}

			_jobsService = null;
			_distributionListsService = null;
		}
	}
}