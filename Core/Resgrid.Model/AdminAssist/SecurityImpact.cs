using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed class SecurityMemberEvidence
	{
		public int DepartmentId { get; set; }
		public int MemberId { get; set; }
		public string UserId { get; set; }
		public bool? TwoFactorEnabled { get; set; }
		public DateTime? PasswordLastSetOn { get; set; }
	}
	public sealed class SecuritySessionEvidence
	{
		public int DepartmentId { get; set; }
		public string Id { get; set; }
		public string UserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime LastActiveOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public long AuthenticationGeneration { get; set; }
		public long? CurrentGeneration { get; set; }
	}
	public sealed record SecurityImpactEvidence(IReadOnlyList<SecurityMemberEvidence> Members, IReadOnlyList<SecuritySessionEvidence> Sessions, int EnabledSsoProviders);
	public interface ISecurityImpactStore
	{
		Task<DepartmentSecurityPolicy> ReadSecurityPolicyAsync(int departmentId, CancellationToken cancellationToken);
		Task<SecurityImpactEvidence> ReadSecurityImpactAsync(int departmentId, DateTime nowUtc, int bound, CancellationToken cancellationToken);
	}
	public interface ISecurityImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, ConfigurationImpactRequest request, CancellationToken cancellationToken = default);
	}
}
