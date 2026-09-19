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
	/// Advanced Data Protection seam for ADP catalog 28 (plan C8): DTR customer signer names, expense descriptions
	/// and attachment names/bytes. Mirrors the certification catalog-27 seam: writes run as a workload caller
	/// (encrypted at rest, sentinel-safe), user reads see REDACTED for protected values until the reveal path lands,
	/// and attachment bytes ride the generic binary seam. Without the protection services rows are written and read as-is.
	/// </summary>
	public partial class DeploymentService
	{
		internal static void MarkProtected(DeploymentTimeReport row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion ?? 0, DeploymentProtectedFields.CatalogVersion); }
		internal static void MarkProtected(DeploymentExpense row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion ?? 0, DeploymentProtectedFields.CatalogVersion); }
		internal static void MarkProtected(DeploymentAttachment row) { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion ?? 0, DeploymentProtectedFields.CatalogVersion); }

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
		private async Task<T> InsertProtectedAsync<T>(IRepository<T> repository, T entity, Func<T, string> rowKey,
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

		/// <summary>Attachment rows: text columns through the entity seam, bytes through the binary seam, both keyed by the allocated identity.</summary>
		private async Task<DeploymentAttachment> SaveProtectedAttachmentAsync(DeploymentAttachment attachment, CancellationToken cancellationToken)
		{
			if (_protectedWrite?.Value == null)
				return await _attachments.SaveOrUpdateAsync(attachment, cancellationToken);

			var bytes = attachment.Data;
			attachment.Data = null;
			var saved = await InsertProtectedAsync(_attachments, attachment, a => a.DeploymentAttachmentId.ToString(), DeploymentProtectedFields.Attachment, MarkProtected, attachment.DepartmentId, cancellationToken);
			saved.Data = bytes;
			var result = await _protectedWrite.Value.PrepareRecordsBinaryWriteAsync(saved.DepartmentId, DeploymentProtectedFields.AttachmentDataFieldId, saved.DeploymentAttachmentId.ToString(), bytes,
				data => saved.Data = data, () => MarkProtected(saved), null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException("deployments_protected_write_refused");
			return await _attachments.SaveOrUpdateAsync(saved, cancellationToken);
		}

		/// <summary>User-facing read: protected values become REDACTED (no grant is carried on this path yet). Never throws.</summary>
		internal async Task ResolveReadAsync<T>(IReadOnlyList<T> rows, Func<T, string> rowKey, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, int departmentId) where T : class
		{
			if (_protectedRead?.Value == null || rows == null || rows.Count == 0) return;
			try { await _protectedRead.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, rowKey(r))).ToList(), accessors, null, null); }
			catch (Exception ex) { Logging.LogException(ex, "Protected deployment rows could not be resolved for read."); }
		}

		/// <summary>User-facing read of one cataloged blob: enveloped bytes are nulled rather than served. Never throws.</summary>
		internal async Task ResolveBinaryReadAsync(int departmentId, string fieldId, string rowKey, byte[] data, Action<byte[]> apply)
		{
			if (_protectedRead?.Value == null || data == null) return;
			try { await _protectedRead.Value.ResolveRecordsBinaryForReadAsync(departmentId, fieldId, rowKey, data, apply, null, null); }
			catch (Exception ex) { Logging.LogException(ex, "A protected deployment file could not be resolved for read."); }
		}
	}
}
