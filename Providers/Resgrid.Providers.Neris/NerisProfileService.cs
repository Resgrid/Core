using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// Department NERIS profile, credential (encrypted per department, decrypted only for a call), the pinned
	/// value sets, and the department crosswalks. Seeds RmsNerisValueSets from the embedded snapshot on first use
	/// so reports and admin screens can query them; the snapshot stays the source of truth for the pinned version.
	/// </summary>
	public class NerisProfileService : INerisProfileService
	{
		private static readonly SemaphoreSlim SeedLock = new SemaphoreSlim(1, 1);
		private static volatile bool _seeded;

		private readonly IRmsNerisProfilesRepository _profiles;
		private readonly IRmsNerisValueSetsRepository _valueSets;
		private readonly IRmsNerisCrosswalksRepository _crosswalks;
		private readonly IDepartmentsService _departments;
		private readonly IEncryptionService _encryption;

		public NerisProfileService(IRmsNerisProfilesRepository profiles, IRmsNerisValueSetsRepository valueSets, IRmsNerisCrosswalksRepository crosswalks,
			IDepartmentsService departments, IEncryptionService encryption)
		{
			_profiles = profiles;
			_valueSets = valueSets;
			_crosswalks = crosswalks;
			_departments = departments;
			_encryption = encryption;
		}

		public string ContractVersion => NerisValueSetCatalog.Instance.ContractVersion;

		public string GetDestinationIdentity(RmsNerisProfile profile) => Newtonsoft.Json.JsonConvert.SerializeObject(new
		{
			profile.DepartmentId, profile.RmsNerisProfileId, profile.NerisEntityId, profile.Environment,
			Endpoint = NerisApiClient.BaseUrlFor(profile), Contract = profile.ContractVersion ?? ContractVersion
		});

		public IReadOnlyList<string> ValueSetKeys => NerisValueSetCatalog.Instance.SetKeys;

		public Task<RmsNerisProfile> GetProfileAsync(int departmentId)
		{
			return _profiles.GetByDepartmentIdAsync(departmentId);
		}

		public async Task<RmsNerisProfile> SaveProfileAsync(RmsNerisProfile profile, NerisCredential credential, string userId, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			if (profile.DepartmentId <= 0) throw new ArgumentException("A department is required.", nameof(profile));

			var now = DateTime.UtcNow;
			var existing = await _profiles.GetByDepartmentIdAsync(profile.DepartmentId);
			var target = existing ?? new RmsNerisProfile
			{
				RmsNerisProfileId = Guid.NewGuid().ToString(),
				DepartmentId = profile.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				CreatedOn = now,
				RowVersion = 0
			};

			target.NerisEntityId = string.IsNullOrWhiteSpace(profile.NerisEntityId) ? null : profile.NerisEntityId.Trim().ToUpperInvariant();
			target.EntityName = profile.EntityName?.Trim();
			// The settings screen posts these as free strings, so canonicalize case-insensitively: "Sandbox" or a
			// padded " sandbox " must not silently persist as Production and file a live incident against it.
			target.Environment = string.Equals(profile.Environment?.Trim(), NerisEnvironments.Sandbox, StringComparison.OrdinalIgnoreCase)
				? NerisEnvironments.Sandbox
				: NerisEnvironments.Production;
			target.BaseUrlOverride = NormalizeBaseUrl(profile.BaseUrlOverride);
			target.GrantType = string.Equals(profile.GrantType?.Trim(), NerisGrantTypes.Password, StringComparison.OrdinalIgnoreCase)
				? NerisGrantTypes.Password
				: NerisGrantTypes.ClientCredentials;
			target.ContractVersion = ContractVersion;
			target.AutoSubmitOnFinalize = profile.AutoSubmitOnFinalize;
			target.IsEnabled = profile.IsEnabled;
			target.UpdatedByUserId = userId;
			target.ModifiedOn = now;
			target.RowVersion += 1;

			if (credential != null)
			{
				var department = await _departments.GetDepartmentByIdAsync(profile.DepartmentId, false);
				var json = JsonConvert.SerializeObject(credential);
				target.EncryptedCredentialJson = _encryption.EncryptForDepartment(json, profile.DepartmentId, department?.Code);
			}

			if (existing == null)
				return await _profiles.InsertAsync(target, cancellationToken, true);

			return await _profiles.UpdateAsync(target, cancellationToken, true);
		}

		/// <summary>
		/// The base URL is a server-side request destination, so an unchecked override is an SSRF sink: a department
		/// administrator could point the submission worker at an internal host. Self-hosted and sandbox deployments
		/// legitimately need their own host, so the rule is absolute HTTPS with no embedded credentials rather than a
		/// fixed allow-list.
		/// </summary>
		private static string NormalizeBaseUrl(string value)
		{
			var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
			if (trimmed == null)
				return null;

			if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
				|| !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
				|| !string.IsNullOrEmpty(uri.UserInfo))
				throw new ArgumentException("The NERIS base URL override must be an absolute https:// address without embedded credentials.", nameof(value));

			return trimmed;
		}

		public async Task<NerisCredential> GetCredentialAsync(RmsNerisProfile profile)
		{
			if (profile == null || !profile.HasCredential)
				return null;

			var department = await _departments.GetDepartmentByIdAsync(profile.DepartmentId, false);
			var json = _encryption.DecryptForDepartment(profile.EncryptedCredentialJson, profile.DepartmentId, department?.Code);
			return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<NerisCredential>(json);
		}

		public async Task<bool> IsSubmissionEnabledAsync(int departmentId)
		{
			if (!NerisConfig.Enabled)
				return false;

			var profile = await _profiles.GetByDepartmentIdAsync(departmentId);
			return profile != null && profile.IsEnabled && !string.IsNullOrWhiteSpace(profile.NerisEntityId) && profile.HasCredential;
		}

		public NerisValueSet GetValueSet(string setKey)
		{
			return NerisValueSetCatalog.Instance.Get(setKey);
		}

		public async Task EnsureValueSetsSeededAsync(CancellationToken cancellationToken = default)
		{
			if (_seeded)
				return;

			await SeedLock.WaitAsync(cancellationToken);
			try
			{
				if (_seeded)
					return;

				var catalog = NerisValueSetCatalog.Instance;
				var expected = catalog.SetKeys.Sum(k => catalog.Get(k).Codes.Count);
				var present = await _valueSets.CountForVersionAsync(catalog.ContractVersion);
				if (present >= expected)
				{
					_seeded = true;
					return;
				}

				var now = DateTime.UtcNow;
				foreach (var key in catalog.SetKeys)
				{
					var set = catalog.Get(key);
					for (var i = 0; i < set.Codes.Count; i++)
					{
						var code = set.Codes[i];
						if (present > 0 && await _valueSets.ExistsAsync(catalog.ContractVersion, key, code))
							continue;

						await _valueSets.InsertAsync(new RmsNerisValueSetEntry
						{
							ContractVersion = catalog.ContractVersion,
							SetKey = key,
							Code = code,
							Label = Label(code),
							ParentCode = Parent(code),
							SortOrder = i,
							IsRetired = false,
							CreatedOn = now
						}, cancellationToken, true);
					}
				}

				_seeded = true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "NERIS value-set seeding failed; the embedded snapshot still serves mapping and validation.");
			}
			finally
			{
				SeedLock.Release();
			}
		}

		public async Task<string> ResolveCrosswalkAsync(int departmentId, string setKey, string localSource, string localCode)
		{
			if (string.IsNullOrWhiteSpace(localCode))
				return null;

			var row = await _crosswalks.GetAsync(departmentId, ContractVersion, setKey, localSource, localCode.Trim());
			return row?.NerisCode;
		}

		public async Task<List<RmsNerisCrosswalk>> GetCrosswalksAsync(int departmentId)
		{
			return (await _crosswalks.GetForDepartmentAsync(departmentId, ContractVersion) ?? Enumerable.Empty<RmsNerisCrosswalk>()).ToList();
		}

		public async Task<bool> RemoveCrosswalkAsync(int departmentId, string setKey, string localSource, string localCode, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(localCode))
				return false;

			var existing = await _crosswalks.GetAsync(departmentId, ContractVersion, setKey, localSource, localCode.Trim());
			if (existing == null)
				return false;

			return await _crosswalks.DeleteAsync(existing, cancellationToken);
		}

		public async Task<RmsNerisCrosswalk> SaveCrosswalkAsync(int departmentId, string userId, string setKey, string localSource, string localCode, string nerisCode, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(localCode)) throw new ArgumentException("A local code is required.", nameof(localCode));
			if (!NerisValueSetCatalog.Instance.Contains(setKey, nerisCode))
				throw new ArgumentException($"'{nerisCode}' is not a value of the NERIS {setKey} set at contract {ContractVersion}.", nameof(nerisCode));

			var now = DateTime.UtcNow;
			var existing = await _crosswalks.GetAsync(departmentId, ContractVersion, setKey, localSource, localCode.Trim());
			if (existing != null)
			{
				existing.NerisCode = nerisCode;
				existing.ModifiedOn = now;
				existing.RowVersion += 1;
				return await _crosswalks.UpdateAsync(existing, cancellationToken, true);
			}

			return await _crosswalks.InsertAsync(new RmsNerisCrosswalk
			{
				RmsNerisCrosswalkId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				ContractVersion = ContractVersion,
				SetKey = setKey,
				LocalSource = localSource,
				LocalCode = localCode.Trim(),
				NerisCode = nerisCode,
				IsDefault = false,
				CreatedByUserId = userId,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			}, cancellationToken, true);
		}

		/// <summary>"FIRE||STRUCTURE_FIRE||ROOM_AND_CONTENTS" reads as "Room And Contents"; the code stays the identity.</summary>
		public static string Label(string code)
		{
			if (string.IsNullOrEmpty(code))
				return code;

			var leaf = code.Split(new[] { "||" }, StringSplitOptions.None).Last();
			var words = leaf.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length <= 3 && w.All(char.IsUpper) && w.Length > 1 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant());
			return string.Join(" ", words);
		}

		public static string Parent(string code)
		{
			if (string.IsNullOrEmpty(code))
				return null;

			var index = code.LastIndexOf("||", StringComparison.Ordinal);
			return index > 0 ? code.Substring(0, index) : null;
		}
	}
}
