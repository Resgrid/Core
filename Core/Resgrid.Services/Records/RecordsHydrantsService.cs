using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>Hydrants and other water sources (RMS plan section 4.3, RMS-5): flow/test history, maintenance, in/out-of-service state, JSON/CSV import and the response-map layer.</summary>
	public class RecordsHydrantsService : IRecordsHydrantsService
	{
		public const int TestIntervalMonths = 12;
		/// <summary>Import rows per batch: one number lookup and one transaction each (matches the repository's lookup chunk).</summary>
		public const int ImportBatchSize = 1000;

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsHydrantsRepository _hydrants;
		private readonly IRmsHydrantFlowTestsRepository _flowTests;
		private readonly IRmsHydrantMaintenancesRepository _maintenance;
		private readonly IRmsPreventionAttachmentsRepository _attachments;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsHydrantsService(RecordsPreventionGate gate, IRmsHydrantsRepository hydrants, IRmsHydrantFlowTestsRepository flowTests, IRmsHydrantMaintenancesRepository maintenance, IRmsPreventionAttachmentsRepository attachments,
			IUnitOfWork unitOfWork)
		{
			_gate = gate; _hydrants = hydrants; _flowTests = flowTests; _maintenance = maintenance; _attachments = attachments; _unitOfWork = unitOfWork;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Hydrants);

		private async Task RequireViewAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Hydrants); await _gate.RequireViewerAsync(departmentId, userId); }
		private async Task RequireAdminAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Hydrants); await _gate.RequireAdminAsync(departmentId, userId); }

		public async Task<List<RmsHydrant>> ListAsync(int departmentId, string userId)
		{
			await RequireViewAsync(departmentId, userId);
			return (await _hydrants.GetAllLiveAsync(departmentId))?.ToList() ?? new List<RmsHydrant>();
		}

		public async Task<HydrantAggregate> GetAsync(int departmentId, string userId, string hydrantId)
		{
			await RequireViewAsync(departmentId, userId);
			var hydrant = await LiveAsync(departmentId, hydrantId);
			if (hydrant == null) return null;
			return new HydrantAggregate
			{
				Hydrant = hydrant,
				FlowTests = (await _flowTests.GetForHydrantAsync(departmentId, hydrantId))?.ToList() ?? new List<RmsHydrantFlowTest>(),
				Maintenance = (await _maintenance.GetForHydrantAsync(departmentId, hydrantId))?.ToList() ?? new List<RmsHydrantMaintenance>(),
				Attachments = (await _attachments.GetMetadataForParentAsync(departmentId, RmsPreventionParentKind.Hydrant, hydrantId))?.ToList() ?? new List<RmsPreventionAttachment>()
			};
		}

		public async Task<RmsHydrant> SaveAsync(int departmentId, string userId, RmsHydrant input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var entity = await UpsertAsync(departmentId, userId, input, "manual", cancellationToken);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Hydrant saved", entity.RmsHydrantId, new { entity.HydrantNumber }, cancellationToken: cancellationToken);
			return entity;
		}

		/// <summary>Service-state changes an import made; merged into the result only once the writes that made them have succeeded.</summary>
		private sealed class ServiceTally
		{
			public int TakenOut;
			public int Returned;
			public void AddTo(HydrantImportResult result) { result.TakenOutOfService += TakenOut; result.ReturnedToService += Returned; }
		}

		/// <param name="byNumber">Import batches pass the batch lookup (<see cref="IRmsHydrantsRepository.GetByNumbersAsync"/>, keyed by the
		/// normalized number); null falls back to the single-row lookup.</param>
		/// <param name="serviceState">Import only: the state another system's status column gives (null = leave as it is), with
		/// <paramref name="input"/>.OutOfServiceReason as the reason when false.</param>
		private async Task<RmsHydrant> UpsertAsync(int departmentId, string userId, RmsHydrant input, string source, CancellationToken cancellationToken, IReadOnlyDictionary<string, RmsHydrant> byNumber = null,
			bool? serviceState = null, ServiceTally tally = null)
		{
			var now = DateTime.UtcNow;
			var number = RecordsPreventionGate.Require(input.HydrantNumber, 64, "A hydrant needs a number.");
			if (input.Latitude < -90 || input.Latitude > 90 || input.Longitude < -180 || input.Longitude > 180) throw new ArgumentException("Hydrant coordinates are out of range.");
			var existing = !string.IsNullOrWhiteSpace(input.RmsHydrantId) ? await _hydrants.GetByIdForDepartmentAsync(departmentId, input.RmsHydrantId)
				: byNumber != null ? (byNumber.TryGetValue(number, out var match) ? match : null)
				: await _hydrants.GetByNumberAsync(departmentId, number);
			if (existing != null && existing.DeletedOn != null) existing = null;
			var entity = existing ?? new RmsHydrant { RmsHydrantId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), InService = true, CreatedOn = now, CreatedByUserId = userId, RowVersion = 0, Source = source };
			if (existing != null && !string.Equals(existing.HydrantNumber, number, StringComparison.OrdinalIgnoreCase))
			{
				var clash = await _hydrants.GetByNumberAsync(departmentId, number);
				if (clash != null && clash.RmsHydrantId != existing.RmsHydrantId) throw new ArgumentException($"Hydrant number {number} is already in use.");
			}
			entity.HydrantNumber = number; entity.Type = input.Type == 0 ? (int)RmsHydrantType.DryBarrel : input.Type; entity.Latitude = input.Latitude; entity.Longitude = input.Longitude;
			entity.AddressText = RecordsPreventionGate.Trim(input.AddressText, 500); entity.OwnerKind = input.OwnerKind == 0 ? (int)RmsHydrantOwnerKind.Municipal : input.OwnerKind; entity.OwnerName = RecordsPreventionGate.Trim(input.OwnerName, 200);
			entity.MainSizeInches = input.MainSizeInches; if (source == "manual") { entity.Notes = RecordsPreventionGate.Trim(input.Notes, 4000); entity.PoiId = input.PoiId; }
			if (input.FlowGpm.HasValue) { entity.FlowGpm = input.FlowGpm; entity.FlowClass = (int)HydrantFlowCalculator.Classify(input.FlowGpm); }
			// An import's flow class (another system's cap colour) applies only while there is no flow to classify: a known flow decides the class.
			if (source != "manual" && Enum.IsDefined(typeof(RmsHydrantFlowClass), input.FlowClass) && input.FlowClass != (int)RmsHydrantFlowClass.Unknown
				&& HydrantFlowCalculator.Classify(entity.FlowGpm) == RmsHydrantFlowClass.Unknown)
				entity.FlowClass = input.FlowClass;
			if (input.StaticPressurePsi.HasValue) entity.StaticPressurePsi = input.StaticPressurePsi;
			if (input.ResidualPressurePsi.HasValue) entity.ResidualPressurePsi = input.ResidualPressurePsi;
			// A change of state is applied as SetServiceStateAsync would; a hydrant already out of service keeps its own reason and date.
			// Deliberately both ways (product decision 2026-11-13): the file is the source of truth for the state it carries, so an older
			// export can return a hydrant to service. The result's TakenOutOfService/ReturnedToService counts and the audit make that visible.
			if (serviceState == true && !entity.InService)
			{
				entity.InService = true; entity.OutOfServiceReason = null; entity.OutOfServiceSince = null;
				if (tally != null) tally.Returned++;
			}
			else if (serviceState == false && entity.InService)
			{
				entity.InService = false; entity.OutOfServiceReason = RecordsPreventionGate.Trim(input.OutOfServiceReason, 500); entity.OutOfServiceSince = now;
				if (tally != null) tally.TakenOut++;
			}
			entity.ModifiedOn = now; entity.RowVersion++;
			if (existing == null) await _hydrants.InsertAsync(entity, cancellationToken, true); else await _hydrants.UpdateAsync(entity, cancellationToken, true);
			return entity;
		}

		public async Task DeleteAsync(int departmentId, string userId, string hydrantId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var hydrant = await LiveAsync(departmentId, hydrantId) ?? throw new ArgumentException("The hydrant does not exist.");
			hydrant.DeletedOn = DateTime.UtcNow; hydrant.ModifiedOn = hydrant.DeletedOn.Value; hydrant.RowVersion++;
			await _hydrants.UpdateAsync(hydrant, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Hydrant removed", hydrantId, new { hydrant.HydrantNumber }, cancellationToken: cancellationToken);
		}

		/// <summary>Most ids one Selected removal accepts; matches the largest import (<see cref="HydrantImportParser.MaximumRows"/>).</summary>
		public const int MaxBulkDeleteSelection = HydrantImportParser.MaximumRows;

		public async Task<int> BulkDeleteAsync(int departmentId, string userId, HydrantBulkDeleteScope scope, IEnumerable<string> hydrantIds, int? expectedCount, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (!Enum.IsDefined(typeof(HydrantBulkDeleteScope), scope)) throw new ArgumentException("Choose which hydrants to remove.");
			List<string> ids = null;
			if (scope == HydrantBulkDeleteScope.Selected)
			{
				ids = (hydrantIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
				if (ids.Count == 0) throw new ArgumentException("Select the hydrants to remove.");
				if (ids.Count > MaxBulkDeleteSelection) throw new ArgumentException($"Select at most {MaxBulkDeleteSelection:N0} hydrants at a time, or remove all imported hydrants instead.");
			}
			// A department-wide removal names no rows, so the caller confirms how many it expects to remove; a stale page or a
			// concurrent import then refuses instead of removing hydrants nobody saw.
			else if (expectedCount == null) throw new ArgumentException("Confirm how many hydrants will be removed.");

			var matching = await _hydrants.CountForBulkDeleteAsync(departmentId, scope, ids);
			if (expectedCount.HasValue && expectedCount.Value != matching) throw BulkCountMismatch(matching, expectedCount.Value);
			if (matching == 0) return 0;

			// The count above refuses a stale page without writing. The UPDATE's own row count is the binding check: a hydrant imported
			// (or removed) since then makes it differ and the removal rolls back. The audit row commits with the removal or not at all.
			var owns = _unitOfWork.Transaction == null;
			await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				var removed = await _hydrants.SoftDeleteBulkAsync(departmentId, scope, ids, DateTime.UtcNow, cancellationToken);
				if (expectedCount.HasValue && removed != expectedCount.Value) throw BulkCountMismatch(removed, expectedCount.Value);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Hydrants removed in bulk", departmentId.ToString(), new { scope = scope.ToString(), removed }, cancellationToken: cancellationToken);
				if (owns) _unitOfWork.CommitChanges();
				return removed;
			}
			catch
			{
				if (owns) _unitOfWork.DiscardChanges();
				throw;
			}
		}

		private static ArgumentException BulkCountMismatch(int matching, int expected)
			=> new ArgumentException($"{matching:N0} hydrants match this removal, not {expected:N0}. Reload the hydrant list and confirm again.");

		public async Task<RmsHydrant> SetServiceStateAsync(int departmentId, string userId, string hydrantId, bool inService, string reason, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var hydrant = await LiveAsync(departmentId, hydrantId) ?? throw new ArgumentException("The hydrant does not exist.");
			if (!inService && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Taking a hydrant out of service needs a reason.");
			var now = DateTime.UtcNow;
			hydrant.InService = inService; hydrant.OutOfServiceReason = inService ? null : RecordsPreventionGate.Trim(reason, 500); hydrant.OutOfServiceSince = inService ? null : now;
			hydrant.ModifiedOn = now; hydrant.RowVersion++;
			await _hydrants.UpdateAsync(hydrant, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, inService ? "Hydrant returned to service" : "Hydrant out of service", hydrantId, new { hydrant.HydrantNumber, reason = hydrant.OutOfServiceReason }, cancellationToken: cancellationToken);
			return hydrant;
		}

		public async Task<RmsHydrantFlowTest> RecordFlowTestAsync(int departmentId, string userId, RmsHydrantFlowTest input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var hydrant = await LiveAsync(departmentId, input.RmsHydrantId) ?? throw new ArgumentException("The hydrant does not exist.");
			if (input.PitotPressurePsi <= 0 || input.OutletDiameterInches <= 0) throw new ArgumentException("A flow test needs the pitot pressure and the outlet diameter.");
			var now = DateTime.UtcNow;
			var coefficient = input.Coefficient <= 0 ? 0.9m : input.Coefficient;
			var test = new RmsHydrantFlowTest
			{
				RmsHydrantFlowTestId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsHydrantId = hydrant.RmsHydrantId,
				TestedOn = RecordsPreventionGate.RequireStorableDate(input.TestedOn == default ? now : input.TestedOn, "The test date is not valid."), TestedByUserId = userId, StaticPressurePsi = input.StaticPressurePsi, ResidualPressurePsi = input.ResidualPressurePsi,
				PitotPressurePsi = input.PitotPressurePsi, OutletDiameterInches = input.OutletDiameterInches, Coefficient = coefficient, Notes = RecordsPreventionGate.Trim(input.Notes, 2000), CreatedOn = now
			};
			test.FlowGpm = HydrantFlowCalculator.FlowGpm(coefficient, input.OutletDiameterInches, input.PitotPressurePsi);
			test.FlowClass = (int)HydrantFlowCalculator.Classify(test.FlowGpm);
			await _flowTests.InsertAsync(test, cancellationToken, true);
			if (!hydrant.LastTestedOn.HasValue || hydrant.LastTestedOn <= test.TestedOn)
			{
				hydrant.LastTestedOn = test.TestedOn; hydrant.FlowGpm = test.FlowGpm; hydrant.FlowClass = test.FlowClass; hydrant.StaticPressurePsi = test.StaticPressurePsi; hydrant.ResidualPressurePsi = test.ResidualPressurePsi;
				hydrant.ModifiedOn = now; hydrant.RowVersion++;
				await _hydrants.UpdateAsync(hydrant, cancellationToken, true);
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Hydrant flow test recorded", hydrant.RmsHydrantId, new { test.FlowGpm, flow_class = ((RmsHydrantFlowClass)test.FlowClass).ToString() }, cancellationToken: cancellationToken);
			return test;
		}

		public async Task<RmsHydrantMaintenance> RecordMaintenanceAsync(int departmentId, string userId, RmsHydrantMaintenance input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var hydrant = await LiveAsync(departmentId, input.RmsHydrantId) ?? throw new ArgumentException("The hydrant does not exist.");
			var now = DateTime.UtcNow;
			var row = new RmsHydrantMaintenance
			{
				RmsHydrantMaintenanceId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsHydrantId = hydrant.RmsHydrantId,
				PerformedOn = RecordsPreventionGate.RequireStorableDate(input.PerformedOn == default ? now : input.PerformedOn, "The maintenance date is not valid."), PerformedByUserId = userId, Kind = input.Kind == 0 ? (int)RmsHydrantMaintenanceKind.Inspection : input.Kind,
				Notes = RecordsPreventionGate.Trim(input.Notes, 2000), ReturnedToService = input.ReturnedToService, CreatedOn = now
			};
			await _maintenance.InsertAsync(row, cancellationToken, true);
			hydrant.LastMaintainedOn = row.PerformedOn;
			if (row.ReturnedToService && !hydrant.InService) { hydrant.InService = true; hydrant.OutOfServiceReason = null; hydrant.OutOfServiceSince = null; }
			hydrant.ModifiedOn = now; hydrant.RowVersion++;
			await _hydrants.UpdateAsync(hydrant, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Hydrant maintenance recorded", hydrant.RmsHydrantId, new { kind = ((RmsHydrantMaintenanceKind)row.Kind).ToString(), row.ReturnedToService }, cancellationToken: cancellationToken);
			return row;
		}

		public Task<HydrantImportResult> ImportCsvAsync(int departmentId, string userId, string csv, CancellationToken cancellationToken = default)
			=> ImportAsync(departmentId, userId, csv, "csv", cancellationToken);

		public async Task<HydrantImportResult> ImportAsync(int departmentId, string userId, string content, string format, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var batch = HydrantImportParser.Parse(content, format);
			var result = batch.Result;
			if (result.ValidationFailed) return result;
			var existingNumbers = ((await _hydrants.GetAllLiveAsync(departmentId)) ?? Enumerable.Empty<RmsHydrant>()).Select(h => h.HydrantNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var source = format.ToLowerInvariant() + "-import";
			// Each batch is one number lookup plus one transaction instead of a lookup and an autocommitted write per row. A batch that
			// fails is rolled back and replayed through the original row-by-row path, so the row the import stops at, the partial
			// counts and the failure message are exactly those of a row-by-row import.
			foreach (var chunk in batch.Rows.Chunk(ImportBatchSize))
			{
				var batchTally = new ServiceTally();
				if (await TryImportBatchAsync(departmentId, userId, chunk, source, batchTally, cancellationToken))
				{
					foreach (var item in chunk)
						if (existingNumbers.Add(item.Hydrant.HydrantNumber)) result.Created++; else result.Updated++;
					batchTally.AddTo(result);
					continue;
				}
				var stopped = false;
				foreach (var item in chunk)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						var rowTally = new ServiceTally();
						await UpsertAsync(departmentId, userId, item.Hydrant, source, cancellationToken, null, item.InService, rowTally);
						if (existingNumbers.Add(item.Hydrant.HydrantNumber)) result.Created++; else result.Updated++;
						rowTally.AddTo(result);
					}
					catch (OperationCanceledException) { throw; }
					catch (Exception ex)
					{
						Resgrid.Framework.Logging.LogException(ex, "Hydrant import save failed");
						result.FailureMessage = $"Import stopped at row {item.Row.Line} ({item.Row.HydrantNumber}). {result.Created} created and {result.Updated} updated before the failure. Check the hydrant list, then retry the file; matching numbers are updated. If the issue continues, contact your administrator.";
						stopped = true;
						break;
					}
				}
				if (stopped) break;
			}
			try
			{
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "Hydrants imported", departmentId.ToString(), new { result.RowsRead, result.Created, result.Updated, result.TakenOutOfService, result.ReturnedToService, result.FailureMessage }, cancellationToken: cancellationToken);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Hydrant import audit failed");
				result.FailureMessage = (result.FailureMessage ?? "Hydrant saves completed.") + " The import activity could not be recorded. Review the saved hydrants and contact your administrator.";
			}
			return result;
		}

		/// <summary>Writes one import batch in a single transaction. False (rolled back) when any row fails; cancellation rolls back and rethrows.</summary>
		private async Task<bool> TryImportBatchAsync(int departmentId, string userId, (HydrantImportRow Row, RmsHydrant Hydrant, bool? InService)[] chunk, string source, ServiceTally tally, CancellationToken cancellationToken)
		{
			// Inside an ambient transaction a failed batch could not be rolled back on its own to replay; use the row-by-row path.
			if (_unitOfWork == null || _unitOfWork.Transaction != null) return false;
			try
			{
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
				// Keys are the normalized numbers UpsertAsync looks up; a row with an invalid number has none and fails in UpsertAsync.
				var numbers = chunk.Where(i => string.IsNullOrWhiteSpace(i.Hydrant.RmsHydrantId) && !string.IsNullOrWhiteSpace(i.Hydrant.HydrantNumber))
					.Select(i => i.Hydrant.HydrantNumber.Trim()).ToList();
				var byNumber = await _hydrants.GetByNumbersAsync(departmentId, numbers);
				foreach (var item in chunk)
				{
					cancellationToken.ThrowIfCancellationRequested();
					await UpsertAsync(departmentId, userId, item.Hydrant, source, cancellationToken, byNumber, item.InService, tally);
				}
				_unitOfWork.CommitChanges();
				return true;
			}
			catch (OperationCanceledException) { _unitOfWork.DiscardChanges(); throw; }
			catch (Exception ex)
			{
				_unitOfWork.DiscardChanges();
				Resgrid.Framework.Logging.LogError($"Hydrant import batch rolled back and replayed row by row ({ex.GetType().Name}).");
				return false;
			}
		}

		public async Task<List<HydrantMapPoint>> GetMapLayerAsync(int departmentId, string userId, decimal? minLat, decimal? maxLat, decimal? minLon, decimal? maxLon)
		{
			await RequireViewAsync(departmentId, userId);
			IEnumerable<RmsHydrant> rows = minLat.HasValue && maxLat.HasValue && minLon.HasValue && maxLon.HasValue
				? await _hydrants.GetInBoundsAsync(departmentId, Math.Min(minLat.Value, maxLat.Value), Math.Max(minLat.Value, maxLat.Value), Math.Min(minLon.Value, maxLon.Value), Math.Max(minLon.Value, maxLon.Value), 5000)
				: await _hydrants.GetAllLiveAsync(departmentId);
			return (rows ?? Enumerable.Empty<RmsHydrant>()).Select(h => new HydrantMapPoint { HydrantId = h.RmsHydrantId, PoiId = h.PoiId, HydrantNumber = h.HydrantNumber, Type = h.Type, Latitude = h.Latitude, Longitude = h.Longitude, FlowClass = h.FlowClass, FlowGpm = h.FlowGpm, InService = h.InService, MainSizeInches = h.MainSizeInches }).ToList();
		}

		public async Task<List<RmsHydrant>> GetNearestAsync(int departmentId, decimal latitude, decimal longitude, int take, double maxMeters)
		{
			var rows = (await _hydrants.GetAllLiveAsync(departmentId))?.ToList() ?? new List<RmsHydrant>();
			return rows.Select(h => (Hydrant: h, Meters: GeoMath.HaversineMeters((double)latitude, (double)longitude, (double)h.Latitude, (double)h.Longitude)))
				.Where(x => maxMeters <= 0 || x.Meters <= maxMeters).OrderBy(x => x.Meters).Take(Math.Clamp(take, 1, 50)).Select(x => x.Hydrant).ToList();
		}

		public Task<int> CountTestDueAsync(int departmentId, DateTime utcNow) => _hydrants.CountTestDueAsync(departmentId, utcNow.AddMonths(-TestIntervalMonths));

		public async Task<HydrantSummary> GetSummaryAsync(int departmentId, string userId, DateTime utcNow, int take)
		{
			await RequireViewAsync(departmentId, userId);
			var summary = new HydrantSummary
			{
				AsOfUtc = utcNow,
				OutOfService = await _hydrants.CountOutOfServiceAsync(departmentId),
				TestDue = await CountTestDueAsync(departmentId, utcNow)
			};
			if (take > 0 && summary.OutOfService > 0)
				summary.OutOfServiceHydrants = (await _hydrants.GetOutOfServiceAsync(departmentId, Math.Min(take, 200)))?.Where(h => h.DepartmentId == departmentId && h.DeletedOn == null && !h.InService).ToList() ?? new List<RmsHydrant>();
			return summary;
		}

		private async Task<RmsHydrant> LiveAsync(int departmentId, string hydrantId)
		{
			if (string.IsNullOrWhiteSpace(hydrantId)) return null;
			var hydrant = await _hydrants.GetByIdForDepartmentAsync(departmentId, hydrantId);
			return hydrant == null || hydrant.DeletedOn != null ? null : hydrant;
		}
	}
}
