using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Declared future scenario, never a claimed count of historical notification events.</summary>
	public sealed record NotificationImpactRequest(string ExpectedRevision, bool SuppressStaffing, int WindowDays, int EventsPerMember);
	/// <summary>Internal metadata projection; no contact addresses, tokens, names or staffing notes.</summary>
	public sealed class NotificationMemberEvidence
	{
		public int DepartmentId { get; set; }
		public int MemberId { get; set; }
		public string UserId { get; set; }
		public int? ProfileId { get; set; }
		public bool? Sms { get; set; }
		public bool? MobileVerified { get; set; }
		public bool? Email { get; set; }
		public bool? EmailVerified { get; set; }
		public bool? Push { get; set; }
		public bool StaffingKnown { get; set; }
		public int? Staffing { get; set; }
	}
	public interface INotificationImpactStore
	{
		Task<IReadOnlyList<NotificationMemberEvidence>> ReadNotificationMembersAsync(int departmentId, int bound, CancellationToken cancellationToken);
	}
	public interface INotificationImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, NotificationImpactRequest request, CancellationToken cancellationToken = default);
	}
}
