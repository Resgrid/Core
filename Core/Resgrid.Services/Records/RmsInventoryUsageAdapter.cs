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

		public RmsInventoryUsageAdapter(IRmsExternalReferencesRepository references, IRmsOperationalRecordsRepository records)
		{
			_references = references;
			_records = records;
		}

		public async Task<List<RmsInventoryUsage>> GetUsageForRecordAsync(int departmentId, string recordId)
		{
			var references = await _references.GetForRecordAsync(departmentId, recordId) ?? Enumerable.Empty<RmsExternalReference>();
			return references
				.Where(r => r != null && !r.DeletedOn.HasValue && string.Equals(r.SemanticRole, SemanticRole, StringComparison.Ordinal))
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
			if (quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));

			var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
			if (record == null || record.DeletedOn.HasValue)
				throw new InvalidOperationException($"Record {recordId} does not exist in department {departmentId}.");

			if (RmsLifecycle.IsTerminal((RmsRecordState)record.State))
				throw new InvalidOperationException("Inventory usage cannot be recorded against a voided or cancelled Record.");

			var now = DateTime.UtcNow;
			var snapshot = JsonConvert.SerializeObject(new UsageSnapshot { InventoryId = inventoryId, Quantity = quantity, Note = note });
			var reference = new RmsExternalReference
			{
				RmsExternalReferenceId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = recordId,
				RecordKind = (int)RmsRecordKind.Operational,
				SourceSubsystem = SourceSubsystem,
				SourceEntityType = "Inventory",
				SourceEntityId = inventoryId.ToString(),
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
			return FromReference(reference);
		}

		private static RmsInventoryUsage FromReference(RmsExternalReference reference)
		{
			UsageSnapshot snapshot;
			try
			{
				snapshot = string.IsNullOrWhiteSpace(reference.SnapshotJson) ? new UsageSnapshot() : JsonConvert.DeserializeObject<UsageSnapshot>(reference.SnapshotJson) ?? new UsageSnapshot();
			}
			catch (JsonException)
			{
				return null;
			}

			if (!int.TryParse(reference.SourceEntityId, out var inventoryId))
				inventoryId = snapshot.InventoryId;

			return new RmsInventoryUsage
			{
				ReferenceId = reference.RmsExternalReferenceId,
				Source = RmsInventoryUsage.SourceRecord,
				RecordId = reference.RecordId,
				InventoryId = inventoryId,
				Quantity = snapshot.Quantity,
				Note = snapshot.Note,
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
		}
	}
}
