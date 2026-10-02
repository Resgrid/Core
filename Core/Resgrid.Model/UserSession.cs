using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("UserSessions")]
	public class UserSession : IEntity
	{
		[Key]
		[MaxLength(128)]
		public string UserSessionId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		public int? DepartmentId { get; set; }
		public long AuthenticationGeneration { get; set; }
		public int State { get; set; }
		public long StateVersion { get; set; }
		public int ClientApplication { get; set; }

		[MaxLength(128)] public string ClientInstanceIdHash { get; set; }
		[MaxLength(256)] public string DeviceName { get; set; }
		[MaxLength(128)] public string DeviceType { get; set; }
		[MaxLength(128)] public string OperatingSystem { get; set; }
		[MaxLength(128)] public string Browser { get; set; }
		[MaxLength(64)] public string ApplicationVersion { get; set; }
		public int AuthenticationMethod { get; set; }
		[MaxLength(128)] public string DepartmentSsoConfigId { get; set; }
		[MaxLength(128)] public string OpenIddictAuthorizationId { get; set; }
		[MaxLength(512)] public string WebCookieTicketKey { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime LastActiveOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		[MaxLength(64)] public string FirstIpAddress { get; set; }
		[MaxLength(64)] public string LastIpAddress { get; set; }
		[MaxLength(128)] public string LastCountry { get; set; }
		[MaxLength(128)] public string LastRegion { get; set; }
		[MaxLength(128)] public string LastCity { get; set; }
		[MaxLength(1024)] public string UserAgent { get; set; }
		public bool IsLegacyAdopted { get; set; }
		public DateTime? RevokedOn { get; set; }
		[MaxLength(128)] public string RevokedByUserId { get; set; }
		public int? RevocationReason { get; set; }

		/// <summary>
		/// The second factor this session's sign-in verified (<c>MfaEvidenceMethod</c>), kept apart from the first-factor
		/// <see cref="AuthenticationMethod"/> (passkey plan section 5.3). Null when sign-in verified none or predates it.
		/// </summary>
		public int? LoginMfaMethod { get; set; }

		/// <summary>The factor instance that sign-in used, such as <c>passkey:{id}</c>, so removing it can end this session.</summary>
		[MaxLength(256)] public string LoginMfaFactorReference { get; set; }

		/// <summary>
		/// A shared vehicle tablet or workstation session (passkey plan sections 5.5 and 12.5): it locks when idle, ends at the
		/// shift ceiling (<see cref="ExpiresOn"/>), and a locked session reaches only its status, unlock and end-shift endpoints.
		/// Set once at sign-in; nothing turns it off.
		/// </summary>
		public bool SharedMode { get; set; }

		/// <summary>Why the session is shared (<c>SharedModeSource</c>): the installation asked, or the department requires it.</summary>
		public int SharedModeSource { get; set; }

		/// <summary>The idle lock the department's policy set at sign-in. A stricter current policy still applies.</summary>
		public int? SharedIdleLockMinutes { get; set; }

		/// <summary>
		/// Advanced by every lock, never by an unlock. Grants, challenges and approvals are bound to the version they were
		/// issued at, so nothing from before a lock is usable after it.
		/// </summary>
		public long LockVersion { get; set; }

		public bool IsLocked { get; set; }

		/// <summary>When the session last locked. Kept after unlock: evidence verified before it no longer counts.</summary>
		public DateTime? LockedOnUtc { get; set; }

		/// <summary>Why it last locked (<c>SharedSessionLockReason</c>).</summary>
		public int? LockReason { get; set; }

		/// <summary>
		/// The last operator activity the client reported (server time). The idle deadline counts from here; polling and
		/// socket traffic never move it.
		/// </summary>
		public DateTime? LastOperatorActivityOn { get; set; }

		/// <summary>A Responder installation that stopped taking approval requests (plan section 6.5); it no longer approves.</summary>
		public DateTime? ApprovalsDisabledOnUtc { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => UserSessionId;
			set => UserSessionId = value?.ToString();
		}

		[NotMapped] public string TableName => "UserSessions";
		[NotMapped] public string IdName => "UserSessionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
