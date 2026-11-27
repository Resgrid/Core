using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Certifications;

namespace Resgrid.Model
{
	/// <summary>
	/// The three wellness roles (protocols-training-knowledge-plan §13b, decided 2026-11-26): each is an assigned personnel role, and the
	/// department may also link a certification type of its own whose requirements and validity it decides. Not persisted.
	/// </summary>
	public enum WellnessRoleKinds
	{
		/// <summary>Reads every member's exposure log, keeps screening flags and follow-ups (WL0).</summary>
		WellnessOfficer = 1,

		/// <summary>Receives bloodborne-pathogen and infectious-disease exposure reports and runs the cases (WL1).</summary>
		DesignatedOfficer = 2,

		/// <summary>Receives the critical-incident anniversary reminders a member chooses to share (WL0).</summary>
		PeerSupportTeam = 3
	}

	/// <summary>A wellness role member's certification of the role's linked type. Not persisted.</summary>
	public enum WellnessCertificationStates
	{
		/// <summary>The role has no certification type linked (or the linked type no longer exists).</summary>
		NotLinked = 0,

		/// <summary>An active record that does not expire within <see cref="WellnessRoleRules.ExpiringSoonDays"/>.</summary>
		Current = 1,

		/// <summary>Still valid, but it expires within <see cref="WellnessRoleRules.ExpiringSoonDays"/>.</summary>
		Expiring = 2,

		/// <summary>The member held the type, but no record of it is valid today (expired, suspended, revoked, pending verification).</summary>
		Expired = 3,

		/// <summary>The member never held the type.</summary>
		Missing = 4
	}

	/// <summary>One member's certification status for a wellness role.</summary>
	public sealed class WellnessCertificationStatus
	{
		public WellnessCertificationStates State { get; set; }

		/// <summary>The expiry of the valid record (Current, Expiring) or of the latest lapsed one (Expired); null when none applies.</summary>
		public DateTime? ExpiresOn { get; set; }

		/// <summary>True for Current and Expiring: the certification counts today.</summary>
		public bool IsCurrent => State == WellnessCertificationStates.Current || State == WellnessCertificationStates.Expiring;
	}

	/// <summary>
	/// The rule of the wellness roles (decided 2026-11-26). Role membership decides who acts: who reads the logs and receives the reports
	/// and reminders. A certification type linked to a role is shown as each member's status (current, expiring, expired, missing). Only
	/// with <see cref="WellnessSettings.RequireCurrentCertification"/> on does a member without a current certification of the role's
	/// linked type stop acting in it; a role with no linked type is never affected. The peer support team may instead be "every holder of
	/// its certification" when no peer support role is named (the behaviour before the role existed). Pure: no clock, no I/O.
	/// </summary>
	public static class WellnessRoleRules
	{
		/// <summary>A certification expiring within this many days shows as expiring (the same window as the BBP training line).</summary>
		public const int ExpiringSoonDays = MemberHealthRules.BbpExpiringSoonDays;

		public static readonly IReadOnlyList<WellnessRoleKinds> Kinds = new[] { WellnessRoleKinds.WellnessOfficer, WellnessRoleKinds.DesignatedOfficer, WellnessRoleKinds.PeerSupportTeam };

		/// <summary>The personnel role of a wellness role; the designated officer falls back to the wellness officer role. 0 = none.</summary>
		public static int RoleIdFor(WellnessSettings settings, WellnessRoleKinds kind)
		{
			if (settings == null)
				return 0;
			switch (kind)
			{
				case WellnessRoleKinds.WellnessOfficer:
					return Math.Max(0, settings.WellnessOfficerRoleId);
				case WellnessRoleKinds.DesignatedOfficer:
					return settings.DesignatedOfficerRoleId > 0 ? settings.DesignatedOfficerRoleId : Math.Max(0, settings.WellnessOfficerRoleId);
				case WellnessRoleKinds.PeerSupportTeam:
					return Math.Max(0, settings.PeerTeamRoleId);
				default:
					return 0;
			}
		}

		/// <summary>
		/// The certification type linked to a wellness role. While no designated officer role is named the wellness officer role serves,
		/// and so does the wellness officer certification unless a designated officer certification is named. 0 = none.
		/// </summary>
		public static int CertificationTypeIdFor(WellnessSettings settings, WellnessRoleKinds kind)
		{
			if (settings == null)
				return 0;
			switch (kind)
			{
				case WellnessRoleKinds.WellnessOfficer:
					return Math.Max(0, settings.WellnessOfficerCertificationTypeId);
				case WellnessRoleKinds.DesignatedOfficer:
					if (settings.DesignatedOfficerCertificationTypeId > 0)
						return settings.DesignatedOfficerCertificationTypeId;
					return settings.DesignatedOfficerRoleId > 0 ? 0 : Math.Max(0, settings.WellnessOfficerCertificationTypeId);
				case WellnessRoleKinds.PeerSupportTeam:
					return Math.Max(0, settings.PeerTeamCertificationTypeId);
				default:
					return 0;
			}
		}

		/// <summary>True when the department has a peer support team: a peer support role or a peer support certification type.</summary>
		public static bool PeerTeamConfigured(WellnessSettings settings) =>
			settings != null && (settings.PeerTeamRoleId > 0 || settings.PeerTeamCertificationTypeId > 0);

		/// <summary>True when the peer support team is the holders of its certification (no peer support role named, a type named).</summary>
		public static bool PeerTeamByCertificationOnly(WellnessSettings settings) =>
			settings != null && settings.PeerTeamRoleId <= 0 && settings.PeerTeamCertificationTypeId > 0;

		/// <summary>True when a role member needs a current certification to act: the switch is on and the role has a linked type.</summary>
		public static bool RequiresCertification(WellnessSettings settings, WellnessRoleKinds kind) =>
			settings != null && settings.RequireCurrentCertification && CertificationTypeIdFor(settings, kind) > 0;

		/// <summary>
		/// Whether a member of the role acts in it, given their certification of the role's linked type. A linked type that no longer
		/// exists is reported as <see cref="WellnessCertificationStates.NotLinked"/> and never takes the role away (the settings page warns).
		/// </summary>
		public static bool Acts(WellnessSettings settings, WellnessRoleKinds kind, WellnessCertificationStatus status)
		{
			if (!RequiresCertification(settings, kind))
				return true;
			if (status == null || status.State == WellnessCertificationStates.NotLinked)
				return true;
			return status.IsCurrent;
		}

		/// <summary>
		/// A member's status for <paramref name="type"/> from their records on <paramref name="onDate"/> (the department-local date).
		/// Valid means active and unexpired (<see cref="CertificationRequirementEvaluator.IsRecordValid"/>); pending verification and
		/// trainee records do not count.
		/// </summary>
		public static WellnessCertificationStatus Status(IEnumerable<PersonnelCertification> records, DepartmentCertificationType type, DateTime onDate)
		{
			if (type == null)
				return new WellnessCertificationStatus { State = WellnessCertificationStates.NotLinked };

			var mine = (records ?? Enumerable.Empty<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.DepartmentCertificationTypeId == type.DepartmentCertificationTypeId)
				.ToList();
			if (mine.Count == 0)
				return new WellnessCertificationStatus { State = WellnessCertificationStates.Missing };

			var valid = mine.Where(r => CertificationRequirementEvaluator.IsRecordValid(r, type, onDate, false, false))
				.OrderByDescending(r => r.ExpiresOn ?? DateTime.MaxValue).FirstOrDefault();
			if (valid == null)
			{
				return new WellnessCertificationStatus
				{
					State = WellnessCertificationStates.Expired,
					ExpiresOn = mine.Where(r => r.ExpiresOn.HasValue).Select(r => r.ExpiresOn).OrderByDescending(d => d).FirstOrDefault()
				};
			}

			var expiring = !type.NeverExpires && valid.ExpiresOn.HasValue && valid.ExpiresOn.Value.Date <= onDate.Date.AddDays(ExpiringSoonDays);
			return new WellnessCertificationStatus
			{
				State = expiring ? WellnessCertificationStates.Expiring : WellnessCertificationStates.Current,
				ExpiresOn = type.NeverExpires ? null : valid.ExpiresOn
			};
		}

		/// <summary>The department terminology key a wellness role is renamed by (setting 118).</summary>
		public static string TermKeyFor(WellnessRoleKinds kind)
		{
			switch (kind)
			{
				case WellnessRoleKinds.WellnessOfficer:
					return DepartmentTerms.WellnessOfficers;
				case WellnessRoleKinds.DesignatedOfficer:
					return DepartmentTerms.DesignatedOfficers;
				default:
					return DepartmentTerms.PeerSupportTeam;
			}
		}
	}

	/// <summary>Who holds one wellness role and whether each of them acts in it (settings page and the officer report).</summary>
	public sealed class WellnessRoleRoster
	{
		public WellnessRoleKinds Kind { get; set; }

		/// <summary>The personnel role (after the designated officer fallback); 0 = none named.</summary>
		public int RoleId { get; set; }

		/// <summary>True when the designated officer role is the wellness officer role because none was named.</summary>
		public bool RoleFallsBack { get; set; }

		/// <summary>The linked certification type id (after the fallback); 0 = none.</summary>
		public int CertificationTypeId { get; set; }

		/// <summary>The linked type's name; null when none is linked or the type no longer exists.</summary>
		public string CertificationTypeName { get; set; }

		/// <summary>True when a type id is linked but the type is gone (deleted, another department's, unit-scoped).</summary>
		public bool CertificationTypeMissing { get; set; }

		/// <summary>True when the switch is on and a type is linked: only members with a current certification act.</summary>
		public bool RequiresCertification { get; set; }

		/// <summary>True for a peer support team made of the holders of its certification (no role named).</summary>
		public bool ByCertificationOnly { get; set; }

		public List<WellnessRoleRosterMember> Members { get; set; } = new List<WellnessRoleRosterMember>();

		public int ActingCount => Members.Count(m => m.Acts);

		/// <summary>True when the role is set up (a role, or for the peer team a certification) but nobody acts in it.</summary>
		public bool NobodyActs => (RoleId > 0 || ByCertificationOnly) && ActingCount == 0;
	}

	public sealed class WellnessRoleRosterMember
	{
		public string UserId { get; set; }

		public WellnessCertificationStatus Certification { get; set; } = new WellnessCertificationStatus();

		public bool Acts { get; set; }
	}
}
