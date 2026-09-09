using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		public async Task<InventoryReport> BuildReportAsync(InventoryActor actor, InventoryReportInput input)
		{
			await _auth.RequireAsync(actor);
			if (input == null || !Enum.IsDefined(input.Kind)) throw new InventoryException(400, "InvalidReport");
			foreach (var id in new[] { input.LocationId, input.ItemId }.Where(x => x != null)) Id(id);
			if (input.UserId != null && (string.IsNullOrWhiteSpace(input.UserId) || input.UserId.Length > 128) || input.UnitId <= 0
				|| input.UserId != null && input.UnitId.HasValue) throw new InventoryException(400, "InvalidReportFilter");
			if ((input.FromUtc.HasValue || input.UntilUtc.HasValue) && input.Kind is InventoryReportKind.OnHand or InventoryReportKind.LowStock or InventoryReportKind.Valuation)
				throw new InventoryException(400, "InvalidReportFilter");
			if (input.FromUtc?.Kind == DateTimeKind.Local || input.UntilUtc?.Kind == DateTimeKind.Local) throw new InventoryException(400, "InvalidReportRange");
			if (_uow.Transaction != null) throw new InvalidOperationException("Inventory reports own their read transaction.");
			try
			{
				await _uow.CreateOrGetConnectionAsync(CancellationToken.None);
				await _store.LockDepartmentAsync(actor.DepartmentId);
				if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
				if (!await _auth.IsEnabledAsync(actor.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
				if (input.Kind == InventoryReportKind.ControlledSubstanceLog) await _auth.RequireAsync(actor, false, PermissionTypes.ManageControlledSubstances);
				var report = await ReadInventoryReportAsync(actor, input);
				await _auth.RequireAsync(actor, false, input.Kind == InventoryReportKind.ControlledSubstanceLog ? PermissionTypes.ManageControlledSubstances
					: input.Kind == InventoryReportKind.Valuation ? PermissionTypes.AdjustInventory : null);
				if (!await _auth.IsEnabledAsync(actor.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
				_uow.CommitChanges(); return report;
			}
			catch { _uow.DiscardChanges(); throw; }
		}

		private async Task<InventoryReport> ReadInventoryReportAsync(InventoryActor actor, InventoryReportInput input)
		{
			var report = new InventoryReport { Kind = input.Kind, GeneratedOn = Now };
			var history = input.Kind is InventoryReportKind.Usage or InventoryReportKind.TransferHistory or InventoryReportKind.Issuance or InventoryReportKind.ControlledSubstanceLog;
			if (history || input.FromUtc.HasValue || input.UntilUtc.HasValue)
			{
				var until = input.UntilUtc ?? report.GeneratedOn.Date.AddDays(1);
				if (!input.FromUtc.HasValue && until < DateTime.MinValue.AddDays(30)) throw new InventoryException(400, "InvalidReportRange");
				var from = input.FromUtc ?? until.AddDays(-30);
				if (from >= until || until - from > TimeSpan.FromDays(366)) throw new InventoryException(400, "InvalidReportRange");
				report.FromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc); report.UntilUtc = DateTime.SpecifyKind(until, DateTimeKind.Utc);
			}
			if (input.Kind == InventoryReportKind.Expiration && !input.FromUtc.HasValue && !input.UntilUtc.HasValue)
				report.UntilUtc = report.GeneratedOn.Date.AddDays(31); // Include already expired stock and the next 30 UTC calendar days.
			bool InRange(DateTime date) => (!report.FromUtc.HasValue || date >= report.FromUtc) && (!report.UntilUtc.HasValue || date < report.UntilUtc);
			bool ItemMatches(string id) => input.ItemId == null || input.ItemId == id;
			void Columns(params string[] columns) => report.Columns.AddRange(columns);
			void Add(params (string Key, object Value)[] cells)
			{
				if (report.Rows.Count >= 5000) throw new InventoryException(409, "InventoryTooLarge");
				report.Rows.Add(cells.ToDictionary(x => x.Key, x => x.Value));
			}
			var itemCache = new Dictionary<string, InventoryItem>();
			var lotCache = new Dictionary<string, InventoryLot>();
			var locations = new Dictionary<(string Id, bool Historical), (InventoryLocation Location, InventoryLocation Holder)>();
			var authorizedSources = new List<RecordInventoryUsage>();
			var authorizedRecipients = new List<InventoryLocation>();
			async Task<InventoryItem> Item(string id)
			{
				if (!itemCache.TryGetValue(id, out var item)) itemCache[id] = item = await GetAsync<InventoryItem>(actor, id);
				return item;
			}
			async Task<InventoryLot> Lot(string id)
			{
				if (id == null) return null;
				if (!lotCache.TryGetValue(id, out var lot)) lotCache[id] = lot = await GetAsync<InventoryLot>(actor, id);
				return lot;
			}
			async Task<(InventoryLocation Location, InventoryLocation Holder)> Location(string id, bool historical = false)
			{
				if (id == null) return (null, null);
				if (locations.TryGetValue((id, historical), out var cached)) return cached;
				var stored = await LocationAsync(actor, id, historical: historical);
				var holder = await EffectiveLocationAsync(actor.DepartmentId, stored, historical: historical);
				var revealed = await RevealAsync(actor, stored);
				locations[(id, historical)] = (revealed, holder); return (revealed, holder);
			}
			async Task<bool> VisibleLocation(string id, bool historical = false, bool filterHolder = true)
			{
				if (id == null || input.LocationId != null && id != input.LocationId) return false;
				try
				{
					// Scope denials are filtered before the protected content is read. ADP failures remain errors.
					var location = await LocationAsync(actor, id, historical: historical);
					var holder = await EffectiveLocationAsync(actor.DepartmentId, location, historical: historical);
					return !filterHolder || (input.UnitId == null || holder.UnitId == input.UnitId) && (input.UserId == null || holder.UserId == input.UserId);
				}
				catch (InventoryException error) when (error.StatusCode is 403 or 404 || error.Code == "LocationUnavailable") { return false; }
			}
			async Task<bool> Authorized(InventoryRow row)
			{
				try { await AuthorizeRowAsync(actor, row); return true; }
				catch (InventoryException error) when (error.StatusCode is 403 or 404 || error.Code == "LocationUnavailable") { return false; }
			}
			async Task<string> LocationName(string id, bool historical = false) => id == null ? null : Decode<InventoryLabel>((await Location(id, historical)).Location).Name ?? id;
			async Task<string> LotNumber(string id) => id == null ? null : Decode<InventoryLotContent>(await Lot(id)).LotNumber ?? id;
			if (input.LocationId != null) await Location(input.LocationId, history);
			if (input.ItemId != null) await Item(input.ItemId);
			if (input.UserId != null || input.UnitId.HasValue)
			{
				var holder = new InventoryLocation { DepartmentId = actor.DepartmentId, UserId = input.UserId, UnitId = input.UnitId,
					LocationType = (int)(input.UnitId.HasValue ? InventoryLocationType.Unit : InventoryLocationType.Personnel) };
				await _auth.ValidateHolderAsync(actor, holder); authorizedRecipients.Add(holder);
			}

			if (input.Kind is InventoryReportKind.OnHand or InventoryReportKind.Expiration)
			{
				Columns("Name", "UnitOfMeasure", "Location", "Amount", "TrackingMode", "AssetId", "Batch", "SerialNumber", "Status", "Expiration");
				if (input.Kind == InventoryReportKind.Expiration) report.Columns.Add("M5DaysUntilExpiry");
				async Task StockRow(string itemId, string locationId, string lotId, decimal quantity, InventoryAsset asset = null)
				{
					if (!ItemMatches(itemId) || !await VisibleLocation(locationId)) return;
					var item = await Item(itemId); var detail = Decode<InventoryItemContent>(item); var lot = await Lot(lotId);
					var expires = asset?.ExpiresOn ?? lot?.ExpiresOn;
					if (lot?.ExpiresOn != null && (!expires.HasValue || lot.ExpiresOn < expires)) expires = lot.ExpiresOn;
					if (input.Kind == InventoryReportKind.Expiration && (!expires.HasValue || quantity <= 0 || !InRange(expires.Value))) return;
					var assetDetails = asset == null ? null : Decode<InventoryAssetContent>(asset);
					var cells = new List<(string Key, object Value)> { ("Name", detail.Name), ("UnitOfMeasure", detail.UnitOfMeasure), ("Location", await LocationName(locationId)), ("Amount", quantity),
						("TrackingMode", (InventoryTrackingMode)item.TrackingMode), ("AssetId", asset?.Id), ("Batch", await LotNumber(lotId)), ("SerialNumber", assetDetails?.SerialNumber),
						("Status", asset == null ? null : (object)(InventoryAssetStatus)asset.Status), ("Expiration", expires) };
					if (input.Kind == InventoryReportKind.Expiration) cells.Add(("M5DaysUntilExpiry", expires.HasValue ? (object)(expires.Value.Date - report.GeneratedOn.Date).Days : null));
					Add(cells.ToArray());
				}
				foreach (var stock in await AllAsync<InventoryStock>(actor.DepartmentId))
					if (!stock.IsDeleted && stock.Quantity != 0) await StockRow(stock.ItemId, stock.LocationId, stock.LotId, stock.Quantity);
				foreach (var stored in await AllAsync<InventoryAsset>(actor.DepartmentId))
					if (!stored.IsDeleted && stored.Status is 0 or 1 or 2 or 3 && ItemMatches(stored.ItemId) && await VisibleLocation(stored.CurrentLocationId))
						await StockRow(stored.ItemId, stored.CurrentLocationId, stored.LotId, 1, await RevealAsync(actor, stored));
			}
			else if (input.Kind == InventoryReportKind.LowStock)
			{
				Columns("Name", "UnitOfMeasure", "OnHand", "ReorderPoint", "M5Shortfall", "M5ReorderQuantity");
				var totals = new Dictionary<string, decimal>();
				var assetTotals = new Dictionary<string, decimal>();
				foreach (var stock in await AllAsync<InventoryStock>(actor.DepartmentId))
				{
					if (stock.IsDeleted || !ItemMatches(stock.ItemId) || !await VisibleLocation(stock.LocationId)) continue;
					totals.TryGetValue(stock.ItemId, out var quantity); totals[stock.ItemId] = quantity + stock.Quantity;
					await Location(stock.LocationId);
				}
				foreach (var asset in await AllAsync<InventoryAsset>(actor.DepartmentId))
				{
					if (asset.IsDeleted || asset.Status is not (0 or 1 or 2 or 3) || !ItemMatches(asset.ItemId) || !await VisibleLocation(asset.CurrentLocationId)) continue;
					assetTotals.TryGetValue(asset.ItemId, out var quantity); assetTotals[asset.ItemId] = quantity + 1;
					await Location(asset.CurrentLocationId);
				}
				foreach (var stored in await AllAsync<InventoryItem>(actor.DepartmentId))
				{
					if (stored.IsDeleted || !stored.IsActive || !ItemMatches(stored.Id)) continue;
					var details = Decode<InventoryItemContent>(await Item(stored.Id)); var threshold = details.ReorderPoint ?? details.MinLevel;
					(stored.TrackingMode == (int)InventoryTrackingMode.Bulk ? totals : assetTotals).TryGetValue(stored.Id, out var quantity);
					if (threshold.HasValue && quantity <= threshold.Value) Add(("Name", details.Name), ("UnitOfMeasure", details.UnitOfMeasure), ("OnHand", quantity),
						("ReorderPoint", threshold), ("M5Shortfall", threshold.Value - quantity), ("M5ReorderQuantity", details.ReorderQuantity));
				}
			}
			else if (input.Kind == InventoryReportKind.Valuation)
			{
				Columns("Name", "Location", "AssetId", "LotId", "Amount", "UnitCost", "M4Currency", "M4KnownValue");
				var valuation = await ReadValuationAsync(actor, input.LocationId);
				foreach (var line in valuation.Lines)
				{
					if (!ItemMatches(line.ItemId) || !await VisibleLocation(line.LocationId)) continue;
					Add(("Name", line.ItemName), ("Location", await LocationName(line.LocationId)), ("AssetId", line.AssetId), ("LotId", line.LotId),
						("Amount", line.Quantity), ("UnitCost", line.UnitCost), ("M4Currency", line.CurrencyCode), ("M4KnownValue", line.Value));
				}
				report.Totals = report.Rows.GroupBy(row => row["M4Currency"] as string).Select(group => new InventoryValuationTotal {
					CurrencyCode = group.Key, KnownValue = group.Sum(row => (decimal?)row["M4KnownValue"] ?? 0), UncostedRows = group.Count(row => row["M4KnownValue"] == null) }).OrderBy(x => x.CurrencyCode).ToList();
			}
			else if (input.Kind == InventoryReportKind.Issuance)
			{
				Columns("M5IssuanceId", "Name", "UnitOfMeasure", "M5PersonId", "M5UnitId", "Location", "M5IssuedOn", "Amount", "M5ReturnedQuantity", "M5OutstandingQuantity", "ExpectedReturn", "M5ReturnedOn", "Status", "Note");
				foreach (var stored in (await AllAsync<InventoryIssuance>(actor.DepartmentId)).OrderBy(x => x.IssuedOn).ThenBy(x => x.Id))
				{
					if (stored.IsDeleted || !ItemMatches(stored.ItemId) || !InRange(stored.IssuedOn) || input.UserId != null && input.UserId != stored.IssuedToUserId
						|| input.UnitId.HasValue && input.UnitId != stored.IssuedToUnitId || !await VisibleLocation(stored.LocationId, true, false) || !await Authorized(stored)) continue;
					// The source location alone does not authorize the personnel/unit receiving the equipment.
					var recipient = new InventoryLocation { DepartmentId = actor.DepartmentId, UserId = stored.IssuedToUserId, UnitId = stored.IssuedToUnitId,
						LocationType = (int)(stored.IssuedToUnitId.HasValue ? InventoryLocationType.Unit : InventoryLocationType.Personnel) };
					if (!await _auth.CanLocationAsync(actor, recipient)) continue;
					authorizedRecipients.Add(recipient);
					var issuance = await RevealAsync(actor, stored); var item = Decode<InventoryItemContent>(await Item(issuance.ItemId));
					Add(("M5IssuanceId", issuance.Id), ("Name", item.Name), ("UnitOfMeasure", item.UnitOfMeasure), ("M5PersonId", issuance.IssuedToUserId), ("M5UnitId", issuance.IssuedToUnitId),
						("Location", await LocationName(issuance.LocationId, true)), ("M5IssuedOn", issuance.IssuedOn), ("Amount", issuance.Quantity), ("M5ReturnedQuantity", issuance.ReturnedQuantity),
						("M5OutstandingQuantity", issuance.Status is 0 or 2 ? issuance.Quantity - issuance.ReturnedQuantity : 0), ("ExpectedReturn", issuance.ExpectedReturnOn),
						("M5ReturnedOn", issuance.ReturnedOn), ("Status", (InventoryIssuanceStatus)issuance.Status), ("Note", JObject.Parse(issuance.Content ?? "{}").Value<string>("Note")));
				}
			}
			else
			{
				Columns("RecordUsageRecordedUtc", "RecordUsageLedgerEntry", "Name", "UnitOfMeasure", "Movement", "Amount", "FromLocation", "ToLocation", "M5Performer", "M5ReversesTransaction");
				if (input.Kind == InventoryReportKind.Usage) report.Columns.AddRange(new[] { "RecordUsageType", "M5UsageSource", "M5SourceId", "M5CallId", "M5RevisionId", "Note" });
				if (input.Kind == InventoryReportKind.ControlledSubstanceLog) report.Columns.AddRange(new[] { "SerialNumber", "Batch", "M5DeaSchedule", "M5FromBefore", "M5FromAfter", "M5ToBefore", "M5ToAfter", "M5Witness", "M5WitnessedOn", "Attestation", "Note" });
				var transactions = await AllAsync<InventoryTransaction>(actor.DepartmentId);
				var byId = transactions.ToDictionary(x => x.Id);
				var usageByTransaction = new Dictionary<string, RecordInventoryUsage>();
				if (input.Kind == InventoryReportKind.Usage)
					foreach (var usage in await AllAsync<RecordInventoryUsage>(actor.DepartmentId))
					{
						if (!ItemMatches(usage.ItemId) || !byId.TryGetValue(usage.TransactionId, out var movement) || !InRange(movement.OccurredOn) || !await Authorized(usage)) continue;
						usageByTransaction[usage.TransactionId] = usage; authorizedSources.Add(usage);
					}
				bool UsageMovement(InventoryTransaction movement) => movement.TransactionType == (int)InventoryTransactionType.Consume || movement.TransactionType == (int)InventoryTransactionType.WriteOff
					|| movement.TransactionType == (int)InventoryTransactionType.Migrated && movement.FromLocationId != null && movement.ToLocationId == null;
				foreach (var stored in transactions.OrderBy(x => x.OccurredOn).ThenBy(x => x.EntryId).ThenBy(x => x.Id))
				{
					if (!ItemMatches(stored.ItemId) || !InRange(stored.OccurredOn)) continue;
					var original = stored.ReversesTransactionId != null && byId.TryGetValue(stored.ReversesTransactionId, out var reversed) ? reversed : null;
					usageByTransaction.TryGetValue(stored.Id, out var usage);
					if (input.Kind == InventoryReportKind.Usage && usage == null && !UsageMovement(stored) && (original == null || !UsageMovement(original))) continue;
					if (input.Kind == InventoryReportKind.TransferHistory && stored.TransactionType != (int)InventoryTransactionType.Transfer && original?.TransactionType != (int)InventoryTransactionType.Transfer) continue;
					if (!await Authorized(stored) || !await VisibleLocation(stored.FromLocationId, true) && !await VisibleLocation(stored.ToLocationId, true)) continue;
					var item = await Item(stored.ItemId);
					if (input.Kind == InventoryReportKind.ControlledSubstanceLog && !item.IsControlledSubstance) continue;
					var transaction = await RevealAsync(actor, stored); var details = JObject.Parse(transaction.Content ?? "{}"); var itemDetails = Decode<InventoryItemContent>(item);
					var quantity = input.Kind == InventoryReportKind.Usage && transaction.FromLocationId == null ? -transaction.Quantity : transaction.Quantity;
					var cells = new List<(string Key, object Value)> { ("RecordUsageRecordedUtc", transaction.OccurredOn), ("RecordUsageLedgerEntry", transaction.Id),
						("Name", details.Value<string>("ItemName") ?? itemDetails.Name), ("UnitOfMeasure", details.Value<string>("UnitOfMeasure") ?? itemDetails.UnitOfMeasure),
						("Movement", (InventoryTransactionType)transaction.TransactionType), ("Amount", quantity), ("FromLocation", await LocationName(transaction.FromLocationId, true)),
						("ToLocation", await LocationName(transaction.ToLocationId, true)), ("M5Performer", details.Value<string>("PerformerId") ?? transaction.CreatedBy), ("M5ReversesTransaction", transaction.ReversesTransactionId) };
					if (input.Kind == InventoryReportKind.Usage)
					{
						// The ledger is the only quantity source. Optional source metadata never adds another movement.
						var source = usage == null ? transaction.ReferenceType == (int)InventoryReferenceType.Legacy ? InventoryReferenceType.Legacy : InventoryReferenceType.None : InventoryReferenceType.RmsRecord;
						var note = usage == null ? transaction.ReferenceType == (int)InventoryReferenceType.RmsRecord ? null : details.Value<string>("Note") : Decode<InventoryLabel>(await RevealAsync(actor, usage)).Note;
						cells.AddRange(new (string, object)[] { ("RecordUsageType", usage == null ? null : (object)(InventoryUsageType)usage.UsageType), ("M5UsageSource", source),
							("M5SourceId", usage?.SourceId), ("M5CallId", usage?.CallId), ("M5RevisionId", usage?.RmsRevisionId), ("Note", note) });
					}
					if (input.Kind == InventoryReportKind.ControlledSubstanceLog)
						cells.AddRange(new (string, object)[] { ("SerialNumber", details.Value<string>("SerialNumber")), ("Batch", await LotNumber(transaction.LotId)), ("M5DeaSchedule", itemDetails.DeaSchedule),
							("M5FromBefore", transaction.FromQuantityBefore), ("M5FromAfter", transaction.FromQuantityAfter), ("M5ToBefore", transaction.ToQuantityBefore), ("M5ToAfter", transaction.ToQuantityAfter),
							("M5Witness", details.Value<string>("WitnessUserId")), ("M5WitnessedOn", details.Value<DateTime?>("WitnessedOn")), ("Attestation", details.Value<string>("Attestation")), ("Note", details.Value<string>("Note")) });
					Add(cells.ToArray());
				}
			}
			foreach (var location in locations.Keys) await LocationAsync(actor, location.Id, historical: location.Historical);
			foreach (var usage in authorizedSources) await AuthorizeRowAsync(actor, usage);
			foreach (var recipient in authorizedRecipients) if (!await _auth.CanLocationAsync(actor, recipient)) throw new InventoryException(403, "PermissionRequired");
			return report;
		}
	}
}
