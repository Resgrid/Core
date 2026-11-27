using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 87 (Training T2, decision 9): claims queued LMS video uploads one at a time and transcodes them to HLS plus one MP4
	/// rendition with ffmpeg (ILmsVideoTranscoder). A claim a stopped worker left goes back to the queue once stale; a source that
	/// fails three times is marked Failed. It reads only the media queue: no department sweep and no billing gate.
	/// </summary>
	public class LmsMediaLogic
	{
		/// <summary>Transcodes are long; one run handles at most this many before the next schedule picks up the rest.</summary>
		public const int ItemsPerRun = 2;

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken = default)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var finished = await Process(scope.Resolve<ILmsMediaService>(), cancellationToken);
				return new Tuple<bool, string>(true, finished > 0 ? $"Finished {finished} LMS media transcode(s)" : string.Empty);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}

		/// <summary>The run itself, with the media service passed in (tests).</summary>
		public static Task<int> Process(ILmsMediaService media, CancellationToken cancellationToken = default) =>
			media.IsStoreConfigured ? media.ProcessTranscodeQueueAsync(ItemsPerRun, cancellationToken) : Task.FromResult(0);
	}

	/// <summary>The work one run of worker 88 did.</summary>
	public class LmsSweepRunResult
	{
		public int Seeded { get; set; }
		public int Reminders { get; set; }
		public int Archived { get; set; }

		/// <summary>Learners notified or re-assigned for major Academy lesson revisions (A2).</summary>
		public int AcademyUpdates { get; set; }

		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 88 (Training T2 / A1): seeds the Academy system course set when its manifest version rose, reminds learners of courses
	/// left in progress (claimed before sending, deep link to the lesson), and archives hosted media of departments whose add-on
	/// lapsed more than 90 days ago (lapse rule 6). Each step starts from a query for due work and asks the LMS gate once per
	/// department with due work (R-L9); it never sweeps every department.
	/// </summary>
	public class LmsSweepLogic
	{
		public async Task<LmsSweepRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			var result = await Process(scope.Resolve<ILmsAcademyService>(), scope.Resolve<ILmsLearnerService>(), scope.Resolve<ILmsMediaService>(), DateTime.UtcNow, cancellationToken);
			await ApplyAcademyUpdatesAsync(scope.Resolve<IAcademyManifestUpdateService>(), result, DateTime.UtcNow, cancellationToken);
			return result;
		}

		/// <summary>
		/// A2: after the seed, the policy of major Academy lesson revisions (notify or re-assign completed learners of departments that assigned
		/// the lesson), from the recorded updates only (no department sweep, no billing gate).
		/// </summary>
		public static async Task ApplyAcademyUpdatesAsync(IAcademyManifestUpdateService updates, LmsSweepRunResult result, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			result.AcademyUpdates = await Step(() => updates.ApplyPendingAsync(utcNow, cancellationToken), result, "Academy major updates", cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<LmsSweepRunResult> Process(ILmsAcademyService academy, ILmsLearnerService learner, ILmsMediaService media, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var result = new LmsSweepRunResult();
			result.Seeded = await Step(() => academy.EnsureSeededAsync(cancellationToken), result, "Academy seed", cancellationToken);
			result.Reminders = await Step(() => learner.SendInProgressRemindersAsync(utcNow, cancellationToken), result, "in-progress reminders", cancellationToken);
			result.Archived = await Step(() => media.ArchiveLapsedMediaAsync(utcNow, cancellationToken), result, "lapsed media", cancellationToken);
			return result;
		}

		private static async Task<int> Step(Func<Task<int>> work, LmsSweepRunResult result, string name, CancellationToken cancellationToken)
		{
			try
			{
				return await work();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// One failing step does not stop the others; the next run picks the work up again.
				Logging.LogException(ex, $"LMS sweep {name} failed.");
				result.Failures++;
				return 0;
			}
		}
	}
}
