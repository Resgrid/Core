using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Read-only ADP sizing scan. See <see cref="IAdpSizingService"/> for the contract.
	/// Estimate = rows / benchmark_throughput + fixed per-table overhead, plus the verification
	/// allowance; P90 = P50 × the configured multiplier (plan section 18.2). Counts only — no
	/// content is read.
	/// </summary>
	public class AdpSizingService : IAdpSizingService
	{
		private readonly IDepartmentDataProtectionBulkRepository _bulkRepository;

		public AdpSizingService(IDepartmentDataProtectionBulkRepository bulkRepository)
		{
			_bulkRepository = bulkRepository;
		}

		public async Task<AdpSizingResult> RunSizingScanAsync(int departmentId, int windowMinutes,
			CancellationToken cancellationToken = default)
		{
			var result = new AdpSizingResult
			{
				DepartmentId = departmentId,
				ScannedOnUtc = DateTime.UtcNow,
				BenchmarkRowsPerSecond = Math.Max(1, Config.DataProtectionConfig.MigrationBenchmarkRowsPerSecond)
			};

			foreach (var binding in AdpTableBindings.V1)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var rows = await _bulkRepository.CountRowsAsync(binding, departmentId);
				result.TableRowCounts[binding.TableName] = rows;
				result.TotalRows += rows;
			}

			var migrationSeconds = (double)result.TotalRows / result.BenchmarkRowsPerSecond
				+ (double)AdpTableBindings.V1.Count * Math.Max(0, Config.DataProtectionConfig.MigrationEstimatePerTableOverheadSeconds);
			var p50Seconds = migrationSeconds * (1 + Math.Max(0, Config.DataProtectionConfig.MigrationEstimateVerificationAllowance));
			var p90Seconds = p50Seconds * Math.Max(1, Config.DataProtectionConfig.MigrationEstimateP90Multiplier);

			result.EstimatedP50Minutes = (int)Math.Ceiling(p50Seconds / 60);
			result.EstimatedP90Minutes = (int)Math.Ceiling(p90Seconds / 60);

			var window = Math.Max(60, windowMinutes);
			result.ProjectedNights = Math.Max(1, (int)Math.Ceiling((double)result.EstimatedP90Minutes / window));

			return result;
		}
	}
}
