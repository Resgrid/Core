using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.AdminAssist;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistDiagnosticService(IAdminAssistAccessService access, IAdminAssistDiagnosticStore store,
		IAdminAssistDiagnosticSource source, IAdminAssistRepository revisions, IAdminAssistCatalog catalog,
		IAdminAssistDiagnosticProtection protection, IAuditService audit, TimeProvider clock) : IAdminAssistDiagnostics
	{
		private async Task RequireAsync(AdminAssistActor actor, CancellationToken ct)
		{
			if (!AdminAssistConfig.TroubleshootingEnabled || !await access.CanAccessAsync(actor, false, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
			await protection.RequireAsync(actor, ct);
		}
		private async Task<T> BoundedAsync<T>(AdminAssistActor actor, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(60)); ct = deadline.Token;
			await RequireAsync(actor, ct);
			var lease = Guid.NewGuid().ToString("D");
			if (!await store.AcquireDiagnosticLeaseAsync(actor, lease, clock.GetUtcNow().UtcDateTime, ct)) throw new AdminAssistConcurrencyException();
			try { return await operation(ct).WaitAsync(ct); }
			finally
			{
				using var settle = new CancellationTokenSource(TimeSpan.FromSeconds(3));
				try { await store.ReleaseDiagnosticLeaseAsync(actor, lease, settle.Token); }
				catch (Exception) { /* Admission expires after 120 seconds; never log request contents. */ }
			}
		}
		public Task<DiagnosticReport> RunAsync(AdminAssistActor actor, DiagnosticRequest request, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			var now = clock.GetUtcNow().UtcDateTime; DiagnosticPolicy.Validate(request, now);
			var row = new AdminAssistDiagnosticRun { Id = Guid.NewGuid().ToString("D"), DepartmentId = actor.DepartmentId, UserId = actor.UserId, Flow = request.Flow, CreatedOnUtc = now, Revision = 1 };
			var result = await BuildAsync(actor, row, request, token);
			await protection.ProtectAsync(actor, row, request, token);
			await RequireAsync(actor, token);
			await AuditAsync(actor, row.Id, "Run", token);
			await store.SaveDiagnosticAsync(actor, row, token);
			return result;
		}, ct);
		public Task<DiagnosticReport> ReadAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct) => BoundedAsync(actor, token => RefreshAsync(actor, command, token), ct);
		private async Task<DiagnosticReport> RefreshAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct)
		{
			var row = await OwnedAsync(actor, command, ct);
			var request = await protection.ReadAsync(actor, row, ct);
			// The window was valid when the run was created; OwnedAsync enforces retention, so a retained run stays readable as its window ages.
			DiagnosticPolicy.Validate(request, row.CreatedOnUtc);
			if (request.Flow != row.Flow) throw new UnauthorizedAccessException();
			var report = await BuildAsync(actor, row, request, ct);
			await OwnedAsync(actor, command, ct); // A simultaneous tombstone cannot replay a retained request.
			await AuditAsync(actor, row.Id, "Read", ct);
			return report;
		}
		public async Task<IReadOnlyList<DiagnosticRunSummary>> ListAsync(AdminAssistActor actor, CancellationToken ct)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10)); ct = deadline.Token;
			await RequireAsync(actor, ct); var rows = await store.ListDiagnosticsAsync(actor, ct); await RequireAsync(actor, ct);
			return rows.Where(r => r.CreatedOnUtc >= clock.GetUtcNow().UtcDateTime.AddDays(-Math.Clamp(AdminAssistConfig.DiagnosticRetentionDays, 1, 30))).ToArray();
		}
		public Task<DiagnosticSupportBundle> PreviewSupportAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			var bundle = DiagnosticPolicy.Bundle(await RefreshAsync(actor, command, token));
			await AuditAsync(actor, command.RunId, "SupportPreview", token); return bundle;
		}, ct);
		public Task<DiagnosticSupportBundle> ExportSupportAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			if (command?.PreviewDigest == null || command.PreviewDigest.Length != 64) throw new ArgumentException("Preview the support bundle first.");
			var bundle = DiagnosticPolicy.Bundle(await RefreshAsync(actor, command, token));
			if (!string.Equals(bundle.PreviewDigest, command.PreviewDigest, StringComparison.Ordinal)) throw new AdminAssistConcurrencyException();
			await RequireAsync(actor, token); await AuditAsync(actor, command.RunId, "SupportExport", token); return bundle;
		}, ct);
		public async Task DeleteAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct)
		{
			await RequireAsync(actor, ct); await OwnedAsync(actor, command, ct);
			await AuditAsync(actor, command.RunId, "Delete", ct); await store.DeleteDiagnosticAsync(actor, command, ct);
		}
		private async Task<AdminAssistDiagnosticRun> OwnedAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct)
		{
			if (command == null || !Guid.TryParseExact(command.RunId, "D", out _) || command.ExpectedRevision != 1) throw new ArgumentException("Invalid run reference.");
			var row = await store.ReadDiagnosticAsync(actor, command.RunId, ct);
			if (row == null || row.Id != command.RunId || row.Deleted || row.DepartmentId != actor.DepartmentId || row.UserId != actor.UserId || !DiagnosticPolicy.Flows.Contains(row.Flow) || row.CreatedOnUtc < clock.GetUtcNow().UtcDateTime.AddDays(-Math.Clamp(AdminAssistConfig.DiagnosticRetentionDays, 1, 30))) throw new UnauthorizedAccessException();
			if (row.Revision != command.ExpectedRevision) throw new AdminAssistConcurrencyException(); return row;
		}
		private async Task<DiagnosticReport> BuildAsync(AdminAssistActor actor, AdminAssistDiagnosticRun row, DiagnosticRequest request, CancellationToken ct)
		{
			await source.RequireScopeAsync(actor, request, ct);
			var before = await revisions.GetConfigurationRevisionAsync(actor.DepartmentId, ct); var now = clock.GetUtcNow().UtcDateTime;
			var facts = await source.ReadAsync(actor, request, now, ct);
			var rawChanges = await store.ReadDiagnosticChangesAsync(actor.DepartmentId, request.FromUtc.AddDays(-7), request.UntilUtc, 100, ct);
			var changes = new List<DiagnosticChange>();
			var timelineAccess = new Dictionary<string, bool>();
			var usedTimelineSources = new HashSet<string>();
			foreach (var change in rawChanges.Take(100))
			{
				var entry = catalog.Settings.SingleOrDefault(s => s.Id == change.SettingId);
				if (entry == null || entry.Secret || entry.Classification != "Internal" || entry.ValueType is not ("boolean" or "number" or "integer")) continue;
				// Only reviewed scalar DepartmentSettings entries. Complex JSON, names, secret-presence and resource changes are omitted.
				if (!entry.Binding.StartsWith("DepartmentSettingTypes.", StringComparison.Ordinal)) continue;
				// An administrative journal entry does not grant access to a disabled/restricted
				// owning feature. Unmapped settings are omitted until their source mapping is reviewed.
				string visibleOwner = null;
				foreach (var owner in catalog.Capabilities.Where(c => c.SettingIds.Contains(entry.Id)))
				{
					if (!timelineAccess.TryGetValue(owner.Id, out var allowed))
					{
						var permission = await access.GetCapabilityAsync(actor, owner.Id, ct).WaitAsync(ct);
						allowed = permission?.State == EvidenceState.Known && permission.CanConfigure;
						timelineAccess[owner.Id] = allowed;
					}
					if (allowed) { visibleOwner = owner.Id; break; }
				}
				if (visibleOwner == null) continue;
				usedTimelineSources.Add(visibleOwner);
				changes.Add(change with { BeforeCode = Scalar(change.BeforeCode), AfterCode = Scalar(change.AfterCode), CorrelationId = SafeCorrelation(change.CorrelationId) });
			}
			foreach (var id in usedTimelineSources)
			{
				var permission = await access.GetCapabilityAsync(actor, id, ct).WaitAsync(ct);
				if (permission?.State != EvidenceState.Known || !permission.CanConfigure) throw new UnauthorizedAccessException();
			}
			if (before != await revisions.GetConfigurationRevisionAsync(actor.DepartmentId, ct)) throw new AdminAssistConcurrencyException();
			await source.RequireScopeAsync(actor, request, ct); await RequireAsync(actor, ct);
			return new(row.Id, row.Revision, row.Flow, row.CreatedOnUtc, now, catalog.Version, before.ToString(CultureInfo.InvariantCulture),
				DiagnosticPolicy.Outcome(facts.Checks), facts.Checks, changes, facts.Attempts, facts.TraceTruncated, rawChanges.Count > 100)
			{ StatusHistory = facts.StatusHistory };
		}
		private static string SafeCorrelation(string value) => value != null && value.Length is 32 or 36 && value.All(c => char.IsAsciiHexDigit(c) || c == '-') ? value : null;
		private static string Scalar(string value)
		{
			if (value == null || value.Length > 4096) return "redacted";
			// The journal normally wraps fields in a JSON object; no raw JSON escapes into a bundle.
			if (value.StartsWith("{", StringComparison.Ordinal))
			{
				try
				{
					using var json = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 4 });
					if (!json.RootElement.TryGetProperty("Setting", out var scalar) || scalar.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)) return "redacted";
					value = scalar.GetRawText();
				}
				catch (JsonException) { return "redacted"; }
			}
			if (bool.TryParse(value, out var boolean)) return boolean ? "true" : "false";
			return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number.ToString(CultureInfo.InvariantCulture) : "redacted";
		}
		private async Task AuditAsync(AdminAssistActor actor, string id, string action, CancellationToken ct) => await audit.SaveAuditLogAsync(new AuditLog
		{
			DepartmentId = actor.DepartmentId,
			ObjectDepartmentId = actor.DepartmentId,
			UserId = actor.UserId,
			ObjectId = id,
			LogType = (int)AuditLogTypes.AdminAssistDiagnosticAccess,
			LoggedOn = clock.GetUtcNow().UtcDateTime,
			Successful = true,
			// Successful means access was authorized; it does not attest that a later persistence/download completed.
			Message = action,
			Data = JsonSerializer.Serialize(new { runId = id, action, stage = "AccessAuthorized", version = DiagnosticPolicy.Version })
		}, ct);
	}
}
