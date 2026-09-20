using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Repositories;

namespace Resgrid.Services
{
	/// <summary>
	/// Advanced Data Protection seam for ADP catalog 27 (unit certification records and credit entries; plan D2). Mirrors
	/// the invoicing catalog-26 seam: writes run as a workload caller (encrypted at rest, sentinel-safe), user reads see
	/// REDACTED for protected values until the reveal path lands, and the file bytes ride the generic binary seam.
	/// PersonnelCertifications itself stays on the catalog-6 PrepareCertificationWriteAsync path.
	/// </summary>
	public partial class CertificationService
	{
		private static void MarkProtected(UnitCertification row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion ?? 0, CertificationProtectedFields.CatalogVersion); }
		private static void MarkProtected(PersonnelCertificationCredit row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion ?? 0, CertificationProtectedFields.CatalogVersion); }

		private async Task<T> SaveProtectedAsync<T>(IRepository<T> repository, T entity, T existing, Func<T, string> rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action<T> markProtected, int departmentId, CancellationToken cancellationToken) where T : class, IEntity
		{
			if (_protectedWriteService == null)
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			var key = rowKey(entity);
			if (string.IsNullOrWhiteSpace(key))
				return await InsertProtectedAsync(repository, entity, rowKey, accessors, markProtected, departmentId, cancellationToken);

			var result = await _protectedWriteService.Value.PrepareRecordsEntityWriteAsync(departmentId, entity, existing, key, accessors, () => markProtected(entity), null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("certifications_protected_write_refused");
			return await repository.SaveOrUpdateAsync(entity, cancellationToken);
		}

		/// <summary>A new row has no key until inserted and the key binds the envelope: hold the cataloged columns, allocate, encrypt, write.</summary>
		private async Task<T> InsertProtectedAsync<T>(IRepository<T> repository, T entity, Func<T, string> rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action<T> markProtected, int departmentId, CancellationToken cancellationToken) where T : class, IEntity
		{
			if (_protectedWriteService == null)
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			var held = accessors.ToDictionary(a => a.Key, a => a.Value.Get(entity), StringComparer.OrdinalIgnoreCase);
			if (held.Values.All(string.IsNullOrEmpty))
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			foreach (var accessor in accessors) accessor.Value.Set(entity, null);
			var allocated = await repository.SaveOrUpdateAsync(entity, cancellationToken);
			foreach (var accessor in accessors) accessor.Value.Set(allocated, held[accessor.Key]);

			var result = await _protectedWriteService.Value.PrepareRecordsEntityWriteAsync(departmentId, allocated, null, rowKey(allocated), accessors, () => markProtected(allocated), null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("certifications_protected_write_refused");
			return await repository.SaveOrUpdateAsync(allocated, cancellationToken);
		}

		private async Task ProtectBinaryAsync(int departmentId, string fieldId, string rowKey, byte[] data, Action<byte[]> apply, Action markProtected, CancellationToken cancellationToken)
		{
			if (_protectedWriteService == null || data == null || data.Length == 0)
				return;
			var result = await _protectedWriteService.Value.PrepareRecordsBinaryWriteAsync(departmentId, fieldId, rowKey, data, apply, markProtected, null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("certifications_protected_write_refused");
		}

		/// <summary>User-facing read: a caller presenting a valid Protected Data Grant sees the values, everyone else sees REDACTED. Never throws.</summary>
		private async Task ResolveReadAsync<T>(IReadOnlyList<T> rows, Func<T, string> rowKey, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, int departmentId) where T : class
		{
			if (_protectedRead == null || rows == null || rows.Count == 0) return;
			try { await _protectedRead.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, rowKey(r))).ToList(), accessors, _grant?.GrantToken, _grant?.UserId); }
			catch (Exception ex) { Logging.LogException(ex, "Protected certification rows could not be resolved for read."); }
		}

		/// <summary>User-facing read of one cataloged blob: enveloped bytes are nulled rather than served. Never throws.</summary>
		private async Task ResolveBinaryReadAsync(int departmentId, string fieldId, string rowKey, byte[] data, Action<byte[]> apply)
		{
			if (_protectedRead == null || data == null) return;
			try { await _protectedRead.Value.ResolveRecordsBinaryForReadAsync(departmentId, fieldId, rowKey, data, apply, _grant?.GrantToken, _grant?.UserId); }
			catch (Exception ex) { Logging.LogException(ex, "A protected certification file could not be resolved for read."); }
		}
	}
}
