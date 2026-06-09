using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Repositories;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Sweeps every free-form text column in the database and repairs content that would block a
	/// SQL Server -> PostgreSQL (UTF-8) migration (NUL, unpaired surrogates, Windows-1252 mojibake).
	/// Resumable via a per-table watermark, idempotent (clean values are no-ops), and bounded by
	/// configuration so a single run never scans an unbounded number of rows.
	/// </summary>
	public class Utf8CleanupLogic
	{
		private readonly IUtf8MaintenanceRepository _repository;

		public Utf8CleanupLogic()
			: this(Bootstrapper.GetKernel().Resolve<IUtf8MaintenanceRepository>())
		{
		}

		public Utf8CleanupLogic(IUtf8MaintenanceRepository repository)
		{
			_repository = repository;
		}

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			if (!SystemBehaviorConfig.Utf8CleanupEnabled)
				return new Tuple<bool, string>(true, "UTF-8 cleanup is disabled by configuration.");

			try
			{
				var batchSize = SystemBehaviorConfig.Utf8CleanupBatchSize > 0 ? SystemBehaviorConfig.Utf8CleanupBatchSize : 1000;
				var maxRows = SystemBehaviorConfig.Utf8CleanupMaxRowsPerRun; // 0 == unbounded
				var repairDoubleEncoding = SystemBehaviorConfig.Utf8RepairDoubleEncoding;
				NormalizationForm? normalization = SystemBehaviorConfig.Utf8NormalizeToNfc ? NormalizationForm.FormC : (NormalizationForm?)null;

				var targets = await _repository.GetTextColumnTargetsAsync(cancellationToken);

				long totalScanned = 0;
				long totalFixed = 0;

				foreach (var target in targets)
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					if (maxRows > 0 && totalScanned >= maxRows)
						break;

					var progress = await _repository.GetProgressAsync(target.Key, cancellationToken)
						?? new Utf8CleanupProgress { TableName = target.Key };

					var lastKey = progress.LastProcessedKey;

					while (true)
					{
						if (cancellationToken.IsCancellationRequested)
							break;

						if (maxRows > 0 && totalScanned >= maxRows)
							break;

						var batch = await _repository.GetRowBatchAsync(target, lastKey, batchSize, cancellationToken);

						if (batch.Rows.Count == 0)
						{
							MarkTableComplete(progress);
							await _repository.SaveProgressAsync(progress, cancellationToken);
							break;
						}

						var changedRows = new List<Utf8TextRow>();

						foreach (var row in batch.Rows)
						{
							Utf8TextRow changed = null;

							foreach (var column in row.Columns)
							{
								if (column.Value == null)
									continue;

								if (Utf8Sanitizer.TryClean(column.Value, out var cleaned, repairDoubleEncoding, normalization))
								{
									if (changed == null)
										changed = new Utf8TextRow { Key = row.Key };

									changed.Columns[column.Key] = cleaned;
								}
							}

							if (changed != null)
								changedRows.Add(changed);
						}

						if (changedRows.Count > 0)
						{
							var updated = await _repository.UpdateRowsAsync(target, changedRows, cancellationToken);
							totalFixed += updated;
							progress.RowsFixed += updated;
						}

						totalScanned += batch.Rows.Count;
						progress.RowsScanned += batch.Rows.Count;
						lastKey = batch.LastKey;
						progress.LastProcessedKey = lastKey;
						progress.UpdatedOnUtc = DateTime.UtcNow;
						await _repository.SaveProgressAsync(progress, cancellationToken);

						if (batch.Rows.Count < batchSize)
						{
							MarkTableComplete(progress);
							await _repository.SaveProgressAsync(progress, cancellationToken);
							break;
						}
					}
				}

				var summary = $"UTF-8 cleanup scanned {totalScanned} row(s) and repaired {totalFixed} across {targets.Count} table(s).";
				Logging.LogInfo(summary);

				return new Tuple<bool, string>(true, summary);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}

		private static void MarkTableComplete(Utf8CleanupProgress progress)
		{
			// Reset the cursor so the next scheduled run re-sweeps the table from the start
			// (catching anything written since this pass) while staying idempotent.
			progress.LastProcessedKey = null;
			progress.LastCompletedUtc = DateTime.UtcNow;
			progress.UpdatedOnUtc = DateTime.UtcNow;
		}
	}
}
