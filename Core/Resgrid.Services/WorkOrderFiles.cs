using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;
using Resgrid.Services.Records;

namespace Resgrid.Services
{
	public sealed partial class WorkOrdersService
	{
		public async Task AddFileAsync(ChecklistActor actor, int id, int revision, string filename, string contentType, byte[] data)
		{
			await RequireWriteAsync(actor); await FileWriteOrderAsync(actor, id, revision);
			Text(filename, 200, true);
			if (data == null || data.Length == 0 || data.Length > 10 * 1024 * 1024 || contentType is not ("image/png" or "image/jpeg" or "application/pdf")) throw new WorkOrderException(400, "InvalidFile");
			AttachmentHygieneResult clean;
			try
			{
				if (contentType.StartsWith("image/", StringComparison.Ordinal))
				{
					var info = SixLabors.ImageSharp.Image.Identify(data);
					if (info == null || (long)info.Width * info.Height > RecordAttachmentHygiene.MaxPixels) throw new WorkOrderException(400, "InvalidFile");
				}
				else if (data.Length < 5 || System.Text.Encoding.ASCII.GetString(data, 0, 5) != "%PDF-") throw new WorkOrderException(400, "InvalidFile");
				clean = RecordAttachmentHygiene.Sanitize(filename, contentType, data);
			}
			catch (Exception ex) when (ex is ArgumentException or SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException or NotSupportedException) { throw new WorkOrderException(400, "InvalidFile"); }
			if (clean.Data.Length > 10 * 1024 * 1024 || clean.ContentType != contentType) throw new WorkOrderException(400, "InvalidFile");
			var scan = await _scanner.ScanAsync(clean.FileName, clean.ContentType, clean.Data);
			if (scan?.State != RmsAttachmentScanState.Clean) throw new WorkOrderException(409, "ScanRequired");
			await TransactionAsync(actor, async events =>
			{
				var row = await FileWriteOrderAsync(actor, id, revision);
				if ((await _store.ChildrenAsync<WorkOrderFile>(actor.DepartmentId, id)).Count >= 100) throw new WorkOrderException(400, "FileLimit");
				var file = New<WorkOrderFile>(actor, id); file.ContentType = clean.ContentType; file.Size = clean.Data.Length; file.Sha256 = Convert.ToHexString(SHA256.HashData(clean.Data)); file.ScanState = (int)scan.State;
				await _store.AllocateAsync(file); file.Content = clean.FileName; file.Data = clean.Data;
				var result = await _write.Value.PrepareRecordsBinaryWriteAsync(actor.DepartmentId, "workorderfiles.data", Key(file), file.Data, bytes => file.Data = bytes, () => file.IsProtected = true, actor.GrantToken, actor.UserId, false);
				if (result?.Success != true || result.IsProtected && !ProtectedReadService.IsBinaryEnveloped(file.Data)) throw new WorkOrderException(403, "ProtectedDataRequired");
				await SaveAsync(actor, file); await ChangedAsync(actor, row, WorkOrderActivityType.FileAdded, events); return true;
			});
		}
		private async Task<WorkOrder> FileWriteOrderAsync(ChecklistActor actor, int id, int revision)
		{
			var row = await ReadOrderAsync(actor, id); Revision(row, revision);
			if (Terminal(row) || row.Status == 5 || row.CreatedBy != actor.UserId && !await _authorization.CanContributeAsync(actor, row)) throw new WorkOrderException(403, "PermissionRequired");
			return row;
		}
		public async Task<WorkOrderFile> GetFileAsync(ChecklistActor actor, int id)
		{
			await _authorization.RequireMemberAsync(actor); var metadata = await _store.GetAsync<WorkOrderFile>(actor.DepartmentId, id, false);
			if (metadata?.WorkOrderId == null || metadata.WithdrawnOn.HasValue) throw new WorkOrderException(404, "Unavailable");
			await ReadOrderAsync(actor, metadata.WorkOrderId.Value);
			var file = await RevealAsync(actor, await _store.GetAsync<WorkOrderFile>(actor.DepartmentId, id));
			if (file.ScanState != (int)RmsAttachmentScanState.Clean || file.WithdrawnOn.HasValue) throw new WorkOrderException(404, "Unavailable");
			var enveloped = ProtectedReadService.IsBinaryEnveloped(file.Data);
			var result = await _read.Value.ResolveRecordsBinaryForReadAsync(actor.DepartmentId, "workorderfiles.data", Key(file), file.Data, bytes => file.Data = bytes, actor.GrantToken, actor.UserId);
			if (result == null || result.RedactedFields.Count > 0 || result.IsProtected && !enveloped || file.Data == null) throw new WorkOrderException(403, "ProtectedDataRequired");
			if (Convert.ToHexString(SHA256.HashData(file.Data)) != file.Sha256) throw new WorkOrderException(409, "IntegrityFailed"); return file;
		}
		public async Task WithdrawFileAsync(ChecklistActor actor, int id, int fileId, int revision, string reason)
		{
			Text(reason, 4000, true);
			await TransactionAsync(actor, async events =>
			{
				var row = await FileWriteOrderAsync(actor, id, revision); var file = await _store.GetAsync<WorkOrderFile>(actor.DepartmentId, fileId);
				if (file?.WorkOrderId != id || file.WithdrawnOn.HasValue) throw new WorkOrderException(404, "Unavailable");
				await RevealAsync(actor, file); file.WithdrawnOn = Now; file.Revision++; await SaveAsync(actor, file);
				await ChangedAsync(actor, row, WorkOrderActivityType.FileWithdrawn, events, reason); return true;
			});
		}
	}
}
