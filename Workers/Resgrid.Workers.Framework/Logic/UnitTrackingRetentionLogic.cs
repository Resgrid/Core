using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	public sealed class UnitTrackingRetentionLogic
	{
		private const int DefaultBatchSize = 1000;
		private const int DefaultMaximumRowsPerRun = 100000;

		private readonly IDepartmentsRepository
			_departmentsRepository;
		private readonly IDepartmentSettingsService
			_departmentSettingsService;
		private readonly IUnitLocationRetentionRepository
			_retentionRepository;

		public UnitTrackingRetentionLogic()
			: this(
				Bootstrapper.GetKernel()
					.Resolve<IDepartmentsRepository>(),
				Bootstrapper.GetKernel()
					.Resolve<IDepartmentSettingsService>(),
				Bootstrapper.GetKernel()
					.Resolve<IUnitLocationRetentionRepository>())
		{
		}

		public UnitTrackingRetentionLogic(
			IDepartmentsRepository departmentsRepository,
			IDepartmentSettingsService departmentSettingsService,
			IUnitLocationRetentionRepository retentionRepository)
		{
			_departmentsRepository =
				departmentsRepository ??
				throw new ArgumentNullException(
					nameof(departmentsRepository));
			_departmentSettingsService =
				departmentSettingsService ??
				throw new ArgumentNullException(
					nameof(departmentSettingsService));
			_retentionRepository =
				retentionRepository ??
				throw new ArgumentNullException(
					nameof(retentionRepository));
		}

		public async Task<Tuple<bool, string>> Process(
			CancellationToken cancellationToken,
			DateTime? utcNow = null)
		{
			if (!UnitTrackingConfig
				    .LocationRetentionWorkerEnabled)
			{
				return new Tuple<bool, string>(
					true,
					"Unit tracking location retention is disabled by configuration.");
			}

			try
			{
				var runUtc = utcNow ?? DateTime.UtcNow;
				if (runUtc.Kind != DateTimeKind.Utc)
				{
					throw new ArgumentException(
						"The retention run timestamp must be UTC.",
						nameof(utcNow));
				}

				var batchSize =
					UnitTrackingConfig
						.LocationRetentionBatchSize > 0
						? UnitTrackingConfig
							.LocationRetentionBatchSize
						: DefaultBatchSize;
				var maximumRows =
					UnitTrackingConfig
						.LocationRetentionMaxRowsPerRun > 0
						? UnitTrackingConfig
							.LocationRetentionMaxRowsPerRun
						: DefaultMaximumRowsPerRun;
				var departments = await _departmentsRepository
					.GetAllAsync()
					.WaitAsync(cancellationToken);
				var departmentIds = departments?
					.Where(
						department =>
							department != null &&
							department.DepartmentId > 0)
					.Select(
						department =>
							department.DepartmentId)
					.Distinct()
					.OrderBy(
						departmentId =>
							departmentId)
					.ToList() ??
					new System.Collections.Generic.List<int>();

				var totalDeleted = 0;
				var processedDepartments = 0;
				foreach (var departmentId in departmentIds)
				{
					cancellationToken
						.ThrowIfCancellationRequested();
					if (totalDeleted >= maximumRows)
						break;

					var retentionDays =
						await _departmentSettingsService
							.GetHardwareTrackingLocationRetentionDaysAsync(
								departmentId)
							.WaitAsync(cancellationToken);
					var cutoffUtc =
						runUtc.AddDays(-retentionDays);
					processedDepartments++;

					while (totalDeleted < maximumRows)
					{
						cancellationToken
							.ThrowIfCancellationRequested();
						var requestedBatchSize = Math.Min(
							batchSize,
							maximumRows - totalDeleted);
						var deleted =
							await _retentionRepository
								.DeleteHardwareLocationsBeforeAsync(
									departmentId,
									cutoffUtc,
									requestedBatchSize,
									cancellationToken);
						if (deleted < 0 ||
						    deleted > requestedBatchSize)
						{
							throw new InvalidOperationException(
								"The unit-location retention repository returned an invalid deletion count.");
						}

						totalDeleted += deleted;
						if (deleted < requestedBatchSize)
							break;
					}
				}

				var summary =
					$"Unit tracking retention deleted {totalDeleted} hardware location(s) across {processedDepartments} department(s).";
				Logging.LogInfo(summary);
				return new Tuple<bool, string>(
					true,
					summary);
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(
					false,
					ex.ToString());
			}
		}
	}
}
