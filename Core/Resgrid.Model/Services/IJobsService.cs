using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IJobsService
	{
		/// <summary>
		/// Sets the job as started asynchronous.
		/// </summary>
		/// <param name="jobType">Type of the job.</param>
		/// <param name="checkInterval">The check interval.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Job&gt;.</returns>
		Task<Job> SetJobAsStartedAsync(JobTypes jobType, int checkInterval, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the job as checked asynchronous.
		/// </summary>
		/// <param name="jobType">Type of the job.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Job&gt;.</returns>
		Task<Job> SetJobAsCheckedAsync(JobTypes jobType, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Marks the job for reset asynchronous.
		/// </summary>
		/// <param name="jobType">Type of the job.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Job&gt;.</returns>
		Task<Job> MarkJobForResetAsync(JobTypes jobType, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all jobs asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;Job&gt;&gt;.</returns>
		Task<List<Job>> GetAllJobsAsync();

		/// <summary>
		/// Gets the job for type asynchronous.
		/// </summary>
		/// <param name="jobType">Type of the job.</param>
		/// <returns>Task&lt;Job&gt;.</returns>
		Task<Job> GetJobForTypeAsync(JobTypes jobType);

		/// <summary>
		/// Marks the job as restarted asynchronous.
		/// </summary>
		/// <param name="jobType">Type of the job.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Job&gt;.</returns>
		Task<Job> MarkJobAsRestartedAsync(JobTypes jobType, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the job as checked asynchronous.
		/// </summary>
		/// <param name="jobType">Type of the job.</param>
		/// <param name="timestamp">The timestamp.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Job&gt;.</returns>
		Task<Job> SetJobAsCheckedAsync(JobTypes jobType, DateTime timestamp, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all batch jobs asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;Job&gt;&gt;.</returns>
		Task<List<Job>> GetAllBatchJobsAsync();

		/// <summary>
		/// Doeses the job need reset.
		/// </summary>
		/// <param name="job">The job.</param>
		/// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
		bool DoesJobNeedReset(Job job);

		/// <summary>
		/// Resets the jobs time stamps asynchronous.
		/// </summary>
		/// <param name="jobTypes">The job types.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ResetJobsTimeStampsAsync(List<JobTypes> jobTypes, CancellationToken cancellationToken = default(CancellationToken));
	}
}
