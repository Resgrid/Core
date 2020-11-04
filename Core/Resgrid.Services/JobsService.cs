using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class JobsService : IJobsService
	{
		private readonly IJobsRepository _jobsRepository;

		public JobsService(IJobsRepository jobsRepository)
		{
			_jobsRepository = jobsRepository;
		}

		public async Task<Job> SetJobAsStartedAsync(JobTypes jobType, int checkInterval, CancellationToken cancellationToken = default(CancellationToken))
		{
			Job job = (from j in await _jobsRepository.GetAllAsync()
				      where j.JobType == (int)jobType
				      select j).FirstOrDefault();

			if (job != null)
			{
				job.DoRestart = false;
				job.StartTimestamp = DateTime.UtcNow;
				await _jobsRepository.SaveOrUpdateAsync(job, cancellationToken);
			}
			else
			{
				job = new Job();
				job.JobType = (int) jobType;
				job.CheckInterval = checkInterval;
				job.StartTimestamp = DateTime.UtcNow;

				await _jobsRepository.SaveOrUpdateAsync(job, cancellationToken);
			}

			return job;
		}

		public async Task<Job> SetJobAsCheckedAsync(JobTypes jobType, CancellationToken cancellationToken = default(CancellationToken))
		{
			Job job = (from j in await _jobsRepository.GetAllAsync()
				where j.JobType == (int)jobType
				select j).FirstOrDefault();

			if (job != null)
			{
				job.LastCheckTimestamp = DateTime.UtcNow;
				await _jobsRepository.SaveOrUpdateAsync(job, cancellationToken);
			}

			return job;
		}

		public async Task<Job> SetJobAsCheckedAsync(JobTypes jobType, DateTime timestamp, CancellationToken cancellationToken = default(CancellationToken))
		{
			Job job = (from j in await _jobsRepository.GetAllAsync()
				where j.JobType == (int)jobType
				select j).FirstOrDefault();

			if (job != null)
			{
				job.LastCheckTimestamp = timestamp;
				await _jobsRepository.SaveOrUpdateAsync(job, cancellationToken);
			}

			return job;
		}

		public async Task<List<Job>> GetAllJobsAsync()
		{
			var jobs = await _jobsRepository.GetAllAsync();
			return jobs.ToList();
		}

		public async Task<List<Job>> GetAllBatchJobsAsync()
		{
			var jobs = from j in await _jobsRepository.GetAllAsync()
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

		public async Task<Job> MarkJobForResetAsync(JobTypes jobType, CancellationToken cancellationToken = default(CancellationToken))
		{
			Job job = (from j in await _jobsRepository.GetAllAsync()
				where j.JobType == (int)jobType
				select j).FirstOrDefault();

			if (job != null)
			{
				job.DoRestart = true;
				job.RestartRequestedTimestamp = DateTime.UtcNow;

				await _jobsRepository.SaveOrUpdateAsync(job, cancellationToken);
			}

			return job;
		}

		public async Task<Job> GetJobForTypeAsync(JobTypes jobType)
		{
			Job job = (from j in await _jobsRepository.GetAllAsync()
				where j.JobType == (int)jobType
				select j).FirstOrDefault();

			return job;
		}

		public async Task<Job> MarkJobAsRestartedAsync(JobTypes jobType, CancellationToken cancellationToken = default(CancellationToken))
		{
			Job job = (from j in await _jobsRepository.GetAllAsync()
				where j.JobType == (int)jobType
				select j).FirstOrDefault();

			if (job != null)
			{
				job.DoRestart = false;
				job.LastResetTimestamp = DateTime.UtcNow;

				await _jobsRepository.SaveOrUpdateAsync(job, cancellationToken);
			}

			return job;
		}

		public async Task<bool> ResetJobsTimeStampsAsync(List<JobTypes> jobTypes, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var jobType in jobTypes)
			{
				Job job = (from j in await _jobsRepository.GetAllAsync()
					where j.JobType == (int)jobType
					select j).FirstOrDefault();

				if (job != null)
				{
					job.DoRestart = false;
					job.LastResetTimestamp = null;
					job.LastCheckTimestamp = null;
					job.StartTimestamp = null;

					await _jobsRepository.SaveOrUpdateAsync(job, cancellationToken);
				}
			}

			return true;
		}
	}
}
