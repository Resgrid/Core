using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;

namespace Resgrid.Services.Workforce
{
	/// <summary>
	/// Advanced Data Protection seam shared by the Phase E services (ADP catalog 28, Personnel family). Writes run
	/// as a workload caller (encrypted at rest, sentinel-safe: a REDACTED value posted back from an unrevealed page
	/// keeps the stored envelope); user reads honour the caller's Protected Data Grant; the costing and reporting
	/// engines decrypt through their broker purposes. Without the protection services rows are written and read
	/// as-is (a department that has not enrolled). No plaintext twin, cache or log ever carries a resolved value.
	/// </summary>
	public sealed class WorkforceProtectionSeam
	{
		private readonly Lazy<IProtectedWriteService> _write;
		private readonly Lazy<IProtectedReadService> _read;
		private readonly IProtectedGrantContext _grant;

		public WorkforceProtectionSeam(Lazy<IProtectedWriteService> write, Lazy<IProtectedReadService> read, IProtectedGrantContext grant)
		{
			_write = write;
			_read = read;
			_grant = grant;
		}

		public bool IsActive => _write?.Value != null;

		public static void Mark(IEntity row)
		{
			switch (row)
			{
				case WorkforceEmployerProfile r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case WorkforceAffiliatedEntity r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case WorkforceEstablishment r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case WorkforceLaborContractor r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case WorkforceWorker r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case EmployeeCompensationProfile r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case EmployeePayComponent r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case EmployeeCostComponent r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case WorkforceWorkEntry r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case WorkforceAnnualPayFact r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case FieldCostLine r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case PayDataReportingDemographic r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case PayDataReportRun r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case PayDataReportEmployeeSnapshot r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case PayDataReportRow r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
				case PayDataExportArtifact r: r.IsProtected = true; r.ProtectedCatalogVersion = Math.Max(r.ProtectedCatalogVersion ?? 0, WorkforceProtectedFields.CatalogVersion); break;
			}
		}

		/// <summary>Insert or update through the write seam. A new row is allocated first (the key binds the envelope), then enveloped and written again.</summary>
		public async Task<T> SaveAsync<T>(IRepository<T> repository, T entity, T existing, int departmentId, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, CancellationToken cancellationToken) where T : class, IEntity
		{
			if (!IsActive) return await repository.SaveOrUpdateAsync(entity, cancellationToken);
			var key = entity.IdValue as string;
			if (string.IsNullOrWhiteSpace(key))
			{
				var held = accessors.ToDictionary(a => a.Key, a => a.Value.Get(entity), StringComparer.OrdinalIgnoreCase);
				if (held.Values.All(string.IsNullOrEmpty)) return await repository.SaveOrUpdateAsync(entity, cancellationToken);
				foreach (var accessor in accessors) accessor.Value.Set(entity, null);
				var allocated = await repository.SaveOrUpdateAsync(entity, cancellationToken);
				foreach (var accessor in accessors) accessor.Value.Set(allocated, held[accessor.Key]);
				var inserted = await _write.Value.PrepareRecordsEntityWriteAsync(departmentId, allocated, null, (string)allocated.IdValue, accessors, () => Mark(allocated), null, null, true, cancellationToken);
				if (inserted != null && !inserted.Success) throw new InvalidOperationException("workforce_protected_write_refused");
				return await repository.SaveOrUpdateAsync(allocated, cancellationToken);
			}
			var result = await _write.Value.PrepareRecordsEntityWriteAsync(departmentId, entity, existing, key, accessors, () => Mark(entity), null, null, true, cancellationToken);
			if (result != null && !result.Success) throw new InvalidOperationException("workforce_protected_write_refused");
			return await repository.SaveOrUpdateAsync(entity, cancellationToken);
		}

		/// <summary>Binary column (export artifacts).</summary>
		public async Task<PayDataExportArtifact> SaveArtifactAsync(IPayDataExportArtifactRepository repository, PayDataExportArtifact artifact, int departmentId, CancellationToken cancellationToken)
		{
			if (!IsActive) return await repository.SaveOrUpdateAsync(artifact, cancellationToken);
			var data = artifact.Data;
			artifact.Data = null;
			var allocated = await repository.SaveOrUpdateAsync(artifact, cancellationToken);
			var result = await _write.Value.PrepareRecordsBinaryWriteAsync(departmentId, WorkforceProtectedFields.ExportDataFieldId, allocated.PayDataExportArtifactId, data, bytes => allocated.Data = bytes, () => Mark(allocated), null, null, true, cancellationToken);
			if (result != null && !result.Success) throw new InvalidOperationException("workforce_protected_write_refused");
			if (allocated.Data == null) allocated.Data = data;
			return await repository.SaveOrUpdateAsync(allocated, cancellationToken);
		}

		/// <summary>User read: a grant holder sees the values, everyone else REDACTED. Never throws.</summary>
		public async Task ResolveForReadAsync<T>(IReadOnlyList<T> rows, int departmentId, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors) where T : class, IEntity
		{
			if (_read?.Value == null || rows == null || rows.Count == 0) return;
			try { await _read.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, (string)r.IdValue)).ToList(), accessors, _grant?.GrantToken, _grant?.UserId); }
			catch (Exception ex) { Logging.LogException(ex, "Protected workforce rows could not be resolved for read."); }
		}

		/// <summary>Workload read for the costing / reporting engines (broker purpose allow-listed in DataProtectionConfig). Never throws; a denied purpose leaves envelopes in place and the caller flags NeedsReview.</summary>
		public async Task<bool> ResolveForWorkloadAsync<T>(IReadOnlyList<T> rows, int departmentId, string purpose, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors) where T : class, IEntity
		{
			if (_read?.Value == null || rows == null || rows.Count == 0) return true;
			try
			{
				var result = await _read.Value.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, rows.Select(r => (r, (string)r.IdValue)).ToList(), accessors);
				return result == null || !result.IsProtected || result.RedactedFields == null || result.RedactedFields.Count == 0;
			}
			catch (Exception ex) { Logging.LogException(ex, $"Protected workforce rows could not be resolved for the {purpose} workload."); return false; }
		}

		public async Task ResolveArtifactForReadAsync(PayDataExportArtifact artifact, int departmentId)
		{
			if (_read?.Value == null || artifact?.Data == null) return;
			try { await _read.Value.ResolveRecordsBinaryForReadAsync(departmentId, WorkforceProtectedFields.ExportDataFieldId, artifact.PayDataExportArtifactId, artifact.Data, bytes => artifact.Data = bytes, _grant?.GrantToken, _grant?.UserId); }
			catch (Exception ex) { Logging.LogException(ex, "Protected export artifact could not be resolved for read."); artifact.Data = null; }
		}

		/// <summary>True when a value is still an envelope or the redaction sentinel (the caller lacked a grant or the purpose was denied).</summary>
		public static bool IsUnavailable(string value) => value != null && (ProtectedDataEnvelope.HasEnvelopePrefix(value) || value == ProtectedDataEnvelope.RedactionValue);
	}
}
