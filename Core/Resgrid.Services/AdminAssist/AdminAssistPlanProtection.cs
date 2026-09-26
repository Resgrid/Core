using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistPlanProtection(IEncryptionService encryption, IProtectedWriteService write,
		IProtectedReadService read, IDepartmentDataProtectionService protection, IProtectedGrantContext grant) : IAdminAssistPlanProtection
	{
		private static readonly IReadOnlyDictionary<string, (Func<AdminAssistPlanRow, string>, Action<AdminAssistPlanRow, string>)> Fields =
			new Dictionary<string, (Func<AdminAssistPlanRow, string>, Action<AdminAssistPlanRow, string>)> { ["adminassistplans.content"] = (r => r.Content, (r, value) => r.Content = value) };
		private static string Binding(AdminAssistPlanRow row) => "AdminAssistPlan:" + row.UserId + ":" + row.Id;
		public async Task RequireAsync(AdminAssistActor actor, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(SecurityConfig.EncryptionKey) || SecurityConfig.EncryptionKey.Length < 32 || SecurityConfig.EncryptionKey.Contains("CHANGEME", StringComparison.Ordinal) ||
				string.IsNullOrWhiteSpace(SecurityConfig.EncryptionSaltValue) || SecurityConfig.EncryptionSaltValue.Contains("CHANGEME", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
			if (await protection.ShouldEncryptNewWritesAsync(actor.DepartmentId).WaitAsync(ct) && await protection.GetPinnedCatalogVersionAsync(actor.DepartmentId).WaitAsync(ct) < ProtectedFieldCatalog.AdminAssistPlansCatalogVersion) throw new UnauthorizedAccessException();
			if ((await write.PreflightWriteAsync(actor.DepartmentId, grant.GrantToken, actor.UserId, false, ct))?.Success != true) throw new UnauthorizedAccessException();
		}
		public async Task ProtectAsync(AdminAssistActor actor, AdminAssistPlanRow row, PlanContent request, CancellationToken ct)
		{
			if (row.DepartmentId != actor.DepartmentId || row.UserId != actor.UserId) throw new UnauthorizedAccessException();
			await RequireAsync(actor, ct);
			row.Content = encryption.EncryptForDepartment(JsonSerializer.Serialize(request), actor.DepartmentId, Binding(row));
			// An updated row arrives with its stored markers. Only this write's protection decides them, so a department that
			// stopped encrypting new writes saves enc2: content unmarked instead of failing the envelope check.
			row.IsProtected = false;
			row.ProtectedCatalogVersion = null;
			var result = await write.PrepareRecordsEntityWriteAsync(actor.DepartmentId, row, null, row.Id, Fields,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = ProtectedFieldCatalog.AdminAssistPlansCatalogVersion; }, grant.GrantToken, actor.UserId, false, ct);
			if (result?.Success != true || (result.IsProtected ? !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content) : !row.Content.StartsWith("enc2:", StringComparison.Ordinal))) throw new UnauthorizedAccessException();
		}
		public async Task<PlanContent> ReadAsync(AdminAssistActor actor, AdminAssistPlanRow row, CancellationToken ct)
		{
			if (row.DepartmentId != actor.DepartmentId || (row.UserId != actor.UserId && !row.Shared)) throw new UnauthorizedAccessException();
			await RequireAsync(actor, ct);
			await read.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, new[] { (row, row.Id) }, Fields, grant.GrantToken, actor.UserId, ct);
			if (row.Content?.StartsWith("enc2:", StringComparison.Ordinal) != true) throw new UnauthorizedAccessException();
			try { return JsonSerializer.Deserialize<PlanContent>(encryption.DecryptForDepartment(row.Content, actor.DepartmentId, Binding(row))) ?? throw new UnauthorizedAccessException(); }
			catch (Exception) { throw new UnauthorizedAccessException(); }
		}
	}
}
