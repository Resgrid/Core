using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Resgrid.Model.AdminAssist
{
	public interface IAdminAssistPermissionEvaluator
	{
		Task<IReadOnlyDictionary<string, bool>> EvaluateCurrentTargetsAsync(AdminAssistActor administrator, string permissionType, IReadOnlyList<string> targetIds, CancellationToken ct);
		Task<bool?> EvaluateCurrentAsync(AdminAssistActor administrator, string memberId, string permissionType, string targetId, CancellationToken ct);
	}
	public sealed record PermissionImpactRequest(string ExpectedRevision, string PermissionType, int Action,
		bool LockToGroup, int[] RoleIds);
	public sealed record PermissionRoleOption(int Id, string Name);
	public interface IPermissionImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, PermissionImpactRequest request, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<PermissionRoleOption>> GetRoleOptionsAsync(AdminAssistActor actor, string expectedRevision, CancellationToken cancellationToken = default);
	}
}
