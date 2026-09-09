using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>History is encrypted at rest and redacted for Workflow/audit viewers; this seam never decrypts or accepts a user grant.</summary>
	public class ReadinessHistoryProtectionService : IReadinessHistoryProtectionService
	{
		private readonly IProtectedWriteService _write;
		private readonly IDepartmentDataProtectionService _policy;
		public ReadinessHistoryProtectionService(IProtectedWriteService write, IDepartmentDataProtectionService policy) { _write = write; _policy = policy; }
		public async Task ProtectAsync<T>(int departmentId, string rowKey, T entity, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> fields, CancellationToken ct = default) where T : class
		{
			if (fields.Values.Any(f => f.Get(entity) == ProtectedDataEnvelope.RedactionValue))
				throw new InvalidOperationException("A redacted history view cannot replace stored history.");
			var result = await _write.PrepareRecordsEntityWriteAsync(departmentId, entity, null, rowKey, fields, null, null, null, true, ct);
			if (result?.Success != true || result.IsProtected && fields.Values.Any(f => !string.IsNullOrEmpty(f.Get(entity)) && !ProtectedDataEnvelope.HasEnvelopePrefix(f.Get(entity))))
				throw new InvalidOperationException("Readiness history protection is unavailable or requires a catalog upgrade.");
		}
		public async Task<T> ForDisplayAsync<T>(int departmentId, T entity, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> fields) where T : class
		{
			if (entity == null) return null;
			// Never mutate repository objects: a masked value must not be saved over an envelope by a later command.
			var copy = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(entity, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore }));
			// History views use their explicit fields; navigation objects can contain unprojected parent data.
			if (copy is WorkflowRun run) run.Workflow = null;
			if (copy is WorkflowRunLog log) { log.WorkflowRun = null; log.WorkflowStep = null; }
			bool enforced;
			try { enforced = await _policy.IsProtectionEnforcedAsync(departmentId); }
			catch { enforced = true; }
			foreach (var field in fields.Values)
				if (!string.IsNullOrEmpty(field.Get(copy)) && (enforced || ProtectedDataEnvelope.HasEnvelopePrefix(field.Get(copy)))) field.Set(copy, ProtectedDataEnvelope.RedactionValue);
			return copy;
		}
	}
}
