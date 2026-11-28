using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
	/// <summary>
	/// Industrial ERT G6 WE1 (Readiness Pro): sets up the technician service of many extinguishers (or any serialized asset) at once.
	/// For every serialized asset of one inventory item and every chosen preset it saves a completion-based yearly preventive-maintenance
	/// schedule on that asset (annual maintenance, 6-year maintenance, 5- or 12-year hydrostatic test). Each schedule has a stable request
	/// identity per department, asset and preset, so running the setup again (for assets added later) never duplicates one.
	/// </summary>
	public sealed partial class WorkOrdersService
	{
		/// <summary>Assets handled in one run; the result says when an item has more (run it again after the first batch).</summary>
		public const int AssetServiceSetupLimit = 1000;

		public static string AssetServiceRequestId(int departmentId, string assetId, AssetServicePreset preset) =>
			MaintenanceIdentity("asset-service:" + departmentId + ":" + assetId + ":" + (int)preset);

		/// <summary>The schedule title: the service, then the asset's tag or serial so the schedules of one item can be told apart.</summary>
		public static string AssetServiceTitle(string service, InventoryAsset asset)
		{
			InventoryAssetContent details = null;
			try { details = string.IsNullOrWhiteSpace(asset?.Content) ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryAssetContent>(asset.Content); }
			catch (Newtonsoft.Json.JsonException) { details = null; }
			var label = (string.IsNullOrWhiteSpace(details?.AssetTag) ? details?.SerialNumber : details.AssetTag)?.Trim();
			var title = string.IsNullOrEmpty(label) ? service : service + " — " + label;
			return title.Length > 200 ? title.Substring(0, 200) : title;
		}

		public async Task<AssetServiceSetupResult> SetupAssetServiceAsync(ChecklistActor actor, AssetServiceSetupInput input)
		{
			RequireMaintenanceStore();
			if (input == null || !Guid.TryParseExact(input.ItemId, "D", out _) || input.Presets == null || input.Presets.Count == 0
				|| input.Presets.Any(p => !Enum.IsDefined(p)) || input.Presets.Distinct().Count() != input.Presets.Count || !Enum.IsDefined(input.Priority))
				throw new WorkOrderException(400, "InvalidInput");
			if (_inventoryCatalog == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
			await RequireWriteAsync(actor);
			if (!await _authorization.CanManageAsync(actor, null)) throw new WorkOrderException(403, "PermissionRequired");

			var result = new AssetServiceSetupResult();
			for (var page = 0; ; page++)
			{
				InventoryPage<InventoryAsset> assets;
				try { assets = await _inventoryCatalog.Value.QueryAsync<InventoryAsset>(InventoryActor(actor), new InventoryQuery { ItemId = input.ItemId }, page); }
				catch (InventoryException ex) { throw new WorkOrderException(ex.StatusCode, ex.StatusCode == 403 ? "InventoryPermissionRequired" : "InventoryOperationFailed"); }

				foreach (var asset in assets.Items.Where(a => !a.IsDeleted && a.ItemId == input.ItemId))
				{
					if (result.Assets >= AssetServiceSetupLimit) { result.Truncated = true; return result; }
					result.Assets++;
					if (asset.Status is (int)InventoryAssetStatus.Retired or (int)InventoryAssetStatus.Lost or (int)InventoryAssetStatus.Consumed) { result.Skipped++; continue; }
					foreach (var preset in input.Presets)
					{
						var requestId = AssetServiceRequestId(actor.DepartmentId, asset.Id, preset);
						if ((await _maintenance.QueryMaintenanceAsync<WorkOrderRecurrence>(actor.DepartmentId, "RequestId", requestId)).Count > 0) { result.Existing++; continue; }
						try
						{
							await SaveRecurrenceAsync(actor, new WorkOrderRecurrenceInput
							{
								Template = new WorkOrderInput
								{
									RequestId = requestId, InventoryAssetId = asset.Id, Priority = input.Priority, Type = WorkOrderType.Preventive,
									Content = new WorkOrderContent { Title = AssetServiceTitle(MaintenanceText(AssetServicePresets.TitleKey(preset)), asset) }
								},
								Calendar = MaintenanceCalendar.Yearly, Interval = AssetServicePresets.Years(preset), CompletionBased = true,
								AnchorLocal = input.AnchorLocal, TimeZoneId = input.TimeZoneId, LeadDays = input.LeadDays,
								AssignedToRoleId = input.AssignedToRoleId
							});
							result.Created++;
						}
						catch (WorkOrderException ex) when (ex.StatusCode == 400 && ex.Code == "RecurrenceInvalid") { throw; }
						catch (WorkOrderException) { result.Failed++; }
					}
				}
				if (!assets.HasMore) break;
			}
			return result;
		}
	}
}
