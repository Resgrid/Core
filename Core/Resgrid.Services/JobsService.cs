using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class JobsService : IJobsService
	{
		private readonly IGenericDataRepository<Job> _jobsRepository;

		public JobsService(IGenericDataRepository<Job> jobsRepository)
		{
			_jobsRepository = jobsRepository;
		}

		public Job SetJobAsStarted(JobTypes jobType, int checkInterval)
		{
			Job job = _jobsRepository.GetAll().FirstOrDefault(x => x.JobType == (int)jobType);

			if (job != null)
			{
				job.DoRestart = false;
				job.StartTimestamp = DateTime.UtcNow;
				_jobsRepository.SaveOrUpdate(job);
			}
			else
			{
				job = new Job();
				job.JobType = (int) jobType;
				job.CheckInterval = checkInterval;
				job.StartTimestamp = DateTime.UtcNow;

				_jobsRepository.SaveOrUpdate(job);
			}

			return job;
		}

		public Job SetJobAsChecked(JobTypes jobType)
		{
			Job job = _jobsRepository.GetAll().FirstOrDefault(x => x.JobType == (int)jobType);

			if (job != null)
			{
				job.LastCheckTimestamp = DateTime.UtcNow;
				_jobsRepository.SaveOrUpdate(job);
			}

			return job;
		}

		public Job SetJobAsChecked(JobTypes jobType, DateTime timestamp)
		{
			Job job = _jobsRepository.GetAll().FirstOrDefault(x => x.JobType == (int)jobType);

			if (job != null)
			{
				job.LastCheckTimestamp = timestamp;
				_jobsRepository.SaveOrUpdate(job);
			}

			return job;
		}

		public List<Job> GetAllJobs()
		{
			return _jobsRepository.GetAll().ToList();
		}

		public List<Job> GetAllBatchJobs()
		{
			var jobs = from j in _jobsRepository.GetAll()
				where j.JobType == (int) JobTypes.Broadcast || j.JobType == (int) JobTypes.CallEmail ||
				      j.JobType == (int) JobTypes.CallPrune || j.JobType == (int) JobTypes.DistributionList ||
					  j.JobType == (int)JobTypes.StaffingChange || j.JobType == (int)JobTypes.DistributionList ||
					  j.JobType == (int)JobTypes.MessageBroadcast || j.JobType == (int)JobTypes.ReportDelivery ||
					  j.JobType == (int)JobTypes.Notification || j.JobType == (int)JobTypes.ShiftNotifier
				select j;

			return jobs.ToList();
		}

		public bool DoesJobNeedReset(Job job)
		{
			if (job.LastCheckTimestamp.HasValue && job.LastCheckTimestamp.Value.AddSeconds(job.CheckInterval * 3.5) < DateTime.UtcNow)
				return true;

			return false;
		}

		public Job MarkJobForReset(JobTypes jobType)
		{
			Job job = _jobsRepository.GetAll().FirstOrDefault(x => x.JobType == (int)jobType);

			if (job != null)
			{
				job.DoRestart = true;
				job.RestartRequestedTimestamp = DateTime.UtcNow;

				_jobsRepository.SaveOrUpdate(job);
			}

			return job;
		}

		public Job GetJobForType(JobTypes jobType)
		{
			return _jobsRepository.GetAll().FirstOrDefault(x => x.JobType == (int)jobType);
		}

		public Job MarkJobAsRestarted(JobTypes jobType)
		{
			Job job = _jobsRepository.GetAll().FirstOrDefault(x => x.JobType == (int)jobType);

			if (job != null)
			{
				job.DoRestart = false;
				job.LastResetTimestamp = DateTime.UtcNow;

				_jobsRepository.SaveOrUpdate(job);
			}

			return job;
		}

		public void ResetJobsTimeStamps(List<JobTypes> jobTypes)
		{
			foreach (var jobType in jobTypes)
			{
				Job job = _jobsRepository.GetAll().FirstOrDefault(x => x.JobType == (int) jobType);

				if (job != null)
				{
					job.DoRestart = false;
					job.LastResetTimestamp = null;
					job.LastCheckTimestamp = null;
					job.StartTimestamp = null;

					_jobsRepository.SaveOrUpdate(job);
				}
			}
		}
	}
}