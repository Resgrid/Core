using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Inventory usage over RmsExternalReference rows (semantic role InventoryUsage). Writes never touch a legacy
	/// Log row; reads answer for either source so callers stay source-agnostic (RMS plan RMS-1 package).
	/// </summary>
	public class RmsInventoryUsageAdapter : IRmsInventoryUsageAdapter
	{
		public const string SemanticRole = "InventoryUsage";
		public const string SourceSubsystem = "Inventory";
		public const string IdentifierScheme = "resgrid:inventory";

		private readonly IRmsExternalReferencesRepository _references;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _incidents;
		private readonly IInventoryService _inventory;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitsService _units;
		private readonly IUnitOfWork _unit;
		private readonly IRmsAccessAuditsRepository _audits;

		public RmsInventoryUsageAdapter(IRmsExternalReferencesRepository references, IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository incidents,
			IInventoryService inventory, IRecordsAuthorizationService authorization, IDepartmentGroupsService groups, IUnitsService units, IUnitOfWork unit, IRmsAccessAuditsRepository audits)
		{
			_references = references;
			_records = records;
			_incidents = incidents; _inventory = inventory; _authorization = authorization; _groups = groups; _units = units; _unit = unit; _audits = audits;
		}

		public async Task<RmsInventoryUsage> ConsumeAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, long expectedRowVersion, int typeId, int groupId, int? unitId, decimal quantity, string note, CancellationToken cancellationToken = default)
		{
			ValidateQuantity(quantity, note);
			if (kind is not (RmsRecordKind.Operational or RmsRecordKind.IncidentReport)) throw new ArgumentException("Choose an operational or incident record.");
			_unit.CreateOrGetConnection();
			try
			{
				await GuardAsync(departmentId, userId, recordId, kind, expectedRowVersion, cancellationToken);
				var type = await _inventory.GetTypeByIdAsync(typeId);
				if (type?.DepartmentId != departmentId || (await _groups.GetGroupByIdAsync(groupId, true))?.DepartmentId != departmentId || (unitId.HasValue && (await _units.GetUnitByIdAsync(unitId.Value))?.DepartmentId != departmentId)) throw new UnauthorizedAccessException("Inventory type, station and unit must belong to this department.");
				if (!await _authorization.CanUseSourceInventoryAsync(userId, departmentId, groupId)) throw new UnauthorizedAccessException();
				var ledger = await _inventory.SaveInventoryAsync(new Inventory { DepartmentId = departmentId, TypeId = typeId, GroupId = groupId, UnitId = unitId,
					Amount = -(double)quantity, Note = note, TimeStamp = DateTime.UtcNow, AddedByUserId = userId }, cancellationToken);
				if (ledger?.DepartmentId != departmentId || ledger.InventoryId <= 0) throw new InvalidOperationException("The inventory consumption could not be recorded.");
				var usage = await WriteReferenceAsync(departmentId, userId, recordId, kind, ledger, type, quantity, note, cancellationToken);
				_unit.CommitChanges(); return usage;
			}
			catch { _unit.DiscardChanges(); throw; }
		}

		private async Task GuardAsync(int department, string user, string recordId, RmsRecordKind kind, long? expected, CancellationToken ct)
		{
			if (!await _authorization.CanUserViewRecordAsync(user, recordId, department) || !await _authorization.HasPermissionAsync(user, department, PermissionTypes.CreateRecord) || !await _authorization.HasPermissionAsync(user, department, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			string author, owner, amendment; int state; long version;
			if (kind == RmsRecordKind.Operational)
			{
				var r = await _records.GetByIdForDepartmentAsync(department, recordId);
				if (r == null || r.DeletedOn.HasValue || r.PurgedOn.HasValue) throw new InvalidOperationException("The record is unavailable.");
				author=r.AuthorUserId; owner=r.OwnerUserId; amendment=r.AmendsRevisionId; state=r.State; version=r.RowVersion;
			}
			else
			{
				var r = await _incidents.GetByIdForDepartmentAsync(department, recordId);
				if (r == null || r.DeletedOn.HasValue || r.PurgedOn.HasValue) throw new InvalidOperationException("The incident report is unavailable.");
				author=r.AuthorUserId; owner=r.OwnerUserId; amendment=r.AmendsRevisionId; state=r.State; version=r.RowVersion;
			}
			if (RmsLifecycle.IsTerminal((RmsRecordState)state) || !(RmsLifecycle.IsEditable((RmsRecordState)state) || amendment != null)) throw new InvalidOperationException("Record inventory usage through a draft or amendment.");
			if (author != user && owner != user && !await _authorization.IsDepartmentAdminAsync(user, department) && !(amendment != null && await _authorization.HasPermissionAsync(user, department, PermissionTypes.AmendRecords))) throw new UnauthorizedAccessException();
			if (expected.HasValue && expected.Value != version) throw new RecordConcurrencyException(recordId, expected.Value, version);
			var bumped = kind == RmsRecordKind.Operational ? await _records.TryBumpRowVersionAsync(department, recordId, version, ct) : await _incidents.TryBumpRowVersionAsync(department, recordId, version, ct);
			if (!bumped) throw new RecordConcurrencyException(recordId, version, version + 1);
		}
		private static void ValidateQuantity(decimal quantity, string note)
		{
			if (quantity <= 0 || quantity > 100000000 || decimal.Round(quantity, 6) != quantity) throw new ArgumentException("Quantity must be positive, at most 100,000,000, with at most six decimal places.");
			if (note?.Length > 16000) throw new ArgumentException("The usage note is limited to 16,000 characters.");
		}

		public async Task<List<RmsInventoryUsage>> GetUsageForRecordAsync(int departmentId, string recordId)
		{
			var references = await _references.GetForRecordAsync(departmentId, recordId) ?? Enumerable.Empty<RmsExternalReference>();
			return references
				.Where(r => r != null && r.DepartmentId == departmentId && r.RecordId == recordId && !r.DeletedOn.HasValue && string.Equals(r.SemanticRole, SemanticRole, StringComparison.Ordinal))
				.Select(FromReference)
				.Where(u => u != null)
				.OrderBy(u => u.CapturedOn)
				.ToList();
		}

		/// <summary>
		/// The legacy Logs schema has no inventory linkage (Inventory rows carry a unit and a group, never a LogId),
		/// so legacy usage is empty by construction. The method exists so callers never branch on the source.
		/// </summary>
		public Task<List<RmsInventoryUsage>> GetUsageForLegacyLogAsync(int departmentId, int logId)
		{
			return Task.FromResult(new List<RmsInventoryUsage>());
		}

		public async Task<RmsInventoryUsage> RecordUsageAsync(int departmentId, string userId, string recordId, int inventoryId, decimal quantity, string note, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recordId)) throw new ArgumentException("A record is required.", nameof(recordId));
			if (inventoryId <= 0) throw new ArgumentException("An inventory item is required.", nameof(inventoryId));
			ValidateQuantity(quantity, note);
			_unit.CreateOrGetConnection();
			try
			{
				await GuardAsync(departmentId, userId, recordId, RmsRecordKind.Operational, null, cancellationToken);
				var ledger = await _inventory.GetInventoryByIdAsync(inventoryId);
				if (ledger?.DepartmentId != departmentId || !await _authorization.CanUseSourceInventoryAsync(userId, departmentId, ledger.GroupId)) throw new UnauthorizedAccessException();
				var type = await _inventory.GetTypeByIdAsync(ledger.TypeId);
				if (type?.DepartmentId != departmentId) throw new UnauthorizedAccessException();
				var usage = await WriteReferenceAsync(departmentId, userId, recordId, RmsRecordKind.Operational, ledger, type, quantity, note, cancellationToken);
				_unit.CommitChanges(); return usage;
			}
			catch { _unit.DiscardChanges(); throw; }
		}
		private async Task<RmsInventoryUsage> WriteReferenceAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, Inventory ledger, InventoryType type, decimal quantity, string note, CancellationToken cancellationToken)
		{
			if (!await _authorization.CanUseSourceInventoryAsync(userId, departmentId, ledger.GroupId) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			var now = DateTime.UtcNow;
			var source = new { ledger.InventoryId, ledger.TypeId, ledger.GroupId, ledger.UnitId, ledger.Amount, ledger.TimeStamp, ledger.Batch, ledger.Note, ledger.Location, ledger.AddedByUserId };
			var snapshot = JsonConvert.SerializeObject(new UsageSnapshot { InventoryId = ledger.InventoryId, Quantity = quantity, Note = note, ItemName = type.Type, UnitOfMeasure = type.UnitOfMesasure, Source = source, SourceChecksum = Checksum(JsonConvert.SerializeObject(source)) });
			var reference = new RmsExternalReference
			{
				RmsExternalReferenceId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = recordId,
				RecordKind = (int)kind,
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = "Inventory",
				SourceEntityId = ledger.InventoryId.ToString(),
				IdentifierScheme = IdentifierScheme,
				SemanticRole = SemanticRole,
				CapturedOn = now,
				CapturedByUserId = userId,
				Checksum = Checksum(snapshot),
				SnapshotJson = snapshot,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};

			await _references.InsertAsync(reference, cancellationToken, true);
			await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = recordId, ActorUserId = userId, Action = (int)RmsAccessAuditAction.Change, Successful = true, OccurredOn = now, Purpose = "Inventory usage recorded", DetailJson = JsonConvert.SerializeObject(new { reference.RmsExternalReferenceId, ledger.InventoryId, reference.Checksum }) }, cancellationToken, true);
			return FromReference(reference);
		}

		private static RmsInventoryUsage FromReference(RmsExternalReference reference)
		{
			if (string.IsNullOrWhiteSpace(reference.Checksum) || Checksum(reference.SnapshotJson ?? "") != reference.Checksum) throw new InvalidOperationException("Inventory usage failed its integrity check.");
			UsageSnapshot snapshot;
			try
			{
				snapshot = string.IsNullOrWhiteSpace(reference.SnapshotJson) ? new UsageSnapshot() : JsonConvert.DeserializeObject<UsageSnapshot>(reference.SnapshotJson) ?? new UsageSnapshot();
			}
			catch (JsonException)
			{
				throw new InvalidOperationException("The inventory usage snapshot is unreadable.");
			}

			if (!int.TryParse(reference.SourceEntityId, out var inventoryId) || inventoryId != snapshot.InventoryId || snapshot.Quantity <= 0)
				throw new InvalidOperationException("The inventory usage source identity is invalid.");

			return new RmsInventoryUsage
			{
				ReferenceId = reference.RmsExternalReferenceId,
				ReferenceChecksum = reference.Checksum,
				Source = RmsInventoryUsage.SourceRecord,
				RecordId = reference.RecordId,
				InventoryId = inventoryId,
				Quantity = snapshot.Quantity,
				Note = snapshot.Note,
				ItemName = snapshot.ItemName, UnitOfMeasure = snapshot.UnitOfMeasure, SourceChecksum = snapshot.SourceChecksum,
				CapturedByUserId = reference.CapturedByUserId,
				CapturedOn = reference.CapturedOn
			};
		}

		private static string Checksum(string text)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

		private sealed class UsageSnapshot
		{
			public int InventoryId { get; set; }
			public decimal Quantity { get; set; }
			public string Note { get; set; }
			public string ItemName { get; set; }
			public string UnitOfMeasure { get; set; }
			public string SourceChecksum { get; set; }
			public object Source { get; set; }
		}
	}
}
