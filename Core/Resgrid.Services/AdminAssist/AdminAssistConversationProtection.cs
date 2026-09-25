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
	/// <summary>All conversations have authenticated encryption; ADP adds its broker/grant envelope when enforced.</summary>
	public sealed class AdminAssistConversationProtection(IEncryptionService encryption, Lazy<IProtectedWriteService> write,
		Lazy<IProtectedReadService> read, IDepartmentDataProtectionService protection, IProtectedGrantContext grant) : IAdminAssistConversationProtection
	{
		private static readonly IReadOnlyDictionary<string, (Func<AiGenerationRow, string>, Action<AiGenerationRow, string>)> Fields =
			new Dictionary<string, (Func<AiGenerationRow, string>, Action<AiGenerationRow, string>)> { ["aigenerations.content"] = (row => row.Content, (row, value) => row.Content = value) };
		public async Task PreflightAsync(AdminAssistActor actor, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(SecurityConfig.EncryptionKey) || SecurityConfig.EncryptionKey.Length < 32 || SecurityConfig.EncryptionKey.Contains("CHANGEME", StringComparison.Ordinal) ||
				string.IsNullOrWhiteSpace(SecurityConfig.EncryptionSaltValue) || SecurityConfig.EncryptionSaltValue.Contains("CHANGEME", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
			if (await protection.ShouldEncryptNewWritesAsync(actor.DepartmentId).WaitAsync(ct) && await protection.GetPinnedCatalogVersionAsync(actor.DepartmentId).WaitAsync(ct) < ProtectedFieldCatalog.AiGenerationsCatalogVersion) throw new UnauthorizedAccessException();
			if ((await write.Value.PreflightWriteAsync(actor.DepartmentId, grant.GrantToken, actor.UserId, false, ct))?.Success != true) throw new UnauthorizedAccessException();
		}
		private static string Binding(AiGenerationRow row) => "AdminAssist:" + row.UserId + ":" + row.ConversationId + ":" + row.Id;
		public async Task ProtectAsync(AdminAssistActor actor, AiGenerationRow row, CancellationToken ct)
		{
			if (row.DepartmentId != actor.DepartmentId || row.UserId != actor.UserId) throw new UnauthorizedAccessException();
			await PreflightAsync(actor, ct);
			row.Content = encryption.EncryptForDepartment(row.Content, actor.DepartmentId, Binding(row));
			if (!row.Content.StartsWith("enc2:", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
			var prepared = await write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, row, null, row.Id, Fields,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = 31; }, grant.GrantToken, actor.UserId, false, ct);
			if (prepared?.Success != true || prepared.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content)) throw new UnauthorizedAccessException();
		}
		public async Task<AskStoredContent> ReadAsync(AdminAssistActor actor, AiGenerationRow row, CancellationToken ct)
		{
			if (row.DepartmentId != actor.DepartmentId || row.UserId != actor.UserId) throw new UnauthorizedAccessException();
			if (await protection.IsProtectionEnforcedAsync(actor.DepartmentId).WaitAsync(ct) && await protection.GetPinnedCatalogVersionAsync(actor.DepartmentId).WaitAsync(ct) < ProtectedFieldCatalog.AiGenerationsCatalogVersion) return null;
			await read.Value.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, new[] { (row, row.Id) }, Fields, grant.GrantToken, actor.UserId, ct);
			if (row.Content == null || !row.Content.StartsWith("enc2:", StringComparison.Ordinal)) return null;
			try { return JsonSerializer.Deserialize<AskStoredContent>(encryption.DecryptForDepartment(row.Content, actor.DepartmentId, Binding(row))); }
			catch (Exception) { return null; } // Corrupt/wrong-key payloads are never echoed or logged.
		}
	}
}
