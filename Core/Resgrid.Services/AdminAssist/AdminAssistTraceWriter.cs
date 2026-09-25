using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistTraceWriter(IAdminAssistTraceStore store, IProtectedWriteService write,
		IDepartmentDataProtectionService protection, IFeatureToggleService flags) : IAdminAssistTraceWriter
	{
		private static readonly IReadOnlyDictionary<string, (Func<AdminAssistDispatchTraceRow, string>, Action<AdminAssistDispatchTraceRow, string>)> Fields =
			new Dictionary<string, (Func<AdminAssistDispatchTraceRow, string>, Action<AdminAssistDispatchTraceRow, string>)>
			{ ["adminassistdispatchtraces.content"] = (row => row.Content, (row, value) => row.Content = value) };
		public async Task PersistAsync(DispatchTraceObservation observation, CancellationToken ct)
		{
			var prepared = await PrepareAsync(observation, ct);
			if (prepared != null) await PersistPreparedAsync(prepared, ct);
		}
		public async Task<AdminAssistDispatchTraceRow> PrepareAsync(DispatchTraceObservation observation, CancellationToken ct)
		{
			ArgumentNullException.ThrowIfNull(observation);
			// This check runs in the background, never on the emergency sender's critical path.
			if (!Config.AdminAssistConfig.CaptureDispatchTraces ||
				!await AdminAssistFeatureAvailability.IsEnabledAsync(flags, observation.DepartmentId, false, ct)) return null;
			var row = new AdminAssistDispatchTraceRow { AdminAssistDispatchTraceId = observation.Id, DepartmentId = observation.DepartmentId,
				CallId = observation.CallId, AttemptId = observation.AttemptId, Stage = observation.Stage.ToString(), ResolverVersion = observation.ResolverVersion ?? "legacy-unrecorded",
				OccurredOn = observation.OccurredOnUtc, Content = JsonSerializer.Serialize(observation) };
			DispatchTraceEnvelope.Validate(row);
			await ProtectAsync(row, ct);
			return row;
		}
		public async Task PersistPreparedAsync(AdminAssistDispatchTraceRow row, CancellationToken ct)
		{
			DispatchTraceEnvelope.Validate(row);
			// A late queue replay must not resurrect a deleted department's derived evidence. A concurrent
			// deletion is fenced by SaveTraceAsync's department lock and is retried without acknowledging.
			if (!await store.TraceDepartmentExistsAsync(row.DepartmentId, ct)) return;
			// Enrollment may have started while this envelope was queued. Recheck the owning write gate;
			// existing protected envelopes are preserved even if the subscription has since expired.
			await ProtectAsync(row, ct);
			await store.SaveTraceAsync(row, ct);
		}
		private async Task ProtectAsync(AdminAssistDispatchTraceRow row, CancellationToken ct)
		{
			if (await protection.ShouldEncryptNewWritesAsync(row.DepartmentId).WaitAsync(ct) && await protection.GetPinnedCatalogVersionAsync(row.DepartmentId).WaitAsync(ct) < 30)
				throw new InvalidOperationException("Trace protection catalog upgrade required.");
			var result = await write.PrepareRecordsEntityWriteAsync(row.DepartmentId, row, null, row.AdminAssistDispatchTraceId, Fields,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = 30; }, null, null, true, ct);
			if (result?.Success != true || result.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content)) throw new UnauthorizedAccessException();
			DispatchTraceEnvelope.Validate(row);
		}
	}
}
