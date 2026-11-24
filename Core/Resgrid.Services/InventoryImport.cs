using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Columns = Resgrid.Services.InventoryImportCsv.Columns;

namespace Resgrid.Services
{
	/// <summary>
	/// Inventory import: seeds the catalog and the quantity on hand from a file (<see cref="InventoryImportCsv"/>).
	/// <para>
	/// One plan serves both steps. Preview builds it outside any transaction and returns its hash; apply builds it again
	/// under the department's posting lock, refuses when the hash differs (someone moved stock or edited an item in
	/// between), then writes everything in one operation: categories, locations, items, par levels, lots, assets, and one
	/// ledger posting per quantity change. Quantities are absolute (the file is a count): a row adds a Receive for the
	/// shortfall or an Adjust for the excess, so importing a corrected file again only posts the difference.
	/// </para>
	/// <para>
	/// New rows get identities derived from the request id, so the plan (and its hash) is the same on both steps and a
	/// replay of an applied request returns the first run's receipt. Controlled substances are refused: they need an
	/// independent witness, which a bulk file cannot carry.
	/// </para>
	/// </summary>
	public sealed partial class InventoryModernizationService : IInventoryImportService
	{
		private const string DefaultImportUnitOfMeasure = "unit";

		private sealed class ImportCategory { public string Id; public string ParentId; public string Name; }
		private sealed class ImportLocation { public string Id; public int Type; public int? GroupId; public int? UnitId; public string ParentId; public string Name; }
		private sealed class ImportItem
		{
			public string Id; public int Revision; public InventoryImportItemAction Action; public string CategoryId;
			public InventoryTrackingMode Tracking; public bool RequiresLot; public bool RequiresExpiration; public bool IsKit; public bool IsActive = true;
			public InventoryItemContent Details; public List<string> Changed = new();
		}
		private sealed class ImportLot { public string Id; public string ItemId; public string LotNumber; public DateTime? ExpiresOn; }
		private sealed class ImportAsset { public string Id; public string ItemId; public string LotId; public DateTime? ExpiresOn; public InventoryAssetContent Details; }
		private sealed class ImportPar { public string ItemId; public string LocationId; public decimal Minimum; public bool Changed; }
		private sealed class ImportPlan
		{
			public List<InventoryImportRowResult> Results = new();
			public List<ImportCategory> Categories = new();
			public List<ImportLocation> Locations = new();
			public List<ImportItem> Items = new();
			public List<ImportLot> Lots = new();
			public List<ImportAsset> Assets = new();
			public List<InventoryPosting> Lines = new();
			public List<ImportPar> Pars = new();
			public InventoryImportTotals Totals = new();
			[JsonIgnore] public Dictionary<string, (decimal Value, int Uncosted)> Added = new(StringComparer.Ordinal);
		}
		/// <summary>One read of the department's inventory, so a 1,000-row plan does not re-read the catalog per row.</summary>
		private sealed class ImportContext
		{
			public int DepartmentId; public string RequestId;
			public List<InventoryItem> Items; public Dictionary<string, InventoryItemContent> ItemDetails;
			public List<InventoryCategory> Categories; public Dictionary<string, string> CategoryNames;
			public Dictionary<string, InventoryLocation> Locations; public Dictionary<string, string> LocationNames; public HashSet<string> Unplaceable;
			public List<InventoryLot> Lots; public Dictionary<string, InventoryLotContent> LotDetails;
			public List<InventoryStock> Stocks; public ILookup<string, InventoryStock> StocksByItem;
			public Dictionary<(string ItemId, string LocationId, string LotId), decimal> OnHand;
			public List<InventoryAsset> Assets; public Dictionary<string, InventoryAssetContent> AssetDetails; public ILookup<string, InventoryAsset> AssetsByItem;
			public List<InventoryParLevel> Pars;
			public List<Unit> Units; public List<DepartmentGroup> Groups;
			public Dictionary<string, string> Access = new(StringComparer.Ordinal);
			public HashSet<(string, string, string)> Lines = new();
			public HashSet<(string, string)> Serials = new();
		}

		private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
		private static string ImportKey(string value) => (value ?? "").Trim().ToUpperInvariant();

		/// <summary>
		/// A stable identity for a row the import creates: the same department, request and key always give the same id. Row ids
		/// are unique across departments and the request id comes from the client, so the department is part of the input.
		/// </summary>
		private static string ImportId(int departmentId, string requestId, string key)
		{
			var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("inventory-import:" + departmentId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + requestId + ":" + key))[..16];
			bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
			return new Guid(bytes).ToString("D");
		}

		private static string ImportHash(InventoryActor actor, InventoryImportInput input, ImportPlan plan) => Fingerprint(new
		{
			actor.DepartmentId, actor.UserId, input.RequestId, plan.Results, plan.Categories, plan.Locations, plan.Items, plan.Lots, plan.Assets, plan.Lines, plan.Pars
		});

		public async Task<InventoryImportPreview> PreviewImportAsync(InventoryActor actor, InventoryImportInput input)
		{
			await _auth.RequireAsync(actor, true);
			if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
			// Applying writes protected ledger notes; find out now rather than after the person has reviewed the whole file.
			var preflight = await _write.PreflightWriteAsync(actor.DepartmentId, actor.GrantToken, actor.UserId, false);
			if (preflight?.Success != true) throw new InventoryException(403, "ProtectedDataRequired");
			var plan = await PlanImportAsync(actor, input);
			return new InventoryImportPreview
			{
				PreviewHash = ImportHash(actor, input, plan), Rows = plan.Results, IgnoredColumns = input.IgnoredColumns ?? new(), Totals = plan.Totals,
				ErrorRows = plan.Results.Count(r => r.ErrorCode != null),
				AddedValue = plan.Added.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new InventoryValuationTotal { CurrencyCode = a.Key.Length == 0 ? null : a.Key, KnownValue = Money(a.Value.Value), UncostedRows = a.Value.Uncosted }).ToList()
			};
		}

		public async Task<InventoryImportResult> ApplyImportAsync(InventoryActor actor, InventoryImportInput input)
		{
			if (input?.Rows == null) throw new InventoryException(400, "ImportFileInvalid");
			Id(input.RequestId);
			var ran = false;
			var result = await TransactionAsync(actor, events => OperationAsync(actor, input.RequestId, new { Kind = "Import", input.Rows }, async (operation, pending) =>
			{
				ran = true;
				var plan = await PlanImportAsync(actor, input);
				if (plan.Results.Any(r => r.ErrorCode != null)) throw new InventoryException(400, "ImportHasErrors");
				if (!string.Equals(ImportHash(actor, input, plan), input.PreviewHash, StringComparison.Ordinal)) throw new InventoryException(409, "ImportPreviewChanged");
				return await ApplyImportPlanAsync(actor, operation, plan, pending);
			}, events));
			return new InventoryImportResult { OperationId = result.OperationId, AlreadyApplied = !ran, Totals = result.Import ?? new InventoryImportTotals(), TransactionCount = result.TransactionIds?.Count ?? 0 };
		}

		private async Task<InventoryResult> ApplyImportPlanAsync(InventoryActor actor, InventoryOperation operation, ImportPlan plan, List<long> events)
		{
			foreach (var category in plan.Categories)
			{
				var row = New<InventoryCategory>(actor); row.Id = category.Id; row.ParentCategoryId = category.ParentId;
				row.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = category.Name });
				await SaveAsync(actor, row); await AuditAsync(actor, row, "InventoryCategorySaved");
			}
			foreach (var location in plan.Locations)
			{
				var row = New<InventoryLocation>(actor); row.Id = location.Id; row.LocationType = location.Type; row.GroupId = location.GroupId; row.UnitId = location.UnitId; row.ParentLocationId = location.ParentId;
				await _auth.ValidateHolderAsync(actor, row);
				if (row.ParentLocationId != null) await LocationAsync(actor, row.ParentLocationId, true, PermissionTypes.AdjustInventory);
				row.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = location.Name });
				await SaveAsync(actor, row); await AuditAsync(actor, row, "InventoryLocationSaved");
			}
			var items = new Dictionary<string, InventoryItem>(StringComparer.Ordinal);
			foreach (var planned in plan.Items)
			{
				InventoryItem row;
				if (planned.Action == InventoryImportItemAction.Create)
				{
					row = New<InventoryItem>(actor); row.Id = planned.Id; row.CategoryId = planned.CategoryId; row.TrackingMode = (int)planned.Tracking;
					row.RequiresLotTracking = planned.RequiresLot; row.RequiresExpiration = planned.RequiresExpiration; row.IsActive = true;
					row.Content = JsonConvert.SerializeObject(planned.Details);
					await SaveAsync(actor, row); await AuditAsync(actor, row, "InventoryItemSaved");
				}
				else
				{
					row = await _store.GetAsync<InventoryItem>(actor.DepartmentId, planned.Id);
					if (row == null || row.IsDeleted || row.Revision != planned.Revision) throw new InventoryException(409, "ImportPreviewChanged");
					if (planned.Action == InventoryImportItemAction.Update)
					{
						row.CategoryId = planned.CategoryId; row.Content = JsonConvert.SerializeObject(planned.Details);
						await SaveAsync(actor, row, false); await AuditAsync(actor, row, "InventoryItemSaved");
					}
				}
				items[row.Id] = row;
			}
			foreach (var group in plan.Pars.Where(p => p.Changed).GroupBy(p => p.ItemId))
				await SaveParLevelsAsync(actor, items[group.Key], group.Select(p => new InventoryParLevelInput { LocationId = p.LocationId, MinimumQuantity = p.Minimum }).ToList());
			foreach (var lot in plan.Lots)
			{
				var row = New<InventoryLot>(actor); row.Id = lot.Id; row.ItemId = lot.ItemId; row.ExpiresOn = lot.ExpiresOn; row.ReceivedOn = Now;
				row.Content = JsonConvert.SerializeObject(new InventoryLotContent { LotNumber = lot.LotNumber });
				await SaveAsync(actor, row); await AuditAsync(actor, row, "InventoryLotCreated");
			}
			foreach (var asset in plan.Assets)
				await CreateUnreceivedAssetAsync(actor, items[asset.ItemId], asset.Id, asset.LotId, asset.ExpiresOn, asset.Details);
			var result = new InventoryResult { OperationId = operation.Id };
			if (plan.Lines.Count > 0)
			{
				// The posting contract validates at most 100 lines a call; the whole file still posts in this one operation.
				for (var index = 0; index < plan.Lines.Count; index += 100)
					await ValidateCommandAsync(actor, new InventoryCommand { RequestId = operation.RequestId, Lines = plan.Lines.Skip(index).Take(100).ToList() });
				result = await PostLinesAsync(actor, operation, new InventoryCommand { RequestId = operation.RequestId, Lines = plan.Lines }, events);
				foreach (var line in plan.Lines.Where(l => l.AssetId != null && items[l.ItemId].IsKit)) await CreateKitContainerAsync(actor, line.AssetId, null);
			}
			// Posted items had their alerts refreshed by the posting; catalog-only changes (levels, par minimums) need it too.
			var posted = plan.Lines.Select(l => l.ItemId).ToHashSet(StringComparer.Ordinal);
			var changed = plan.Items.Where(i => i.Action != InventoryImportItemAction.Existing).Select(i => i.Id).Concat(plan.Pars.Where(p => p.Changed).Select(p => p.ItemId));
			foreach (var itemId in changed.Distinct().Where(id => !posted.Contains(id))) await RefreshLowStockAsync(actor, itemId, events);
			result.Import = plan.Totals; result.OutboxIds = events.ToList();
			return result;
		}

		private async Task<ImportContext> ImportContextAsync(InventoryActor actor, string requestId)
		{
			var context = new ImportContext { DepartmentId = actor.DepartmentId, RequestId = requestId };
			context.Items = await AllAsync<InventoryItem>(actor.DepartmentId);
			context.ItemDetails = context.Items.ToDictionary(i => i.Id, i => Decode<InventoryItemContent>(i), StringComparer.Ordinal);
			context.Categories = (await AllAsync<InventoryCategory>(actor.DepartmentId)).Where(c => !c.IsDeleted).ToList();
			context.CategoryNames = context.Categories.ToDictionary(c => c.Id, c => Decode<InventoryLabel>(c).Name, StringComparer.Ordinal);
			context.Locations = (await AllAsync<InventoryLocation>(actor.DepartmentId)).Where(l => !l.IsDeleted).ToDictionary(l => l.Id, StringComparer.Ordinal);
			context.LocationNames = context.Locations.Values.ToDictionary(l => l.Id, l => Decode<InventoryLabel>(l).Name, StringComparer.Ordinal);
			// Stock cannot be placed with a person (that is an issuance) or inside a kit (that moves with the kit).
			context.Unplaceable = new HashSet<string>(StringComparer.Ordinal);
			foreach (var location in context.Locations.Values)
			{
				var current = location; var seen = new HashSet<string>(StringComparer.Ordinal);
				while (current != null && seen.Add(current.Id) && seen.Count <= 32)
				{
					if (current.ContainerAssetId != null || current.LocationType is (int)InventoryLocationType.Personnel or (int)InventoryLocationType.Container) { context.Unplaceable.Add(location.Id); break; }
					current = current.ParentLocationId != null && context.Locations.TryGetValue(current.ParentLocationId, out var parent) ? parent : null;
				}
			}
			context.Lots = await AllAsync<InventoryLot>(actor.DepartmentId);
			context.LotDetails = context.Lots.ToDictionary(l => l.Id, l => Decode<InventoryLotContent>(l), StringComparer.Ordinal);
			context.Stocks = (await AllAsync<InventoryStock>(actor.DepartmentId)).Where(s => !s.IsDeleted).ToList();
			// Indexed once: planning reads one balance per row and export one item's stock at a time.
			context.StocksByItem = context.Stocks.ToLookup(s => s.ItemId, StringComparer.Ordinal);
			context.OnHand = context.Stocks.GroupBy(s => (s.ItemId, s.LocationId, s.LotId)).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
			context.Assets = await AllAsync<InventoryAsset>(actor.DepartmentId);
			context.AssetDetails = context.Assets.ToDictionary(a => a.Id, a => Decode<InventoryAssetContent>(a), StringComparer.Ordinal);
			context.AssetsByItem = context.Assets.ToLookup(a => a.ItemId, StringComparer.Ordinal);
			context.Pars = (await AllAsync<InventoryParLevel>(actor.DepartmentId)).Where(p => !p.IsDeleted).ToList();
			context.Units = (await _units.GetUnitsForDepartmentAsync(actor.DepartmentId) ?? new List<Unit>()).Where(u => u != null && !u.IsDeleted && u.DepartmentId == actor.DepartmentId).ToList();
			context.Groups = (await _groups.GetAllGroupsForDepartmentAsync(actor.DepartmentId) ?? new List<DepartmentGroup>()).Where(g => g != null && g.DepartmentId == actor.DepartmentId).ToList();
			return context;
		}

		private async Task<ImportPlan> PlanImportAsync(InventoryActor actor, InventoryImportInput input)
		{
			if (input?.Rows == null || input.Rows.Count is < 1 or > InventoryImportLimits.MaxRows || input.Rows.Any(r => r == null || r.RowNumber < 1)
				|| input.Rows.Select(r => r.RowNumber).Distinct().Count() != input.Rows.Count) throw new InventoryException(400, "ImportFileInvalid");
			Id(input.RequestId);
			await _auth.RequireAsync(actor, true);
			var context = await ImportContextAsync(actor, input.RequestId);
			var plan = new ImportPlan();
			var results = new Dictionary<int, InventoryImportRowResult>();
			foreach (var row in input.Rows.OrderBy(r => r.RowNumber))
			{
				var result = new InventoryImportRowResult { RowNumber = row.RowNumber, ItemName = row.Item?.Trim() };
				plan.Results.Add(result); results[row.RowNumber] = result;
				if (row.ParseError != null) Fail(result, row.ParseError, row.ParseColumn);
				else if (string.IsNullOrWhiteSpace(row.Item)) Fail(result, "ImportItemRequired", Columns.Item);
				// The row's note becomes the posting note: refuse here what the posting would refuse at apply.
				else if (row.Note?.Length > InventoryImportLimits.MaxTextLength) Fail(result, "ImportValueTooLong", Columns.Note);
			}
			var valid = input.Rows.OrderBy(r => r.RowNumber).Where(r => results[r.RowNumber].ErrorCode == null).ToList();
			var planned = new Dictionary<string, ImportItem>(StringComparer.Ordinal);
			foreach (var group in valid.GroupBy(r => ImportKey(r.Item)))
			{
				var item = await PlanImportItemAsync(actor, context, plan, group.ToList(), results);
				if (item != null) planned[group.Key] = item;
			}
			var described = new HashSet<string>(StringComparer.Ordinal);
			foreach (var row in valid)
			{
				var result = results[row.RowNumber];
				if (result.ErrorCode != null || !planned.TryGetValue(ImportKey(row.Item), out var item)) continue;
				result.ItemAction = item.Action; result.CurrencyCode = item.Details.CurrencyCode;
				if (described.Add(item.Id)) result.ChangedFields = item.Changed.ToList();
				await PlanImportStockAsync(actor, context, plan, row, item, result);
			}
			foreach (var result in plan.Results.Where(r => r.ErrorCode == null))
			{
				if (result.StockAction == InventoryImportStockAction.Receive || result.StockAction == InventoryImportStockAction.CreateAsset) plan.Totals.Receipts++;
				else if (result.StockAction == InventoryImportStockAction.Decrease) plan.Totals.Decreases++;
				else if (result.StockAction is InventoryImportStockAction.Unchanged or InventoryImportStockAction.AssetExists) plan.Totals.Unchanged++;
			}
			plan.Totals.ItemsCreated = plan.Items.Count(i => i.Action == InventoryImportItemAction.Create);
			plan.Totals.ItemsUpdated = plan.Items.Count(i => i.Action == InventoryImportItemAction.Update);
			plan.Totals.CategoriesCreated = plan.Categories.Count; plan.Totals.LocationsCreated = plan.Locations.Count;
			plan.Totals.LotsCreated = plan.Lots.Count; plan.Totals.AssetsCreated = plan.Assets.Count; plan.Totals.ParLevelsSet = plan.Pars.Count(p => p.Changed);
			return plan;
		}

		private static void Fail(InventoryImportRowResult result, string code, string column = null)
		{
			if (result.ErrorCode != null) return;
			result.ErrorCode = code; result.ErrorColumn = column;
		}

		private static void Warn(InventoryImportRowResult result, string code) { if (!result.Warnings.Contains(code)) result.Warnings.Add(code); }

		/// <summary>
		/// Merges the item columns of every row naming one item (a conflicting value fails the row that brings it), then
		/// matches the item by name: an existing item takes the file's values for its descriptive fields; a new one is checked
		/// with the editor's own rules.
		/// </summary>
		private async Task<ImportItem> PlanImportItemAsync(InventoryActor actor, ImportContext context, ImportPlan plan, List<InventoryImportRow> rows, Dictionary<int, InventoryImportRowResult> results)
		{
			string MergeText(Func<InventoryImportRow, string> get, string column)
			{
				string merged = null;
				foreach (var row in rows)
				{
					var value = get(row)?.Trim();
					if (string.IsNullOrEmpty(value)) continue;
					if (merged == null) merged = value;
					else if (!Same(merged, value)) Fail(results[row.RowNumber], "ImportItemConflict", column);
				}
				return merged;
			}
			T? Merge<T>(Func<InventoryImportRow, T?> get, string column) where T : struct
			{
				T? merged = null;
				foreach (var row in rows)
				{
					var value = get(row);
					if (!value.HasValue) continue;
					if (!merged.HasValue) merged = value;
					else if (!Equals(merged.Value, value.Value)) Fail(results[row.RowNumber], "ImportItemConflict", column);
				}
				return merged;
			}
			void FailAll(string code, string column) { foreach (var row in rows) Fail(results[row.RowNumber], code, column); }

			var name = rows[0].Item.Trim();
			var code = MergeText(r => r.Code, Columns.Code); var barcode = MergeText(r => r.Barcode, Columns.Barcode);
			var categoryPath = MergeText(r => r.Category, Columns.Category); var description = MergeText(r => r.Description, Columns.Description);
			var unitOfMeasure = MergeText(r => r.UnitOfMeasure, Columns.UnitOfMeasure); var currency = MergeText(r => r.Currency, Columns.Currency);
			var tracking = Merge(r => r.Tracking, Columns.Tracking); var requiresLot = Merge(r => r.RequiresLot, Columns.RequiresLot);
			var requiresExpiration = Merge(r => r.RequiresExpiration, Columns.RequiresExpiration);
			var minLevel = Merge(r => r.MinLevel, Columns.MinLevel); var maxLevel = Merge(r => r.MaxLevel, Columns.MaxLevel);
			var reorderPoint = Merge(r => r.ReorderPoint, Columns.ReorderPoint); var reorderQuantity = Merge(r => r.ReorderQuantity, Columns.ReorderQuantity);
			var firstCost = rows.Select(r => r.UnitCost).FirstOrDefault(c => c.HasValue);
			var existing = context.Items.Where(i => !i.IsDeleted && Same(context.ItemDetails[i.Id].Name, name)).OrderBy(i => i.CreatedOn).FirstOrDefault();
			var column = Columns.Item;
			try
			{
				Text(name);
				ImportItem item;
				if (existing != null)
				{
					if (existing.IsControlledSubstance) { FailAll("ImportControlledSubstance", Columns.Item); return null; }
					if (tracking.HasValue && (int)tracking.Value != existing.TrackingMode) { FailAll("ImportItemTrackingMismatch", Columns.Tracking); return null; }
					if (requiresLot.HasValue && requiresLot.Value != existing.RequiresLotTracking) { FailAll("ImportItemTrackingMismatch", Columns.RequiresLot); return null; }
					if (requiresExpiration.HasValue && requiresExpiration.Value != existing.RequiresExpiration) { FailAll("ImportItemTrackingMismatch", Columns.RequiresExpiration); return null; }
					var details = JsonConvert.DeserializeObject<InventoryItemContent>(JsonConvert.SerializeObject(context.ItemDetails[existing.Id]));
					item = new ImportItem { Id = existing.Id, Revision = existing.Revision, Action = InventoryImportItemAction.Existing, CategoryId = existing.CategoryId,
						Tracking = (InventoryTrackingMode)existing.TrackingMode, RequiresLot = existing.RequiresLotTracking, RequiresExpiration = existing.RequiresExpiration,
						IsKit = existing.IsKit, IsActive = existing.IsActive, Details = details };
					void Change<T>(string field, T current, T value, Action<T> set)
					{
						if (value == null || Equals(current, value)) return;
						set(value); item.Changed.Add(field);
					}
					Change(Columns.Code, details.Code, code, v => details.Code = v);
					Change(Columns.Barcode, details.Barcode, barcode, v => details.Barcode = v);
					Change(Columns.Description, details.Description, description, v => details.Description = v);
					Change(Columns.UnitOfMeasure, details.UnitOfMeasure, unitOfMeasure, v => details.UnitOfMeasure = v);
					Change(Columns.MinLevel, details.MinLevel, minLevel, v => details.MinLevel = v);
					Change(Columns.MaxLevel, details.MaxLevel, maxLevel, v => details.MaxLevel = v);
					Change(Columns.ReorderPoint, details.ReorderPoint, reorderPoint, v => details.ReorderPoint = v);
					Change(Columns.ReorderQuantity, details.ReorderQuantity, reorderQuantity, v => details.ReorderQuantity = v);
					if (categoryPath != null)
					{
						column = Columns.Category;
						var (categoryId, created) = ResolveImportCategory(context, plan, categoryPath);
						if (created) results[rows[0].RowNumber].CreatesCategory = true;
						if (categoryId != existing.CategoryId) { item.CategoryId = categoryId; item.Changed.Add(Columns.Category); }
					}
					if (currency != null)
					{
						column = Columns.Currency;
						var normalized = Currency(currency);
						if (normalized != details.CurrencyCode)
						{
							if (details.CurrencyCode != null && ((await _store.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ItemId", existing.Id)).Count != 0 || await HasOpenPurchaseLineAsync(actor, existing.Id)))
								throw new InventoryException(409, "ItemCurrencyLocked");
							details.CurrencyCode = normalized; item.Changed.Add(Columns.Currency);
						}
					}
					// An item with no cost at all takes the file's first; a priced item's cost is never overwritten from a row.
					if (firstCost.HasValue && !details.DefaultUnitCost.HasValue && !details.AverageUnitCost.HasValue)
					{
						column = Columns.UnitCost; Cost(firstCost);
						details.DefaultUnitCost = firstCost; item.Changed.Add(Columns.UnitCost);
					}
					if (item.Changed.Count > 0) item.Action = InventoryImportItemAction.Update;
				}
				else
				{
					var mode = tracking ?? (rows.Any(r => r.SerialNumber != null) ? InventoryTrackingMode.Serialized : InventoryTrackingMode.Bulk);
					var expiring = requiresExpiration ?? false;
					// A bulk item that expires is tracked by dated lots; say so for the person rather than refusing the file.
					var lots = requiresLot ?? (mode == InventoryTrackingMode.Bulk && expiring);
					column = Columns.Currency;
					var details = new InventoryItemContent { Name = name, Code = code, Barcode = barcode, Description = description, UnitOfMeasure = unitOfMeasure ?? DefaultImportUnitOfMeasure,
						MinLevel = minLevel, MaxLevel = maxLevel, ReorderPoint = reorderPoint, ReorderQuantity = reorderQuantity, DefaultUnitCost = firstCost, CurrencyCode = Currency(currency) };
					if (unitOfMeasure == null) Warn(results[rows[0].RowNumber], "ImportUnitOfMeasureDefaulted");
					item = new ImportItem { Id = ImportId(context.DepartmentId, context.RequestId,"item:" + ImportKey(name)), Action = InventoryImportItemAction.Create, Tracking = mode, RequiresLot = lots, RequiresExpiration = expiring, Details = details };
					if (categoryPath != null)
					{
						column = Columns.Category;
						var (categoryId, created) = ResolveImportCategory(context, plan, categoryPath);
						if (created) results[rows[0].RowNumber].CreatesCategory = true;
						item.CategoryId = categoryId;
					}
				}
				column = item.Details.UnitOfMeasure?.Length > 80 ? Columns.UnitOfMeasure : item.Details.Description?.Length > 16000 ? Columns.Description
					: item.Details.Code?.Length > 100 ? Columns.Code : item.Details.Barcode?.Length > 250 ? Columns.Barcode : requiresExpiration == true ? Columns.RequiresExpiration : Columns.Item;
				ValidateItemInput(new InventoryItemInput { TrackingMode = item.Tracking, IsKit = item.IsKit, RequiresLotTracking = item.RequiresLot, RequiresExpiration = item.RequiresExpiration, Details = item.Details });
				column = Columns.UnitCost; Cost(item.Details.DefaultUnitCost);
				foreach (var (value, levelColumn) in new[] { (item.Details.MinLevel, Columns.MinLevel), (item.Details.MaxLevel, Columns.MaxLevel), (item.Details.ReorderPoint, Columns.ReorderPoint), (item.Details.ReorderQuantity, Columns.ReorderQuantity) })
				{
					column = levelColumn;
					if (value < 0 || value > 1_000_000_000m || value.HasValue && decimal.Round(value.Value, 6) != value) throw new InventoryException(400, "InvalidItem");
				}
				if (!string.IsNullOrWhiteSpace(item.Details.Barcode))
				{
					column = Columns.Barcode;
					var taken = context.Items.Any(i => !i.IsDeleted && i.Id != item.Id && Same(context.ItemDetails[i.Id].Barcode, item.Details.Barcode))
						|| plan.Items.Any(i => i.Id != item.Id && Same(i.Details.Barcode, item.Details.Barcode));
					if (taken) throw new InventoryException(409, "DuplicateItem");
				}
				plan.Items.Add(item);
				return item;
			}
			catch (InventoryException error)
			{
				FailAll(error.Code, column);
				return null;
			}
		}

		/// <summary>Finds or plans the category for a "Parent &gt; Child" path, one level at a time.</summary>
		private static (string Id, bool Created) ResolveImportCategory(ImportContext context, ImportPlan plan, string path)
		{
			var segments = path.Split('>').Select(s => s.Trim()).ToList();
			if (segments.Count > 32 || segments.Any(s => s.Length == 0)) throw new InventoryException(400, "InvalidCategoryHierarchy");
			string parent = null; var created = false;
			foreach (var segment in segments)
			{
				Text(segment);
				var existing = context.Categories.Where(c => c.ParentCategoryId == parent && Same(context.CategoryNames[c.Id], segment)).OrderBy(c => c.CreatedOn).FirstOrDefault();
				if (existing != null) { parent = existing.Id; continue; }
				var planned = plan.Categories.FirstOrDefault(c => c.ParentId == parent && Same(c.Name, segment));
				if (planned == null)
				{
					planned = new ImportCategory { Id = ImportId(context.DepartmentId, context.RequestId,"category:" + parent + ":" + ImportKey(segment)), ParentId = parent, Name = segment };
					plan.Categories.Add(planned); created = true;
				}
				parent = planned.Id;
			}
			return (parent, created);
		}

		private async Task PlanImportStockAsync(InventoryActor actor, ImportContext context, ImportPlan plan, InventoryImportRow row, ImportItem item, InventoryImportRowResult result)
		{
			var stock = row.Quantity.HasValue || row.SerialNumber != null;
			if (!stock && !row.ParLevel.HasValue)
			{
				if (row.Location != null || row.Station != null || row.Unit != null) Warn(result, "ImportLocationIgnored");
				return;
			}
			var column = Columns.Location;
			try
			{
				var (locationId, createdLocation) = ResolveImportLocation(context, plan, row);
				result.CreatesLocation = createdLocation;
				var denied = await ImportLocationAccessAsync(actor, context, plan, locationId);
				if (denied != null) throw new InventoryException(403, denied);
				// Named only once the person may use it: a refused row must not show a location they cannot see.
				result.LocationName = ImportLocationLabel(context, plan, locationId);
				if (row.ParLevel.HasValue)
				{
					column = Columns.ParLevel;
					var minimum = row.ParLevel.Value;
					if (minimum < 0 || minimum > 1_000_000_000m || decimal.Round(minimum, 6) != minimum) throw new InventoryException(400, "InvalidParLevel");
					var same = plan.Pars.FirstOrDefault(p => p.ItemId == item.Id && p.LocationId == locationId);
					if (same != null && same.Minimum != minimum) throw new InventoryException(409, "ImportItemConflict");
					if (same == null)
					{
						var current = context.Pars.FirstOrDefault(p => p.ItemId == item.Id && p.LocationId == locationId);
						plan.Pars.Add(new ImportPar { ItemId = item.Id, LocationId = locationId, Minimum = minimum, Changed = current?.MinimumQuantity != minimum });
					}
					result.ParLevel = minimum;
				}
				if (!stock) return;
				if (row.SerialNumber != null) PlanImportAsset(context, plan, row, item, result, locationId, c => column = c);
				else PlanImportQuantity(context, plan, row, item, result, locationId, c => column = c);
			}
			// A station or unit that cannot be found is the fault of that column, not of Location.
			catch (InventoryException error)
			{
				Fail(result, error.Code, error.Code switch
				{
					"ImportUnitNotFound" or "ImportUnitAmbiguous" => Columns.Unit,
					"ImportStationNotFound" or "ImportStationAmbiguous" => Columns.Station,
					_ => column
				});
			}
		}

		private static void PlanImportQuantity(ImportContext context, ImportPlan plan, InventoryImportRow row, ImportItem item, InventoryImportRowResult result, string locationId, Action<string> at)
		{
			at(Columns.SerialNumber);
			if (item.Tracking == InventoryTrackingMode.Serialized) throw new InventoryException(400, "ImportSerialRequired");
			at(Columns.Quantity); Quantity(row.Quantity.Value, true);
			var lot = ResolveImportLot(context, plan, row, item, at);
			result.LotNumber = lot.Id == null ? null : lot.Number; result.CreatesLot = lot.Created;
			at(Columns.Quantity);
			if (!context.Lines.Add((item.Id, locationId, lot.Id))) throw new InventoryException(400, "ImportDuplicateLine");
			var current = context.OnHand.TryGetValue((item.Id, locationId, lot.Id), out var onHand) ? onHand : 0m;
			var change = row.Quantity.Value - current;
			result.CurrentQuantity = current; result.NewQuantity = row.Quantity.Value; result.Change = change;
			if (change == 0) { result.StockAction = InventoryImportStockAction.Unchanged; return; }
			if (!item.IsActive) throw new InventoryException(409, "ItemUnavailable");
			var note = row.Note ?? "Inventory import, row " + row.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
			if (change > 0)
			{
				at(Columns.UnitCost); Cost(row.UnitCost);
				if (lot.UnitCost.HasValue && row.UnitCost.HasValue && lot.UnitCost != row.UnitCost) throw new InventoryException(409, "LotCostMismatch");
				var cost = row.UnitCost ?? lot.UnitCost ?? item.Details.AverageUnitCost ?? item.Details.DefaultUnitCost;
				plan.Lines.Add(new InventoryPosting { ItemId = item.Id, LotId = lot.Id, ToLocationId = locationId, Quantity = change, Type = InventoryTransactionType.Receive, UnitCost = row.UnitCost, Note = note });
				result.StockAction = InventoryImportStockAction.Receive; result.UnitCost = cost;
				AddImportValue(plan, item, cost, change);
			}
			else
			{
				plan.Lines.Add(new InventoryPosting { ItemId = item.Id, LotId = lot.Id, FromLocationId = locationId, Quantity = -change, Type = InventoryTransactionType.Adjust, Note = note });
				result.StockAction = InventoryImportStockAction.Decrease;
			}
		}

		private static void PlanImportAsset(ImportContext context, ImportPlan plan, InventoryImportRow row, ImportItem item, InventoryImportRowResult result, string locationId, Action<string> at)
		{
			at(Columns.SerialNumber);
			if (item.Tracking == InventoryTrackingMode.Bulk) throw new InventoryException(400, "ImportSerialOnBulkItem");
			Text(row.SerialNumber); result.SerialNumber = row.SerialNumber;
			at(Columns.Quantity);
			if (row.Quantity.HasValue && row.Quantity != 1) throw new InventoryException(400, "SerializedQuantity");
			at(Columns.AssetTag);
			if (row.AssetTag?.Length > 250) throw new InventoryException(400, "InvalidAsset");
			at(Columns.SerialNumber);
			if (!context.Serials.Add((item.Id, ImportKey(row.SerialNumber)))) throw new InventoryException(409, "DuplicateSerial");
			var existing = context.Assets.FirstOrDefault(a => a.ItemId == item.Id && Same(context.AssetDetails[a.Id].SerialNumber, row.SerialNumber));
			if (existing != null)
			{
				// A recorded asset is never moved or revived by a file: transfers and status changes keep their own history.
				result.StockAction = InventoryImportStockAction.AssetExists;
				var live = !existing.IsDeleted && existing.Status is 0 or 1 or 2 or 3;
				if (!live) Warn(result, "ImportAssetRetired");
				else if (existing.CurrentLocationId != locationId) Warn(result, "ImportAssetElsewhere");
				result.CurrentQuantity = result.NewQuantity = live && existing.CurrentLocationId == locationId ? 1 : 0;
				return;
			}
			if (!item.IsActive) throw new InventoryException(409, "ItemUnavailable");
			var lot = ResolveImportLot(context, plan, row, item, at);
			result.LotNumber = lot.Id == null ? null : lot.Number; result.CreatesLot = lot.Created;
			var expires = row.Expires ?? lot.ExpiresOn;
			at(Columns.Expires);
			if (item.RequiresExpiration && !expires.HasValue) throw new InventoryException(400, "ExpiryRequired");
			at(Columns.UnitCost); Cost(row.UnitCost);
			if (lot.UnitCost.HasValue && row.UnitCost.HasValue && lot.UnitCost != row.UnitCost) throw new InventoryException(409, "LotCostMismatch");
			var id = ImportId(context.DepartmentId, context.RequestId,"asset:" + item.Id + ":" + ImportKey(row.SerialNumber));
			plan.Assets.Add(new ImportAsset { Id = id, ItemId = item.Id, LotId = lot.Id, ExpiresOn = expires,
				Details = new InventoryAssetContent { SerialNumber = row.SerialNumber.Trim(), AssetTag = row.AssetTag, AcquisitionCost = row.UnitCost } });
			plan.Lines.Add(new InventoryPosting { ItemId = item.Id, AssetId = id, LotId = lot.Id, ToLocationId = locationId, Quantity = 1, Type = InventoryTransactionType.Receive, UnitCost = row.UnitCost,
				Note = row.Note ?? "Inventory import, row " + row.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) });
			var cost = row.UnitCost ?? lot.UnitCost ?? item.Details.AverageUnitCost ?? item.Details.DefaultUnitCost;
			result.StockAction = InventoryImportStockAction.CreateAsset; result.CurrentQuantity = 0; result.NewQuantity = 1; result.Change = 1; result.UnitCost = cost;
			AddImportValue(plan, item, cost, 1);
		}

		private static void AddImportValue(ImportPlan plan, ImportItem item, decimal? unitCost, decimal quantity)
		{
			var key = item.Details.CurrencyCode ?? "";
			var current = plan.Added.TryGetValue(key, out var value) ? value : (0m, 0);
			plan.Added[key] = unitCost.HasValue ? (current.Item1 + unitCost.Value * quantity, current.Item2) : (current.Item1, current.Item2 + 1);
		}

		private static (string Id, string Number, bool Created, DateTime? ExpiresOn, decimal? UnitCost) ResolveImportLot(ImportContext context, ImportPlan plan, InventoryImportRow row, ImportItem item, Action<string> at)
		{
			at(Columns.Lot);
			if (row.Lot == null)
			{
				if (item.RequiresLot || item.RequiresExpiration && item.Tracking == InventoryTrackingMode.Bulk) throw new InventoryException(400, "LotRequired");
				return (null, null, false, null, null);
			}
			Text(row.Lot);
			var number = row.Lot.Trim();
			var existing = context.Lots.FirstOrDefault(l => l.ItemId == item.Id && Same(context.LotDetails[l.Id].LotNumber, number));
			if (existing != null)
			{
				if (existing.IsDeleted || item.RequiresExpiration && !existing.ExpiresOn.HasValue) throw new InventoryException(409, "LotMismatch");
				at(Columns.Expires);
				if (row.Expires.HasValue && existing.ExpiresOn?.Date != row.Expires.Value.Date) throw new InventoryException(409, "ImportLotExpiryMismatch");
				return (existing.Id, context.LotDetails[existing.Id].LotNumber, false, existing.ExpiresOn, context.LotDetails[existing.Id].UnitCost);
			}
			var planned = plan.Lots.FirstOrDefault(l => l.ItemId == item.Id && Same(l.LotNumber, number));
			at(Columns.Expires);
			if (planned != null)
			{
				if (row.Expires.HasValue && planned.ExpiresOn != row.Expires) throw new InventoryException(409, "ImportLotExpiryMismatch");
				return (planned.Id, planned.LotNumber, false, planned.ExpiresOn, null);
			}
			if (item.RequiresExpiration && !row.Expires.HasValue) throw new InventoryException(400, "ExpiryRequired");
			planned = new ImportLot { Id = ImportId(context.DepartmentId, context.RequestId,"lot:" + item.Id + ":" + ImportKey(number)), ItemId = item.Id, LotNumber = number, ExpiresOn = row.Expires };
			plan.Lots.Add(planned);
			return (planned.Id, number, true, planned.ExpiresOn, null);
		}

		// ── Locations ────────────────────────────────────────────────────────────────────────────────────────────────────
		// Existing and planned locations are walked together: a path that the file names twice is planned once.

		private static string ImportLocationName(ImportContext context, ImportPlan plan, string id)
			=> context.LocationNames.TryGetValue(id, out var name) ? name : plan.Locations.FirstOrDefault(l => l.Id == id)?.Name;

		private static string ImportLocationParent(ImportContext context, ImportPlan plan, string id)
			=> context.Locations.TryGetValue(id, out var location) ? location.ParentLocationId : plan.Locations.FirstOrDefault(l => l.Id == id)?.ParentId;

		/// <summary>Every location stock can be placed in, existing then planned.</summary>
		private static IEnumerable<string> ImportPlaceable(ImportContext context, ImportPlan plan)
			=> context.Locations.Keys.Where(id => !context.Unplaceable.Contains(id)).Concat(plan.Locations.Select(l => l.Id));

		private static string ImportLocationLabel(ImportContext context, ImportPlan plan, string id)
		{
			var names = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal); var top = id;
			for (var current = id; current != null && seen.Add(current) && seen.Count <= 32; current = ImportLocationParent(context, plan, current))
			{ names.Insert(0, ImportLocationName(context, plan, current)); top = current; }
			var root = context.Locations.TryGetValue(top, out var stored) ? (stored.LocationType, stored.UnitId, stored.GroupId)
				: plan.Locations.Where(l => l.Id == top).Select(l => (l.Type, l.UnitId, l.GroupId)).FirstOrDefault();
			var holder = root.Item1 == (int)InventoryLocationType.Unit ? context.Units.FirstOrDefault(u => u.UnitId == root.Item2)?.Name
				: root.Item1 == (int)InventoryLocationType.Station ? context.Groups.FirstOrDefault(g => g.DepartmentGroupId == root.Item3)?.Name : null;
			var path = string.Join(" > ", names);
			return holder == null || Same(holder, path) ? path : holder + " · " + path;
		}

		private static bool ImportUnder(ImportContext context, ImportPlan plan, string id, IReadOnlyCollection<string> roots)
		{
			var seen = new HashSet<string>(StringComparer.Ordinal);
			for (var current = id; current != null && seen.Add(current) && seen.Count <= 32; current = ImportLocationParent(context, plan, current))
				if (roots.Contains(current)) return true;
			return false;
		}

		private static string PlanImportLocation(ImportContext context, ImportPlan plan, InventoryLocationType type, int? groupId, int? unitId, string parentId, string name)
		{
			var id = ImportId(context.DepartmentId, context.RequestId,"location:" + (int)type + ":" + groupId + ":" + unitId + ":" + parentId + ":" + ImportKey(name));
			if (plan.Locations.All(l => l.Id != id)) plan.Locations.Add(new ImportLocation { Id = id, Type = (int)type, GroupId = groupId, UnitId = unitId, ParentId = parentId, Name = name });
			return id;
		}

		/// <summary>
		/// Resolves a row's Station / Unit / Location to one placeable location, planning the missing ones. With a station or
		/// unit, the holder's storage is the root and Location names a compartment inside it (or the root itself, by name).
		/// Without one, a single name is looked up at the top level, then anywhere; a "Warehouse &gt; Shelf A" path is
		/// walked from the top. A blank location uses the department's default location.
		/// </summary>
		private static (string Id, bool Created) ResolveImportLocation(ImportContext context, ImportPlan plan, InventoryImportRow row)
		{
			var segments = string.IsNullOrWhiteSpace(row.Location) ? new List<string>() : row.Location.Split('>').Select(s => s.Trim()).ToList();
			if (segments.Count > 16 || segments.Any(s => s.Length == 0)) throw new InventoryException(400, "InvalidLocationHierarchy");
			foreach (var segment in segments) Text(segment);
			if (row.Station != null && row.Unit != null) throw new InventoryException(400, "ImportStationOrUnit");
			var placeable = ImportPlaceable(context, plan).ToList();
			var created = false; string node; var rest = segments;
			if (row.Station != null || row.Unit != null)
			{
				InventoryLocationType type; int? unitId = null, groupId = null; string holderName;
				if (row.Unit != null)
				{
					var units = context.Units.Where(u => Same(u.Name, row.Unit)).ToList();
					if (units.Count == 0) throw new InventoryException(404, "ImportUnitNotFound");
					if (units.Count > 1) throw new InventoryException(409, "ImportUnitAmbiguous");
					type = InventoryLocationType.Unit; unitId = units[0].UnitId; holderName = units[0].Name.Trim();
				}
				else
				{
					var groups = context.Groups.Where(g => Same(g.Name, row.Station)).ToList();
					if (groups.Count == 0) throw new InventoryException(404, "ImportStationNotFound");
					if (groups.Count > 1) throw new InventoryException(409, "ImportStationAmbiguous");
					type = InventoryLocationType.Station; groupId = groups[0].DepartmentGroupId; holderName = groups[0].Name.Trim();
				}
				var roots = context.Locations.Values.Where(l => l.LocationType == (int)type && l.UnitId == unitId && l.GroupId == groupId && l.ParentLocationId == null && l.ContainerAssetId == null)
					.OrderBy(l => l.CreatedOn).Select(l => l.Id)
					.Concat(plan.Locations.Where(l => l.Type == (int)type && l.UnitId == unitId && l.GroupId == groupId && l.ParentId == null).Select(l => l.Id)).ToList();
				var named = rest.Count == 0 ? new List<string>() : roots.Where(r => Same(ImportLocationName(context, plan, r), rest[0])).ToList();
				if (named.Count == 1) { node = named[0]; rest = rest.Skip(1).ToList(); }
				else if (roots.Count == 1) node = roots[0];
				else if (roots.Count == 0)
				{
					if (rest.Count > 0 && Same(rest[0], holderName)) rest = rest.Skip(1).ToList();
					node = PlanImportLocation(context, plan, type, groupId, unitId, null, holderName); created = true;
				}
				else
				{
					// Several storage locations for one holder: a single name may still pick out exactly one place under them.
					var inside = rest.Count == 1 ? placeable.Where(id => Same(ImportLocationName(context, plan, id), rest[0]) && ImportUnder(context, plan, id, roots)).ToList() : new List<string>();
					if (inside.Count == 1) return (inside[0], false);
					throw new InventoryException(409, "ImportLocationAmbiguous");
				}
				if (rest.Count == 1 && !created)
				{
					var direct = placeable.Where(id => ImportLocationParent(context, plan, id) == node && Same(ImportLocationName(context, plan, id), rest[0])).ToList();
					if (direct.Count == 0)
					{
						var deeper = placeable.Where(id => id != node && Same(ImportLocationName(context, plan, id), rest[0]) && ImportUnder(context, plan, id, new[] { node })).ToList();
						if (deeper.Count == 1) return (deeper[0], false);
						if (deeper.Count > 1) throw new InventoryException(409, "ImportLocationAmbiguous");
					}
				}
			}
			else
			{
				if (rest.Count == 0)
				{
					var fallback = context.Locations.Values.FirstOrDefault(l => l.IsDefault && !context.Unplaceable.Contains(l.Id));
					if (fallback == null) throw new InventoryException(400, "ImportLocationRequired");
					return (fallback.Id, false);
				}
				var tops = placeable.Where(id => ImportLocationParent(context, plan, id) == null && Same(ImportLocationName(context, plan, id), rest[0])).ToList();
				if (tops.Count > 1) throw new InventoryException(409, "ImportLocationAmbiguous");
				if (tops.Count == 1) { node = tops[0]; rest = rest.Skip(1).ToList(); }
				else if (rest.Count == 1)
				{
					var anywhere = placeable.Where(id => Same(ImportLocationName(context, plan, id), rest[0])).ToList();
					if (anywhere.Count > 1) throw new InventoryException(409, "ImportLocationAmbiguous");
					if (anywhere.Count == 1) return (anywhere[0], false);
					return (PlanImportLocation(context, plan, InventoryLocationType.Facility, null, null, null, rest[0]), true);
				}
				else { node = PlanImportLocation(context, plan, InventoryLocationType.Facility, null, null, null, rest[0]); created = true; rest = rest.Skip(1).ToList(); }
			}
			foreach (var segment in rest)
			{
				var children = placeable.Where(id => ImportLocationParent(context, plan, id) == node && Same(ImportLocationName(context, plan, id), segment)).ToList();
				if (children.Count > 1) throw new InventoryException(409, "ImportLocationAmbiguous");
				if (children.Count == 1) { node = children[0]; continue; }
				node = PlanImportLocation(context, plan, InventoryLocationType.Facility, null, null, node, segment); created = true;
				placeable.Add(node);
			}
			return (node, created);
		}

		/// <summary>
		/// The caller's right to adjust stock at a location (null when allowed, else the refusal code). A planned location is
		/// checked through its nearest existing ancestor, or, for a new station or unit storage, against that holder.
		/// </summary>
		private async Task<string> ImportLocationAccessAsync(InventoryActor actor, ImportContext context, ImportPlan plan, string id)
		{
			string existing = null, plannedRoot = null;
			for (var current = id; current != null && plannedRoot != current; )
			{
				if (context.Locations.ContainsKey(current)) { existing = current; break; }
				plannedRoot = current; current = plan.Locations.FirstOrDefault(l => l.Id == current)?.ParentId;
			}
			var key = existing ?? "planned:" + plannedRoot;
			if (context.Access.TryGetValue(key, out var cached)) return cached;
			string denied = null;
			try
			{
				if (existing != null) await LocationAsync(actor, existing, true, PermissionTypes.AdjustInventory);
				else
				{
					var root = plan.Locations.First(l => l.Id == plannedRoot);
					var probe = new InventoryLocation { DepartmentId = actor.DepartmentId, LocationType = root.Type, GroupId = root.GroupId, UnitId = root.UnitId };
					await _auth.ValidateHolderAsync(actor, probe);
					var groupId = root.UnitId.HasValue ? context.Units.FirstOrDefault(u => u.UnitId == root.UnitId)?.StationGroupId : root.GroupId;
					await _auth.RequireAsync(actor, true, PermissionTypes.AdjustInventory, groupId);
				}
			}
			catch (InventoryException error) { denied = error.Code; }
			context.Access[key] = denied;
			return denied;
		}

		// ── Export ───────────────────────────────────────────────────────────────────────────────────────────────────────

		public async Task<string> ExportImportFileAsync(InventoryActor actor)
		{
			await _auth.RequireAsync(actor, false, PermissionTypes.AdjustInventory);
			if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
			var context = await ImportContextAsync(actor, null);
			var places = new Dictionary<string, (string Location, string Station, string Unit)?>(StringComparer.Ordinal);
			async Task<(string Location, string Station, string Unit)?> Place(string id)
			{
				if (id == null) return null;
				if (places.TryGetValue(id, out var known)) return known;
				(string, string, string)? value = null;
				if (context.Locations.ContainsKey(id) && !context.Unplaceable.Contains(id))
				{
					try
					{
						await LocationAsync(actor, id);
						var chain = new List<InventoryLocation>(); var seen = new HashSet<string>(StringComparer.Ordinal);
						for (var current = context.Locations[id]; current != null && seen.Add(current.Id) && seen.Count <= 32;
							current = current.ParentLocationId != null && context.Locations.TryGetValue(current.ParentLocationId, out var parent) ? parent : null) chain.Insert(0, current);
						var path = string.Join(" > ", chain.Select(l => context.LocationNames[l.Id]?.Trim()));
						var root = chain[0];
						if (root.LocationType == (int)InventoryLocationType.Unit)
						{
							var unit = context.Units.Where(u => u.UnitId == root.UnitId).Select(u => u.Name).FirstOrDefault();
							if (unit != null && context.Units.Count(u => Same(u.Name, unit)) == 1) value = (path, null, unit);
						}
						else if (root.LocationType == (int)InventoryLocationType.Station)
						{
							var group = context.Groups.Where(g => g.DepartmentGroupId == root.GroupId).Select(g => g.Name).FirstOrDefault();
							if (group != null && context.Groups.Count(g => Same(g.Name, group)) == 1) value = (path, group, null);
						}
						else value = (path, null, null);
					}
					catch (InventoryException error) when (error.StatusCode is 403 or 404 || error.Code == "LocationUnavailable") { }
				}
				places[id] = value;
				return value;
			}
			string Category(string id)
			{
				var names = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
				for (var current = context.Categories.FirstOrDefault(c => c.Id == id); current != null && seen.Add(current.Id) && seen.Count <= 32;
					current = context.Categories.FirstOrDefault(c => c.Id == current.ParentCategoryId)) names.Insert(0, context.CategoryNames[current.Id]);
				return names.Count == 0 ? null : string.Join(" > ", names);
			}
			static string YesNo(bool value) => value ? "Yes" : "No";
			void Put(Dictionary<string, string> row, (string Location, string Station, string Unit) place)
			{ row[Columns.Location] = place.Location; row[Columns.Station] = place.Station; row[Columns.Unit] = place.Unit; }

			var rows = new List<IReadOnlyDictionary<string, string>>();
			foreach (var item in context.Items.Where(i => !i.IsDeleted && !i.IsControlledSubstance).OrderBy(i => context.ItemDetails[i.Id].Name, StringComparer.OrdinalIgnoreCase))
			{
				var details = context.ItemDetails[item.Id];
				Dictionary<string, string> Line() => new(StringComparer.Ordinal)
				{
					[Columns.Item] = details.Name, [Columns.Code] = details.Code, [Columns.Barcode] = details.Barcode, [Columns.Category] = Category(item.CategoryId),
					[Columns.Description] = details.Description, [Columns.UnitOfMeasure] = details.UnitOfMeasure,
					[Columns.Tracking] = item.TrackingMode == (int)InventoryTrackingMode.Serialized ? "Serialized" : "Bulk",
					[Columns.RequiresLot] = YesNo(item.RequiresLotTracking), [Columns.RequiresExpiration] = YesNo(item.RequiresExpiration), [Columns.Currency] = details.CurrencyCode,
					[Columns.MinLevel] = InventoryImportCsv.Number(details.MinLevel), [Columns.MaxLevel] = InventoryImportCsv.Number(details.MaxLevel),
					[Columns.ReorderPoint] = InventoryImportCsv.Number(details.ReorderPoint), [Columns.ReorderQuantity] = InventoryImportCsv.Number(details.ReorderQuantity)
				};
				var lines = new List<Dictionary<string, string>>();
				var pars = context.Pars.Where(p => p.ItemId == item.Id).ToList();
				var usedPars = new HashSet<string>(StringComparer.Ordinal);
				if (item.TrackingMode == (int)InventoryTrackingMode.Bulk)
				{
					foreach (var stock in context.StocksByItem[item.Id].Where(s => s.Quantity != 0).OrderBy(s => s.LocationId, StringComparer.Ordinal).ThenBy(s => s.LotId, StringComparer.Ordinal))
					{
						if (await Place(stock.LocationId) is not { } place) continue;
						var line = Line(); Put(line, place); line[Columns.Quantity] = InventoryImportCsv.Number(stock.Quantity);
						var lot = stock.LotId == null ? null : context.Lots.FirstOrDefault(l => l.Id == stock.LotId);
						if (lot != null) { line[Columns.Lot] = context.LotDetails[lot.Id].LotNumber; line[Columns.Expires] = InventoryImportCsv.Date(lot.ExpiresOn); }
						line[Columns.UnitCost] = InventoryImportCsv.Number((lot == null ? null : context.LotDetails[lot.Id].UnitCost) ?? details.AverageUnitCost ?? details.DefaultUnitCost);
						var par = stock.LotId == null ? pars.FirstOrDefault(p => p.LocationId == stock.LocationId) : null;
						if (par != null && usedPars.Add(par.LocationId)) line[Columns.ParLevel] = InventoryImportCsv.Number(par.MinimumQuantity);
						lines.Add(line);
					}
				}
				else
				{
					foreach (var asset in context.AssetsByItem[item.Id].Where(a => !a.IsDeleted && a.Status is 0 or 1 or 2 or 3).OrderBy(a => context.AssetDetails[a.Id].SerialNumber, StringComparer.OrdinalIgnoreCase))
					{
						if (await Place(asset.CurrentLocationId) is not { } place) continue;
						var line = Line(); Put(line, place);
						line[Columns.SerialNumber] = context.AssetDetails[asset.Id].SerialNumber; line[Columns.AssetTag] = context.AssetDetails[asset.Id].AssetTag;
						var lot = asset.LotId == null ? null : context.Lots.FirstOrDefault(l => l.Id == asset.LotId);
						if (lot != null) line[Columns.Lot] = context.LotDetails[lot.Id].LotNumber;
						line[Columns.Expires] = InventoryImportCsv.Date(asset.ExpiresOn);
						line[Columns.UnitCost] = InventoryImportCsv.Number(context.AssetDetails[asset.Id].AcquisitionCost ?? details.AverageUnitCost ?? details.DefaultUnitCost);
						lines.Add(line);
					}
				}
				// An item with no stock or assets keeps its default cost on its par rows, as the catalog-only row would.
				var stocked = lines.Count > 0;
				foreach (var par in pars.Where(p => !usedPars.Contains(p.LocationId)))
				{
					if (await Place(par.LocationId) is not { } place) continue;
					var line = Line(); Put(line, place); line[Columns.ParLevel] = InventoryImportCsv.Number(par.MinimumQuantity);
					if (!stocked) line[Columns.UnitCost] = InventoryImportCsv.Number(details.DefaultUnitCost);
					lines.Add(line);
				}
				if (lines.Count == 0) { var line = Line(); line[Columns.UnitCost] = InventoryImportCsv.Number(details.DefaultUnitCost); lines.Add(line); }
				rows.AddRange(lines);
			}
			return InventoryImportCsv.Write(rows);
		}
	}
}
