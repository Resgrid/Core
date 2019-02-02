using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IJobsService
	{
		Job SetJobAsStarted(JobTypes jobType, int checkInterval);
		Job SetJobAsChecked(JobTypes jobType);
		Job MarkJobForReset(JobTypes jobType);
		List<Job> GetAllJobs();
		Job GetJobForType(JobTypes jobType);
		Job MarkJobAsRestarted(JobTypes jobType);
		List<Job> GetAllBatchJobs();
		Job SetJobAsChecked(JobTypes jobType, DateTime timestamp);
		bool DoesJobNeedReset(Job job);
		void ResetJobsTimeStamps(List<JobTypes> jobTypes);
	}
}