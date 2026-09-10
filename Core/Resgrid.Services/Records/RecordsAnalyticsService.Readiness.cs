using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// RMS-6 readiness dashboard (RMS plan section 6, RMS-6, third bullet; section 4.5). RMS computes no readiness of
	/// its own: the checklists module answers compliance through <see cref="IChecklistsService"/>, the maintenance
	/// module answers work-order statistics and history through <see cref="IWorkOrderReportingService"/>, the inventory
	/// module reveals the assets, locations and issuances this viewer may read through <see cref="IInventoryCatalogService"/>,
	/// and Records contributes only the readiness-packet evidence it already captured. Each module authorizes the
	/// viewer itself; a refusal marks the section forbidden and nothing from that module is counted. The unit board
	/// joins those answers on the unit id, restricted to the units a group-scoped viewer may see.
	/// </summary>
	public partial class RecordsAnalyticsService
	{
		/// <summary>The checklists module accepts a compliance cohort of at most 93 days.</summary>
		public const int ChecklistCohortDays = 93;

		/// <summary>Inventory pages read per table (500 rows each) before the equipment section reports truncation.</summary>
		public const int InventoryPageCap = 10;

		/// <summary>Work-order history pages read (50 orders each) for the per-unit roll-up before it reports truncation.</summary>
		public const int WorkOrderHistoryPageCap = 40;

		private static readonly int[] OutstandingIssuance = { (int)InventoryIssuanceStatus.Outstanding, (int)InventoryIssuanceStatus.PartiallyReturned };
		private static readonly string[] AgeBands = { "0-6 days", "7-29 days", "30-89 days", "90+ days" };

		public async Task<RecordsReadiness> GetReadinessAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default)
		{
			var c = await BeginAsync(departmentId, userId, query);
			var data = await LoadAsync(c, c.Start, c.End, true);
			var result = new RecordsReadiness { DepartmentId = c.DepartmentId };
			var labels = await LabelsAsync(departmentId, false, true, false);
			var units = await VisibleUnitsAsync(c);
			var rows = units.ToDictionary(u => u.UnitId, u => new RecordsUnitReadinessRow { UnitId = u.UnitId, Label = u.Name, StationGroupId = u.StationGroupId, StationGroupLabel = u.StationGroupId.HasValue ? labels.Group(u.StationGroupId.Value.ToString()) : null });
			var actor = new ChecklistActor { DepartmentId = departmentId, UserId = userId };

			await SectionAsync(result, "Unit states", async () =>
			{
				foreach (var state in (await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(departmentId)) ?? new List<UnitState>())
					if (state != null && rows.TryGetValue(state.UnitId, out var row)) { row.State = state.State; row.StateOn = state.Timestamp; }
			});

			result.ChecklistsEnabled = await FlagAsync(() => _readinessAccess.CanUseChecklistsAsync(departmentId));
			if (result.ChecklistsEnabled)
				await ModuleSectionAsync(result, "Checklists", async () => { result.Checklists = await ChecklistMetricsAsync(c, actor, rows, labels); },
					status => { if (status == 403) result.ChecklistsForbidden = true; else result.ChecklistsEnabled = false; });

			result.MaintenanceEnabled = await FlagAsync(() => _flags.IsEnabledAsync(FeatureFlagKeys.MaintenanceWorkOrders, departmentId));
			if (result.MaintenanceEnabled)
				await ModuleSectionAsync(result, "Work orders", async () => { result.WorkOrders = await WorkOrderMetricsAsync(c, actor, rows); },
					status => { if (status == 403) result.MaintenanceForbidden = true; else result.MaintenanceEnabled = false; });

			result.InventoryEnabled = await FlagAsync(() => _inventoryAuthorization.IsEnabledAsync(departmentId));
			if (result.InventoryEnabled)
				await ModuleSectionAsync(result, "Equipment", async () => { result.Equipment = await EquipmentMetricsAsync(c, rows); },
					status => { if (status == 403) result.InventoryForbidden = true; else result.InventoryEnabled = false; });

			await SectionAsync(result, "Readiness evidence", async () => { result.Evidence = await EvidenceMetricsAsync(c, data, rows); });

			result.Units = rows.Values.OrderByDescending(r => r.NeedsAttention).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
			return Finish(result, c, data);
		}

		#region Gates and scope

		private static async Task<bool> FlagAsync(Func<Task<bool>> probe)
		{
			try { return await probe(); }
			catch (Exception ex) { Logging.LogException(ex, "Records analytics: readiness module probe failed; treating the module as off."); return false; }
		}

		/// <summary>A module's own refusal (403) or "not available" (402/404/409) answer is reported as such; anything else degrades to a warning.</summary>
		private static async Task ModuleSectionAsync(RecordsAnalyticsBase result, string area, Func<Task> work, Action<int> onModuleStatus)
		{
			try { await work(); }
			catch (Exception ex)
			{
				var status = ex switch
				{
					ChecklistException checklist => checklist.StatusCode,
					WorkOrderException workOrder => workOrder.StatusCode,
					InventoryException inventory => inventory.StatusCode,
					UnauthorizedAccessException _ => 403,
					_ => (int?)null
				};
				if (status == 403 || status == 402 || status == 404 || status == 409) { onModuleStatus(status.Value); return; }
				Logging.LogException(ex, $"Records analytics: {area} unavailable for department {result.DepartmentId}.");
				result.Warnings.Add($"{area} unavailable.");
			}
		}

		/// <summary>Units of the department, narrowed by the station filter and — for a group-scoped viewer — to groups they may see (a unit without a group stays visible, as a Record without one does).</summary>
		private async Task<List<Unit>> VisibleUnitsAsync(Context c)
		{
			var units = ((await _unitsService.GetUnitsForDepartmentAsync(c.DepartmentId)) ?? new List<Unit>()).Where(u => u != null).ToList();
			if (c.StationGroupId.HasValue) units = units.Where(u => u.StationGroupId == c.StationGroupId).ToList();
			if (c.GroupScoped) units = units.Where(u => !u.StationGroupId.HasValue || c.Visible.Contains(u.StationGroupId.Value)).ToList();
			return units.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static bool GroupVisible(Context c, int? groupId) => !groupId.HasValue || (!c.StationGroupId.HasValue || groupId == c.StationGroupId) && (!c.GroupScoped || c.Visible.Contains(groupId.Value));

		#endregion

		#region Checklists

		private async Task<RecordsChecklistMetrics> ChecklistMetricsAsync(Context c, ChecklistActor actor, Dictionary<int, RecordsUnitReadinessRow> rows, Labels labels)
		{
			var cohortStart = c.End - c.Start > TimeSpan.FromDays(ChecklistCohortDays) ? c.End.AddDays(-ChecklistCohortDays) : c.Start;
			var summary = await _checklists.GetComplianceSummaryAsync(actor, new ChecklistReportQuery { FromUtc = cohortStart, UntilUtc = c.End });
			var m = new RecordsChecklistMetrics { CohortStart = cohortStart, CohortEnd = c.End, IsRedacted = summary.IsRedacted, UnavailableSources = summary.UnavailableSources?.ToList() ?? new List<string>() };
			if (cohortStart > c.Start) c.Warnings.Add($"Checklist compliance covers the last {ChecklistCohortDays} days of the window ({cohortStart:yyyy-MM-dd} to {c.End.AddDays(-1):yyyy-MM-dd}); the checklists module measures at most {ChecklistCohortDays} days at a time.");

			// Unit and group targets follow the dashboard's station filter and the viewer's group scope; person and asset
			// targets were already authorized by the module and pass through.
			var groups = (summary.Groups ?? new List<ChecklistComplianceGroup>()).Where(g => g?.Target != null).Where(g =>
			{
				switch (g.Target.Type)
				{
					case ChecklistTargetType.Unit: return int.TryParse(g.Target.Id, out var unitId) ? rows.ContainsKey(unitId) : GroupVisible(c, g.Target.GroupId);
					case ChecklistTargetType.Group: return int.TryParse(g.Target.Id, out var groupId) ? GroupVisible(c, groupId) : GroupVisible(c, g.Target.GroupId);
					default: return GroupVisible(c, g.Target.GroupId);
				}
			}).ToList();

			m.Expected = groups.Sum(g => g.Expected); m.Completed = groups.Sum(g => g.Completed); m.OnTime = groups.Sum(g => g.OnTime);
			m.Missed = groups.Sum(g => g.Missed); m.Skipped = groups.Sum(g => g.Skipped);
			m.CompletionRatePercent = Pct(m.Completed, m.Expected); m.OnTimePercent = Pct(m.OnTime, m.Expected);

			RecordsReadinessTargetRow Row(ChecklistComplianceGroup g, string label) => new RecordsReadinessTargetRow
			{
				Key = g.Target.Id, Label = label, Expected = g.Expected, Completed = g.Completed, OnTime = g.OnTime, Missed = g.Missed, Skipped = g.Skipped,
				CompletionRatePercent = Pct(g.Completed, g.Expected), OnTimePercent = Pct(g.OnTime, g.Expected)
			};
			m.ByUnit = groups.Where(g => g.Target.Type == ChecklistTargetType.Unit).Select(g => Row(g, int.TryParse(g.Target.Id, out var id) && rows.TryGetValue(id, out var r) ? r.Label : g.Target.Name ?? g.Target.Id)).OrderByDescending(r => r.Missed).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
			m.ByStationGroup = groups.Where(g => g.Target.Type == ChecklistTargetType.Group).Select(g => Row(g, labels.Group(g.Target.Id))).OrderByDescending(r => r.Missed).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
			m.ByPerson = groups.Where(g => g.Target.Type == ChecklistTargetType.Personnel).Select(g => Row(g, g.Target.Name ?? g.Target.Id)).OrderByDescending(r => r.Missed).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
			m.ByAsset = groups.Where(g => g.Target.Type == ChecklistTargetType.InventoryAsset).Select(g => Row(g, g.Target.Name ?? g.Target.Id)).OrderByDescending(r => r.Missed).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
			m.Trend = (summary.Trend ?? new List<ChecklistMissedTrend>()).OrderBy(t => t.DayUtc).Select(t => new RecordsChecklistTrendPoint { Day = t.DayUtc, Expected = t.Expected, Missed = t.Missed }).ToList();

			foreach (var g in groups.Where(g => g.Target.Type == ChecklistTargetType.Unit))
				if (int.TryParse(g.Target.Id, out var unitId) && rows.TryGetValue(unitId, out var row))
				{
					row.ChecklistExpected += g.Expected; row.ChecklistCompleted += g.Completed; row.ChecklistMissed += g.Missed;
					row.ChecklistCompletionRatePercent = Pct(row.ChecklistCompleted, row.ChecklistExpected);
				}
			return m;
		}

		#endregion

		#region Work orders

		private async Task<RecordsWorkOrderMetrics> WorkOrderMetricsAsync(Context c, ChecklistActor actor, Dictionary<int, RecordsUnitReadinessRow> rows)
		{
			var stats = await _workOrderReporting.GetWorkOrderStatsAsync(actor, new WorkOrderReportQuery { FromUtc = c.Start, UntilUtc = c.End, GroupId = c.StationGroupId });
			var m = new RecordsWorkOrderMetrics
			{
				CohortStart = stats.FromUtc, CohortEnd = stats.UntilUtc, Total = stats.Total, Open = stats.Open, Overdue = stats.Overdue,
				MeanTimeToRepairHours = stats.MeanTimeToRepairHours.HasValue ? Math.Round((double)stats.MeanTimeToRepairHours.Value, 1) : (double?)null,
				RepairSamples = stats.RepairSamples, MissingRepairTimes = stats.MissingRepairTimes,
				OpenByPriority = (stats.OpenByPriority ?? new int[4]).Select((n, i) => new RecordsAnalyticsCount { Key = i.ToString(), Label = EnumLabel<WorkOrderPriority>(i), Count = n, Percent = Pct(n, stats.Open) }).ToList(),
				OpenByAge = (stats.OpenByAge ?? new int[4]).Select((n, i) => new RecordsAnalyticsCount { Key = i.ToString(), Label = i < AgeBands.Length ? AgeBands[i] : i.ToString(), Count = n, Percent = Pct(n, stats.Open) }).ToList(),
				Costs = (stats.Costs ?? new List<WorkOrderCostTotal>()).Select(x => new RecordsWorkOrderCost { Currency = x.Currency, Labor = x.Labor, Parts = x.Parts, Unknown = x.UnknownLabor + x.UnknownParts }).ToList()
			};

			// Per-unit roll-up from the module's paged history: the same window, the same scope, bounded by the page cap.
			var byUnit = new Dictionary<int, RecordsWorkOrderUnitRow>();
			var query = new WorkOrderReportQuery { FromUtc = c.Start, UntilUtc = c.End, GroupId = c.StationGroupId, AfterId = 0 };
			for (var page = 0; ; page++)
			{
				if (page >= WorkOrderHistoryPageCap) { m.HistoryTruncated = true; c.Warnings.Add($"The per-unit work-order roll-up covers the first {WorkOrderHistoryPageCap * 50:N0} orders in the window; the module's totals are complete."); break; }
				var history = await _workOrderReporting.GetWorkOrderHistoryAsync(actor, query);
				foreach (var entry in history?.Items ?? new List<WorkOrderReportEntry>())
				{
					var order = entry?.Order;
					if (order == null || !order.UnitId.HasValue || !rows.TryGetValue(order.UnitId.Value, out var unitRow)) continue;
					if (!byUnit.TryGetValue(order.UnitId.Value, out var u)) byUnit[order.UnitId.Value] = u = new RecordsWorkOrderUnitRow { Key = order.UnitId.Value.ToString(), Label = unitRow.Label };
					var open = (int)order.Status <= (int)WorkOrderStatus.Completed;
					var overdue = (int)order.Status < (int)WorkOrderStatus.Completed && order.DueOn.HasValue && order.DueOn < c.Now;
					u.Total++;
					if (open) { u.Open++; unitRow.OpenWorkOrders++; if (order.Priority == WorkOrderPriority.Emergency) u.Emergency++; }
					if (overdue) { u.Overdue++; unitRow.OverdueWorkOrders++; }
					if (order.Status == WorkOrderStatus.OnHold) { u.OnHold++; unitRow.OnHoldWorkOrders++; m.OnHold++; }
				}
				if (history?.NextAfterId == null) break;
				query.AfterId = history.NextAfterId.Value;
			}
			m.ByUnit = byUnit.Values.OrderByDescending(u => u.Overdue).ThenByDescending(u => u.Open).ThenBy(u => u.Label, StringComparer.OrdinalIgnoreCase).ToList();
			return m;
		}

		#endregion

		#region Equipment

		private async Task<List<T>> InventoryPagesAsync<T>(InventoryActor actor, Action truncated) where T : InventoryRow
		{
			var rows = new List<T>();
			for (var page = 0; page < InventoryPageCap; page++)
			{
				var result = await _inventory.ListAsync<T>(actor, page);
				rows.AddRange(result?.Items ?? new List<T>());
				if (result == null || !result.HasMore) return rows;
			}
			truncated();
			return rows;
		}

		private async Task<RecordsEquipmentMetrics> EquipmentMetricsAsync(Context c, Dictionary<int, RecordsUnitReadinessRow> rows)
		{
			var actor = new InventoryActor { DepartmentId = c.DepartmentId, UserId = c.UserId };
			var m = new RecordsEquipmentMetrics();
			void Truncate() => m.Truncated = true;
			var assets = (await InventoryPagesAsync<InventoryAsset>(actor, Truncate)).Where(a => a != null && !a.IsDeleted).ToList();
			var locations = (await InventoryPagesAsync<InventoryLocation>(actor, Truncate)).Where(l => l != null && !l.IsDeleted).ToDictionary(l => l.Id, StringComparer.Ordinal);
			var issuances = (await InventoryPagesAsync<InventoryIssuance>(actor, Truncate)).Where(i => i != null && !i.IsDeleted).ToList();
			if (m.Truncated) c.Warnings.Add($"Equipment covers the first {InventoryPageCap * 500:N0} rows of each inventory table the viewer may read.");
			var assetById = assets.ToDictionary(a => a.Id, StringComparer.Ordinal);

			int? UnitOf(InventoryAsset asset)
			{
				var locationId = asset.CurrentLocationId;
				for (var depth = 0; depth < 6 && locationId != null && locations.TryGetValue(locationId, out var location); depth++)
				{
					if (location.UnitId.HasValue) return location.UnitId;
					if (location.ContainerAssetId != null && assetById.TryGetValue(location.ContainerAssetId, out var container) && container.Id != asset.Id) { locationId = container.CurrentLocationId; continue; }
					locationId = location.ParentLocationId;
				}
				return null;
			}

			var soon = c.Now.AddDays(30);
			var outstanding = issuances.Where(i => OutstandingIssuance.Contains(i.Status) && !i.ReturnedOn.HasValue).ToList();
			var issuedToUnit = outstanding.Where(i => i.IssuedToUnitId.HasValue && i.AssetId != null).GroupBy(i => i.AssetId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().IssuedToUnitId.Value, StringComparer.Ordinal);

			m.Assets = assets.Count;
			m.InService = assets.Count(a => a.Status == (int)InventoryAssetStatus.InService);
			m.Issued = assets.Count(a => a.Status == (int)InventoryAssetStatus.Issued);
			m.OutForRepair = assets.Count(a => a.Status == (int)InventoryAssetStatus.OutForRepair);
			m.Damaged = assets.Count(a => a.Status == (int)InventoryAssetStatus.Damaged);
			bool Live(InventoryAsset a) => a.Status == (int)InventoryAssetStatus.InService || a.Status == (int)InventoryAssetStatus.Issued;
			m.Expired = assets.Count(a => Live(a) && a.ExpiresOn.HasValue && a.ExpiresOn < c.Now);
			m.ExpiringWithin30Days = assets.Count(a => Live(a) && a.ExpiresOn.HasValue && a.ExpiresOn >= c.Now && a.ExpiresOn < soon);
			m.IssuancesOutstanding = outstanding.Count;
			m.IssuancesOverdueReturn = outstanding.Count(i => i.ExpectedReturnOn.HasValue && i.ExpectedReturnOn < c.Now);
			m.ByStatus = Counts(assets, a => a.Status.ToString(), k => EnumLabel<InventoryAssetStatus>(int.Parse(k)));

			var byUnit = new Dictionary<int, RecordsEquipmentUnitRow>();
			RecordsEquipmentUnitRow UnitRow(int unitId, string label) => byUnit.TryGetValue(unitId, out var r) ? r : byUnit[unitId] = new RecordsEquipmentUnitRow { Key = unitId.ToString(), Label = label };
			foreach (var asset in assets)
			{
				var unitId = issuedToUnit.TryGetValue(asset.Id, out var issued) ? issued : UnitOf(asset);
				if (!unitId.HasValue || !rows.TryGetValue(unitId.Value, out var unitRow)) continue;
				var u = UnitRow(unitId.Value, unitRow.Label);
				u.Assets++; unitRow.AssetsAssigned++;
				if (issuedToUnit.ContainsKey(asset.Id)) u.Issued++;
				if (asset.Status == (int)InventoryAssetStatus.OutForRepair) { u.OutForRepair++; unitRow.AssetsOutForRepair++; }
				if (asset.Status == (int)InventoryAssetStatus.Damaged) u.Damaged++;
				if (Live(asset) && asset.ExpiresOn.HasValue && asset.ExpiresOn < c.Now) { u.Expired++; unitRow.AssetsExpired++; }
			}
			foreach (var issuance in outstanding.Where(i => i.IssuedToUnitId.HasValue && i.ExpectedReturnOn.HasValue && i.ExpectedReturnOn < c.Now))
				if (rows.TryGetValue(issuance.IssuedToUnitId.Value, out var unitRow)) UnitRow(issuance.IssuedToUnitId.Value, unitRow.Label).OverdueReturns++;
			m.ByUnit = byUnit.Values.OrderByDescending(u => u.OutForRepair + u.Expired + u.OverdueReturns).ThenBy(u => u.Label, StringComparer.OrdinalIgnoreCase).ToList();
			return m;
		}

		#endregion

		#region Evidence

		private async Task<RecordsReadinessEvidenceMetrics> EvidenceMetricsAsync(Context c, Dataset data, Dictionary<int, RecordsUnitReadinessRow> rows)
		{
			var headers = ((await _evidence.GetHeadersByKindInRangeAsync(c.DepartmentId, RmsEvidenceKind.ReadinessPacket, c.Start, c.End, RecordsAnalyticsLimits.RowCap + 1)) ?? Enumerable.Empty<RmsEvidenceArtifactHeader>())
				.Where(h => h != null && !h.SupersededOn.HasValue).ToList();
			if (headers.Count > RecordsAnalyticsLimits.RowCap) { headers = headers.Take(RecordsAnalyticsLimits.RowCap).ToList(); c.Warnings.Add($"More than {RecordsAnalyticsLimits.RowCap:N0} readiness packets fall in this window; evidence figures cover the first {RecordsAnalyticsLimits.RowCap:N0} by capture."); }
			var callLinked = data.Records.Where(r => r.CallId.HasValue).Select(r => r.RmsOperationalRecordId).Concat(data.Reports.Select(r => r.RmsIncidentReportId)).ToHashSet(StringComparer.Ordinal);
			// A group-scoped viewer counts packets only on Records they can open; a department viewer counts every packet captured.
			if (c.GroupScoped) headers = headers.Where(h => callLinked.Contains(h.RecordId) || data.RevisionOf.ContainsKey(h.RecordId)).ToList();
			var withPacket = headers.Select(h => h.RecordId).ToHashSet(StringComparer.Ordinal);
			var m = new RecordsReadinessEvidenceMetrics
			{
				PacketsCaptured = headers.Count,
				CallLinkedRecords = callLinked.Count,
				RecordsWithPacket = callLinked.Count(id => withPacket.Contains(id)),
				ByMonth = Counts(headers.Select(h => Month(h.CapturedOn, c)), k => k, k => k, null, false, StringComparer.Ordinal)
			};
			m.CoveragePercent = Pct(m.RecordsWithPacket, m.CallLinkedRecords);
			foreach (var unitId in data.Units.Where(u => withPacket.Contains(u.RecordId) && u.UnitId > 0).Select(u => (u.RecordId, UnitId: u.UnitId))
				.Concat(data.ReportUnits.Where(u => withPacket.Contains(u.RecordId) && u.UnitId.HasValue).Select(u => (u.RecordId, UnitId: u.UnitId.Value))).Distinct())
				if (rows.TryGetValue(unitId.UnitId, out var row)) row.ReadinessPackets++;
			return m;
		}

		#endregion
	}
}
