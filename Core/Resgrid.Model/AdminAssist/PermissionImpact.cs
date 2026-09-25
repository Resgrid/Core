using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Resgrid.Model.AdminAssist
{
	public sealed record PermissionImpactRequest(string ExpectedRevision, string PermissionType, int Action,
		bool LockToGroup, int[] RoleIds);
	public sealed record PermissionRoleOption(int Id, string Name);
	public interface IPermissionImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, PermissionImpactRequest request, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<PermissionRoleOption>> GetRoleOptionsAsync(AdminAssistActor actor, string expectedRevision, CancellationToken cancellationToken = default);
	}
}
