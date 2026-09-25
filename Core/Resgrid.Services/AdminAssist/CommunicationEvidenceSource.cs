using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Recorded notification preferences and verified contact eligibility, never a delivery or device-health claim.</summary>
	public sealed class CommunicationEvidenceSource(INotificationImpactStore store, IAuthorizationService authorization) : IAdminAssistEvidenceSource
	{
		public string SourceId => "NotificationEligibility";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "notificationMembersWithoutChannel", "notificationMembersMissingProfile" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var bound = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			var rows = await store.ReadNotificationMembersAsync(actor.DepartmentId, bound, ct) ?? throw new InvalidOperationException();
			if (rows.Count > bound || rows.Any(r => r.DepartmentId != actor.DepartmentId || string.IsNullOrWhiteSpace(r.UserId)) ||
				rows.Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count) throw new InvalidOperationException();
			foreach (var row in rows)
				if (!await authorization.CanUserViewPersonAsync(actor.UserId, row.UserId, actor.DepartmentId).WaitAsync(ct)) throw new UnauthorizedAccessException();
			var missing = rows.Count(r => !r.ProfileId.HasValue);
			var incomplete = rows.Any(r => r.ProfileId.HasValue && (!r.Sms.HasValue || !r.Email.HasValue || !r.Push.HasValue));
			var noChannels = rows.Count(r => r.ProfileId.HasValue && r.Sms.HasValue && r.Email.HasValue && r.Push.HasValue &&
				NoChannels(NotificationChannelSelection.From(r.Sms.Value, r.MobileVerified, r.Email.Value, r.EmailVerified, r.Push.Value)));
			return new[] {
				new ConfigurationEvidence("notificationMembersMissingProfile", EvidenceState.Known, SourceId, "notification-eligibility-v1", now, Number: missing),
				new ConfigurationEvidence("notificationMembersWithoutChannel", incomplete || missing > 0 ? EvidenceState.Unknown : EvidenceState.Known,
					SourceId, "notification-eligibility-v1", now, Number: incomplete || missing > 0 ? null : noChannels, ReasonCode: incomplete || missing > 0 ? "IncompleteProfiles" : null)
			};
		}
		private static bool NoChannels(NotificationChannelSelection channels) => !channels.Sms && !channels.Email && !channels.Push;
	}
}
