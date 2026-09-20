using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Advanced Data Protection seam for ADP catalog 27: the deployment wrapper's internal notes only. Daily time
	/// reports, entries, expenses and attachments are customer-facing (the customer signs the DTR; receipts, DTR PDFs
	/// and manifests ride the invoice packet) and are deliberately not protected so people outside the department can
	/// read them. Writes run as a workload caller (encrypted at rest, sentinel-safe); user reads honour the caller's
	/// Protected Data Grant. Without the protection services rows are written and read as-is.
	/// </summary>
	public partial class DeploymentService
	{
		internal static void MarkProtected(Deployment row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion ?? 0, DeploymentProtectedFields.CatalogVersion); }

		internal async Task<T> SaveProtectedAsync<T>(IRepository<T> repository, T entity, T existing, Func<T, string> rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action<T> markProtected, int departmentId, CancellationToken cancellationToken) where T : class, IEntity
		{
			if (_protectedWrite?.Value == null)
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			var key = rowKey(entity);
			if (string.IsNullOrWhiteSpace(key) || key == "0")
				return await InsertProtectedAsync(repository, entity, rowKey, accessors, markProtected, departmentId, cancellationToken);

			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, entity, existing, key, accessors, () => markProtected(entity), null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("deployments_protected_write_refused");
			return await repository.SaveOrUpdateAsync(entity, cancellationToken);
		}

		/// <summary>A new row has no key until inserted and the key binds the envelope: hold the cataloged columns, allocate, encrypt, write.</summary>
		internal async Task<T> InsertProtectedAsync<T>(IRepository<T> repository, T entity, Func<T, string> rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action<T> markProtected, int departmentId, CancellationToken cancellationToken) where T : class, IEntity
		{
			var held = accessors.ToDictionary(a => a.Key, a => a.Value.Get(entity), StringComparer.OrdinalIgnoreCase);
			if (held.Values.All(string.IsNullOrEmpty))
				return await repository.SaveOrUpdateAsync(entity, cancellationToken);

			foreach (var accessor in accessors) accessor.Value.Set(entity, null);
			var allocated = await repository.SaveOrUpdateAsync(entity, cancellationToken);
			foreach (var accessor in accessors) accessor.Value.Set(allocated, held[accessor.Key]);

			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, allocated, null, rowKey(allocated), accessors, () => markProtected(allocated), null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("deployments_protected_write_refused");
			return await repository.SaveOrUpdateAsync(allocated, cancellationToken);
		}

		/// <summary>User-facing read: a caller presenting a valid Protected Data Grant sees the values, everyone else sees REDACTED. Never throws.</summary>
		internal async Task ResolveReadAsync<T>(IReadOnlyList<T> rows, Func<T, string> rowKey, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, int departmentId) where T : class
		{
			if (_protectedRead?.Value == null || rows == null || rows.Count == 0) return;
			try { await _protectedRead.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, rowKey(r))).ToList(), accessors, _grant?.GrantToken, _grant?.UserId); }
			catch (Exception ex) { Logging.LogException(ex, "Protected deployment rows could not be resolved for read."); }
		}
	}
}
