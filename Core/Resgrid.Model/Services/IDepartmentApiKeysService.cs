using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department API keys: issued by a department admin so another system can call a fixed set of department v4
	/// endpoints (by scope) without a user's credentials. A key expires, can be revoked, can be limited to source
	/// addresses, and is stored only as a keyed hash; the key is returned once, from <see cref="CreateKeyAsync"/>.
	/// </summary>
	public interface IDepartmentApiKeysService
	{
		/// <summary>Keys for the department, newest first, revoked and expired included.</summary>
		Task<List<DepartmentApiKey>> GetKeysForDepartmentAsync(int departmentId);

		Task<DepartmentApiKey> GetKeyForDepartmentAsync(int departmentId, string departmentApiKeyId);

		/// <summary>
		/// Issues a key. Fails (Success = false, Error set) for a blank name, no known scope, an expiry that is not in the
		/// future or is past the maximum lifetime, an unparseable address range, or a department already at its limit of
		/// active keys. The returned <see cref="DepartmentApiKeyCreateResult.Key"/> is the only time the key exists in clear.
		/// </summary>
		Task<DepartmentApiKeyCreateResult> CreateKeyAsync(int departmentId, string name, IEnumerable<string> scopes, DateTime expiresOnUtc,
			string allowedIpRanges, string createdByUserId, string ipAddress = null, CancellationToken cancellationToken = default);

		/// <summary>Revokes a key of the department; it stops working at once. False when the key is not the department's or was already revoked.</summary>
		Task<bool> RevokeKeyAsync(int departmentId, string departmentApiKeyId, string revokedByUserId, string ipAddress = null,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Checks a key presented on a request from <paramref name="remoteIpAddress"/>. Records the use on success
		/// (at most every few minutes per key).
		/// </summary>
		Task<DepartmentApiKeyAuthenticationResult> AuthenticateAsync(string key, string remoteIpAddress, CancellationToken cancellationToken = default);

		/// <summary>Null when every line of <paramref name="allowedIpRanges"/> is an IP address or CIDR range; otherwise the first bad line.</summary>
		string ValidateAllowedIpRanges(string allowedIpRanges);
	}

	public class DepartmentApiKeyCreateResult
	{
		public bool Success { get; set; }

		/// <summary>Why the key was not issued (an English message for the admin).</summary>
		public string Error { get; set; }

		public DepartmentApiKey ApiKey { get; set; }

		/// <summary>The key itself, "rgk_{prefix}_{secret}". Shown once; never stored.</summary>
		public string Key { get; set; }
	}

	public enum DepartmentApiKeyAuthenticationStatus
	{
		Success = 0,
		Invalid = 1,
		Expired = 2,
		Revoked = 3,
		AddressNotAllowed = 4,
		DepartmentUnavailable = 5
	}

	public class DepartmentApiKeyAuthenticationResult
	{
		public DepartmentApiKeyAuthenticationStatus Status { get; set; }

		public DepartmentApiKey ApiKey { get; set; }

		public Department Department { get; set; }

		public List<string> Scopes { get; set; } = new List<string>();

		public bool Success => Status == DepartmentApiKeyAuthenticationStatus.Success;
	}
}
