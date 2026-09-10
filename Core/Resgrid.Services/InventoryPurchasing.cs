using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		private async Task<bool> HasOpenPurchaseLineAsync(InventoryActor actor, string itemId)
		{
			foreach (var line in (await _store.RelatedAsync<InventoryPurchaseOrderItem>(actor.DepartmentId, "ItemId", itemId)).Where(l => !l.IsDeleted && l.QuantityReceived < l.QuantityOrdered))
				if ((await _store.GetAsync<InventoryPurchaseOrder>(actor.DepartmentId, line.PurchaseOrderId)) is { IsDeleted: false, Status: 1 or 2 }) return true;
			return false;
		}
		private async Task RequirePurchasingAccessAsync(InventoryActor actor, bool write)
		{
			await _auth.RequireAsync(actor, write, PermissionTypes.AdjustInventory);
			await _auth.RequireAsync(actor, false, PermissionTypes.ContactView);
		}
		private async Task<Contact> VendorContactAsync(InventoryActor actor, string contactId, bool reveal)
		{
			await _auth.RequireAsync(actor, false, PermissionTypes.ContactView);
			if (_contacts == null || string.IsNullOrWhiteSpace(contactId) || contactId.Length > 128) throw new InventoryException(404, "VendorContactUnavailable");
			var contact = await _contacts.GetContactByIdAsync(contactId);
			if (contact?.DepartmentId != actor.DepartmentId || contact.IsDeleted || contact.ContactType != 1) throw new InventoryException(404, "VendorContactUnavailable");
			if (reveal)
			{
				contact = JsonConvert.DeserializeObject<Contact>(JsonConvert.SerializeObject(contact));
				var wasPlain = !string.IsNullOrEmpty(contact.CompanyName) && !ProtectedDataEnvelope.HasEnvelopePrefix(contact.CompanyName);
				var result = await _read.ResolveContactsForReadAsync(actor.DepartmentId, new[] { contact }, actor.GrantToken, actor.UserId);
				if (result == null || result.RedactedFields.Count > 0 || result.IsProtected && wasPlain || ProtectedDataEnvelope.HasEnvelopePrefix(contact.CompanyName)) throw new InventoryException(403, "ProtectedDataRequired");
			}
			return contact;
		}
		public async Task<List<InventoryVendorChoice>> GetVendorContactsAsync(InventoryActor actor)
		{
			await RequirePurchasingAccessAsync(actor, false);
			if (_contacts == null) throw new InventoryException(404, "VendorContactUnavailable");
			var contacts = (await _contacts.GetAllContactsForDepartmentAsync(actor.DepartmentId)).Where(c => c.DepartmentId == actor.DepartmentId && c.ContactType == 1 && !c.IsDeleted)
				.Select(c => JsonConvert.DeserializeObject<Contact>(JsonConvert.SerializeObject(c))).ToList();
			if (contacts.Count > 5000) throw new InventoryException(409, "InventoryTooLarge");
			if (contacts.Count == 0) return new List<InventoryVendorChoice>();
			var wasPlain = contacts.Any(c => !string.IsNullOrEmpty(c.CompanyName) && !ProtectedDataEnvelope.HasEnvelopePrefix(c.CompanyName));
			var result = await _read.ResolveContactsForReadAsync(actor.DepartmentId, contacts, actor.GrantToken, actor.UserId);
			if (result == null || result.RedactedFields.Count > 0 || result.IsProtected && wasPlain || contacts.Any(c => ProtectedDataEnvelope.HasEnvelopePrefix(c.CompanyName)))
				throw new InventoryException(403, "ProtectedDataRequired");
			return contacts.Select(c => new InventoryVendorChoice { Id = c.ContactId, Name = c.CompanyName }).ToList();
		}
		public Task<InventoryVendor> SaveVendorAsync(InventoryActor actor, InventoryVendorInput input) => TransactionAsync(actor, async events =>
		{
			await RequirePurchasingAccessAsync(actor, true);
			if (input?.Details == null || input.Details.AccountNumber?.Length > 250 || input.Details.Note?.Length > 16000) throw new InventoryException(400, "InvalidVendor");
			await VendorContactAsync(actor, input.ContactId, true);
			var row = input.Id == null ? New<InventoryVendor>(actor) : await GetAsync<InventoryVendor>(actor, input.Id);
			if (input.Id != null && (row.Revision != input.Revision || row.IsDeleted)) throw new InventoryException(409, "RevisionConflict");
			if (input.Id != null && !string.Equals(row.ContactId, input.ContactId, StringComparison.OrdinalIgnoreCase)) throw new InventoryException(409, "VendorContactImmutable");
			if ((await _store.RelatedAsync<InventoryVendor>(actor.DepartmentId, "ContactId", input.ContactId)).Any(v => !v.IsDeleted && v.Id != row.Id)) throw new InventoryException(409, "DuplicateVendor");
			row.ContactId = input.ContactId; row.Content = JsonConvert.SerializeObject(input.Details);
			await SaveAsync(actor, row, input.Id == null); await AuditAsync(actor, row, "InventoryVendorSaved"); return await RevealAsync(actor, row);
		});
		public async Task<InventoryPurchaseOrderDetail> GetPurchaseOrderAsync(InventoryActor actor, string id)
		{
			var order = await GetAsync<InventoryPurchaseOrder>(actor, id);
			var result = new InventoryPurchaseOrderDetail { Order = order };
			foreach (var line in (await _store.RelatedAsync<InventoryPurchaseOrderItem>(actor.DepartmentId, "PurchaseOrderId", id)).Where(l => !l.IsDeleted).OrderBy(l => l.LineNumber))
				result.Lines.Add(await RevealAsync(actor, line));
			result.Receipts = await GetByReferenceAsync(actor, InventoryReferenceType.PurchaseOrder, id);
			return result;
		}
		public async Task<InventoryPurchaseOrderDetail> SavePurchaseOrderAsync(InventoryActor actor, InventoryPurchaseOrderInput input)
		{
			var id = await TransactionAsync(actor, async events =>
			{
				await RequirePurchasingAccessAsync(actor, true);
				if (input == null || input.Revision < 0 || input.Lines == null || input.Lines.Count is < 1 or > 100 || input.Lines.Any(l => l == null) || input.Note?.Length > 16000) throw new InventoryException(400, "InvalidPurchaseOrder");
				Id(input.Id); Text(input.Number, 80); var currency = Currency(input.CurrencyCode) ?? throw new InventoryException(400, "CurrencyRequired");
				var vendor = await GetAsync<InventoryVendor>(actor, input.VendorId); if (vendor.IsDeleted) throw new InventoryException(409, "VendorUnavailable");
				var contact = await VendorContactAsync(actor, vendor.ContactId, true);
				var row = await _store.GetAsync<InventoryPurchaseOrder>(actor.DepartmentId, input.Id);
				var insert = row == null;
				if (insert && input.Revision != 0 || !insert && (row.IsDeleted || row.Status != 0 || row.Revision != input.Revision)) throw new InventoryException(409, "RevisionConflict");
				row ??= New<InventoryPurchaseOrder>(actor); row.Id = input.Id; row.VendorId = vendor.Id; row.CurrencyCode = currency;
				foreach (var other in (await AllAsync<InventoryPurchaseOrder>(actor.DepartmentId)).Where(p => p.Id != row.Id))
					if (string.Equals(Decode<InventoryPurchaseOrderContent>(await RevealAsync(actor, other)).Number, input.Number.Trim(), StringComparison.OrdinalIgnoreCase)) throw new InventoryException(409, "DuplicatePurchaseOrderNumber");
				row.Content = JsonConvert.SerializeObject(new InventoryPurchaseOrderContent { Number = input.Number.Trim(), Note = input.Note, SupplierName = contact.CompanyName });
				await SaveAsync(actor, row, insert);
				var old = (await _store.RelatedAsync<InventoryPurchaseOrderItem>(actor.DepartmentId, "PurchaseOrderId", row.Id)).Where(l => !l.IsDeleted).ToList();
				// Retire the old draft lines first; posted lines can never enter this path.
				foreach (var line in old) { var retired = await RevealAsync(actor, line); retired.IsDeleted = true; await SaveAsync(actor, retired, false); }
				var ids = new HashSet<string>();
				for (var index = 0; index < input.Lines.Count; index++)
				{
					var line = input.Lines[index]; Quantity(line.QuantityOrdered); Cost(line.UnitCost);
					if (line.Note?.Length > 16000) throw new InventoryException(400, "InvalidPurchaseOrder");
					var item = await GetAsync<InventoryItem>(actor, line.ItemId); var details = Decode<InventoryItemContent>(item);
					if (item.IsDeleted || !item.IsActive || item.TrackingMode == 1 && decimal.Truncate(line.QuantityOrdered) != line.QuantityOrdered) throw new InventoryException(400, "InvalidItem");
					if (details.CurrencyCode != currency) throw new InventoryException(409, "ItemCurrencyMismatch");
					InventoryPurchaseOrderItem stored;
					if (line.Id != null)
					{
						Id(line.Id); stored = old.SingleOrDefault(l => l.Id == line.Id) ?? throw new InventoryException(404, "PurchaseOrderLineUnavailable");
						stored = await GetAsync<InventoryPurchaseOrderItem>(actor, stored.Id);
					}
					else stored = New<InventoryPurchaseOrderItem>(actor);
					if (!ids.Add(stored.Id)) throw new InventoryException(400, "DuplicatePurchaseOrderLine");
					stored.IsDeleted = false; stored.PurchaseOrderId = row.Id; stored.ItemId = item.Id; stored.LineNumber = index + 1; stored.QuantityOrdered = line.QuantityOrdered;
					stored.Content = JsonConvert.SerializeObject(new InventoryPurchaseOrderItemContent { ItemName = details.Name, UnitOfMeasure = details.UnitOfMeasure, UnitCost = line.UnitCost, Note = line.Note });
					await SaveAsync(actor, stored, line.Id == null);
				}
				await AuditAsync(actor, row, "InventoryPurchaseOrderSaved"); return row.Id;
			});
			return await GetPurchaseOrderAsync(actor, id);
		}
		public Task<InventoryResult> ChangePurchaseOrderStatusAsync(InventoryActor actor, InventoryPurchaseOrderChange input, InventoryPurchaseOrderStatus status) => TransactionAsync(actor, async events =>
		{
			await RequirePurchasingAccessAsync(actor, true);
			if (input == null || status is not (InventoryPurchaseOrderStatus.Ordered or InventoryPurchaseOrderStatus.Cancelled)) throw new InventoryException(400, "InvalidPurchaseOrder");
			Id(input.Id);
			return await OperationAsync(actor, input.RequestId, new { Kind = "PurchaseOrderStatus", input, status }, async (op, pending) =>
			{
				var order = await GetAsync<InventoryPurchaseOrder>(actor, input.Id);
				if (order.IsDeleted || order.Revision != input.Revision || order.Status is 3 or 4 || status == InventoryPurchaseOrderStatus.Ordered && order.Status != 0) throw new InventoryException(409, "PurchaseOrderStateConflict");
				if (status == InventoryPurchaseOrderStatus.Ordered)
				{
					var vendor = await GetAsync<InventoryVendor>(actor, order.VendorId); if (vendor.IsDeleted) throw new InventoryException(409, "VendorUnavailable");
					var contact = await VendorContactAsync(actor, vendor.ContactId, true);
					var details = Decode<InventoryPurchaseOrderContent>(order); details.SupplierName = contact.CompanyName; order.Content = JsonConvert.SerializeObject(details);
					var lines = (await _store.RelatedAsync<InventoryPurchaseOrderItem>(actor.DepartmentId, "PurchaseOrderId", order.Id)).Where(l => !l.IsDeleted).ToList();
					if (lines.Count is < 1 or > 100) throw new InventoryException(409, "InvalidPurchaseOrder");
					foreach (var line in lines)
					{
						var item = await GetAsync<InventoryItem>(actor, line.ItemId);
						if (item.IsDeleted || !item.IsActive || Decode<InventoryItemContent>(item).CurrencyCode != order.CurrencyCode) throw new InventoryException(409, "ItemCurrencyMismatch");
					}
					order.OrderedOn = Now;
				}
				order.Status = (int)status; await SaveAsync(actor, order, false); await AuditAsync(actor, order, "InventoryPurchaseOrderStatusChanged");
				return new InventoryResult { PurchaseOrderId = order.Id };
			}, events);
		});
	}
}
