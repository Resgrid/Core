using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>Hydrants and other water sources (RMS plan section 4.3, RMS-5): flow/test history, maintenance, in/out-of-service state, CSV import and the response-map layer.</summary>
	public class RecordsHydrantsService : IRecordsHydrantsService
	{
		public const int TestIntervalMonths = 12;

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsHydrantsRepository _hydrants;
		private readonly IRmsHydrantFlowTestsRepository _flowTests;
		private readonly IRmsHydrantMaintenancesRepository _maintenance;
		private readonly IRmsPreventionAttachmentsRepository _attachments;

		public RecordsHydrantsService(RecordsPreventionGate gate, IRmsHydrantsRepository hydrants, IRmsHydrantFlowTestsRepository flowTests, IRmsHydrantMaintenancesRepository maintenance, IRmsPreventionAttachmentsRepository attachments)
		{
			_gate = gate; _hydrants = hydrants; _flowTests = flowTests; _maintenance = maintenance; _attachments = attachments;
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

		private async Task<RmsHydrant> UpsertAsync(int departmentId, string userId, RmsHydrant input, string source, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var number = RecordsPreventionGate.Require(input.HydrantNumber, 64, "A hydrant needs a number.");
			if (input.Latitude < -90 || input.Latitude > 90 || input.Longitude < -180 || input.Longitude > 180) throw new ArgumentException("Hydrant coordinates are out of range.");
			var existing = string.IsNullOrWhiteSpace(input.RmsHydrantId) ? await _hydrants.GetByNumberAsync(departmentId, number) : await _hydrants.GetByIdForDepartmentAsync(departmentId, input.RmsHydrantId);
			if (existing != null && existing.DeletedOn != null) existing = null;
			var entity = existing ?? new RmsHydrant { RmsHydrantId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), InService = true, CreatedOn = now, CreatedByUserId = userId, RowVersion = 0, Source = source };
			if (existing != null && !string.Equals(existing.HydrantNumber, number, StringComparison.OrdinalIgnoreCase))
			{
				var clash = await _hydrants.GetByNumberAsync(departmentId, number);
				if (clash != null && clash.RmsHydrantId != existing.RmsHydrantId) throw new ArgumentException($"Hydrant number {number} is already in use.");
			}
			entity.HydrantNumber = number; entity.Type = input.Type == 0 ? (int)RmsHydrantType.DryBarrel : input.Type; entity.Latitude = input.Latitude; entity.Longitude = input.Longitude;
			entity.AddressText = RecordsPreventionGate.Trim(input.AddressText, 500); entity.OwnerKind = input.OwnerKind == 0 ? (int)RmsHydrantOwnerKind.Municipal : input.OwnerKind; entity.OwnerName = RecordsPreventionGate.Trim(input.OwnerName, 200);
			entity.MainSizeInches = input.MainSizeInches; entity.Notes = RecordsPreventionGate.Trim(input.Notes, 4000); entity.PoiId = input.PoiId;
			if (input.FlowGpm.HasValue) { entity.FlowGpm = input.FlowGpm; entity.FlowClass = (int)HydrantFlowCalculator.Classify(input.FlowGpm); }
			if (input.StaticPressurePsi.HasValue) entity.StaticPressurePsi = input.StaticPressurePsi;
			if (input.ResidualPressurePsi.HasValue) entity.ResidualPressurePsi = input.ResidualPressurePsi;
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
				TestedOn = input.TestedOn == default ? now : input.TestedOn, TestedByUserId = userId, StaticPressurePsi = input.StaticPressurePsi, ResidualPressurePsi = input.ResidualPressurePsi,
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
				PerformedOn = input.PerformedOn == default ? now : input.PerformedOn, PerformedByUserId = userId, Kind = input.Kind == 0 ? (int)RmsHydrantMaintenanceKind.Inspection : input.Kind,
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

		public async Task<HydrantImportResult> ImportCsvAsync(int departmentId, string userId, string csv, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (string.IsNullOrWhiteSpace(csv)) throw new ArgumentException("The import file is empty.");
			var result = new HydrantImportResult();
			var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
			if (lines.Count == 0) return result;
			var header = RecordsInspectionsService.CsvSplit(lines[0]).Select(h => h.Trim().ToLowerInvariant().Replace(" ", "_")).ToList();
			var hasHeader = header.Contains("latitude") || header.Contains("lat") || header.Contains("number") || header.Contains("hydrant_number");
			int Index(params string[] names) { foreach (var n in names) { var i = header.IndexOf(n); if (i >= 0) return i; } return -1; }
			var iNumber = hasHeader ? Index("hydrant_number", "number", "id") : 0;
			var iLat = hasHeader ? Index("latitude", "lat") : 1;
			var iLon = hasHeader ? Index("longitude", "lon", "lng") : 2;
			var iType = hasHeader ? Index("type") : 3;
			var iAddress = hasHeader ? Index("address") : 4;
			var iMain = hasHeader ? Index("main_size", "main_size_inches", "main") : 5;
			var iFlow = hasHeader ? Index("flow_gpm", "flow") : 6;
			var iOwner = hasHeader ? Index("owner", "owner_name") : 7;
			if (iNumber < 0 || iLat < 0 || iLon < 0) throw new ArgumentException("The CSV needs hydrant number, latitude and longitude columns.");
			var existingNumbers = ((await _hydrants.GetAllLiveAsync(departmentId)) ?? Enumerable.Empty<RmsHydrant>()).Select(h => h.HydrantNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);

			for (var lineNo = hasHeader ? 1 : 0; lineNo < lines.Count && result.RowsRead < 20000; lineNo++)
			{
				var cells = RecordsInspectionsService.CsvSplit(lines[lineNo]);
				string Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : null;
				var row = new HydrantImportRow { Line = lineNo + 1, HydrantNumber = Cell(iNumber), Type = Cell(iType), Address = Cell(iAddress), MainSize = Cell(iMain), FlowGpm = Cell(iFlow), Owner = Cell(iOwner) };
				result.RowsRead++;
				if (string.IsNullOrWhiteSpace(row.HydrantNumber)) { row.Error = "Missing hydrant number"; result.Rejected.Add(row); continue; }
				if (!decimal.TryParse(Cell(iLat), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) || !decimal.TryParse(Cell(iLon), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180)
				{ row.Error = "Invalid coordinates"; result.Rejected.Add(row); continue; }
				row.Latitude = lat; row.Longitude = lon;
				var input = new RmsHydrant
				{
					HydrantNumber = row.HydrantNumber, Latitude = lat, Longitude = lon, AddressText = row.Address, OwnerName = row.Owner, Type = (int)ParseType(row.Type),
					MainSizeInches = decimal.TryParse(row.MainSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var main) ? main : (decimal?)null,
					FlowGpm = int.TryParse(row.FlowGpm, out var flow) ? flow : (int?)null,
					OwnerKind = string.IsNullOrWhiteSpace(row.Owner) ? (int)RmsHydrantOwnerKind.Municipal : (int)RmsHydrantOwnerKind.Other
				};
				var existed = existingNumbers.Contains(row.HydrantNumber);
				try
				{
					await UpsertAsync(departmentId, userId, input, "csv-import", cancellationToken);
					if (existed) result.Updated++; else { result.Created++; existingNumbers.Add(row.HydrantNumber); }
				}
				catch (ArgumentException ex) { row.Error = ex.Message; result.Rejected.Add(row); }
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "Hydrants imported", departmentId.ToString(), new { result.RowsRead, result.Created, result.Updated, rejected = result.Rejected.Count }, cancellationToken: cancellationToken);
			return result;
		}

		private static RmsHydrantType ParseType(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return RmsHydrantType.DryBarrel;
			var v = value.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
			if (v.StartsWith("dry")) return RmsHydrantType.DryBarrel;
			if (v.StartsWith("wet")) return RmsHydrantType.WetBarrel;
			if (v.Contains("standpipe")) return RmsHydrantType.Standpipe;
			if (v.Contains("cistern") || v.Contains("tank")) return RmsHydrantType.Cistern;
			if (v.Contains("draft") || v.Contains("pond") || v.Contains("lake") || v.Contains("river")) return RmsHydrantType.DraftingSite;
			return RmsHydrantType.Other;
		}

		public async Task<List<HydrantMapPoint>> GetMapLayerAsync(int departmentId, string userId, decimal? minLat, decimal? maxLat, decimal? minLon, decimal? maxLon)
		{
			await RequireViewAsync(departmentId, userId);
			IEnumerable<RmsHydrant> rows = minLat.HasValue && maxLat.HasValue && minLon.HasValue && maxLon.HasValue
				? await _hydrants.GetInBoundsAsync(departmentId, Math.Min(minLat.Value, maxLat.Value), Math.Max(minLat.Value, maxLat.Value), Math.Min(minLon.Value, maxLon.Value), Math.Max(minLon.Value, maxLon.Value), 5000)
				: await _hydrants.GetAllLiveAsync(departmentId);
			return (rows ?? Enumerable.Empty<RmsHydrant>()).Select(h => new HydrantMapPoint { HydrantId = h.RmsHydrantId, HydrantNumber = h.HydrantNumber, Type = h.Type, Latitude = h.Latitude, Longitude = h.Longitude, FlowClass = h.FlowClass, FlowGpm = h.FlowGpm, InService = h.InService, MainSizeInches = h.MainSizeInches }).ToList();
		}

		public async Task<List<RmsHydrant>> GetNearestAsync(int departmentId, decimal latitude, decimal longitude, int take, double maxMeters)
		{
			var rows = (await _hydrants.GetAllLiveAsync(departmentId))?.ToList() ?? new List<RmsHydrant>();
			return rows.Select(h => (Hydrant: h, Meters: GeoMath.HaversineMeters((double)latitude, (double)longitude, (double)h.Latitude, (double)h.Longitude)))
				.Where(x => maxMeters <= 0 || x.Meters <= maxMeters).OrderBy(x => x.Meters).Take(Math.Clamp(take, 1, 50)).Select(x => x.Hydrant).ToList();
		}

		public Task<int> CountTestDueAsync(int departmentId, DateTime utcNow) => _hydrants.CountTestDueAsync(departmentId, utcNow.AddMonths(-TestIntervalMonths));

		private async Task<RmsHydrant> LiveAsync(int departmentId, string hydrantId)
		{
			if (string.IsNullOrWhiteSpace(hydrantId)) return null;
			var hydrant = await _hydrants.GetByIdForDepartmentAsync(departmentId, hydrantId);
			return hydrant == null || hydrant.DeletedOn != null ? null : hydrant;
		}
	}
}
