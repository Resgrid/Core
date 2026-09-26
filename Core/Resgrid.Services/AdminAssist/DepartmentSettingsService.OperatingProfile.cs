using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Services
{
	public partial class DepartmentSettingsService
	{
		public async Task<DepartmentOperatingProfile> GetOperatingProfileAsync(int departmentId)
		{
			var row = await _departmentSettingsRepository.GetDepartmentSettingByIdTypeAsync(departmentId, DepartmentSettingTypes.DepartmentOperatingProfile);
			return row == null ? new DepartmentOperatingProfile() : ObjectSerialization.Deserialize<DepartmentOperatingProfile>(row.Setting)
				?? throw new InvalidOperationException("Operating profile could not be read.");
		}

		public async Task<IReadOnlyList<Document>> GetOperatingProfileDocumentOptionsAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || _operatingProfileRepository == null) return Array.Empty<Document>();
			return await _operatingProfileRepository.GetOperatingProfileDocumentOptionsAsync(departmentId, DateTime.UtcNow, cancellationToken);
		}

		public async Task<DepartmentSetting> SetOperatingProfileAsync(int departmentId, DepartmentOperatingProfile profile, string actingUserId, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(profile);
			Validator.ValidateObject(profile, new ValidationContext(profile), true);
			if (_moduleUnit == null || _operatingProfileRepository == null || _operatingProfileAuthorization == null)
				throw new InvalidOperationException("Operating profile persistence is not configured.");
			async Task RequireAdminAsync()
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (departmentId <= 0 || string.IsNullOrWhiteSpace(actingUserId) ||
					_moduleFlags == null || !await AdminAssist.AdminAssistFeatureAvailability.CanConfigureOperatingProfileAsync(_moduleFlags.Value, departmentId, cancellationToken) ||
					!await _operatingProfileAuthorization.Value.IsActiveMemberAsync(actingUserId, departmentId) ||
					!await _operatingProfileAuthorization.Value.IsDepartmentAdminAsync(actingUserId, departmentId)) throw new UnauthorizedAccessException();
			}
			await RequireAdminAsync();
			// Keep the caller's proposed values/revision intact on a validation or concurrency failure.
			var proposed = ObjectSerialization.Deserialize<DepartmentOperatingProfile>(ObjectSerialization.Serialize(profile));
			var owns = _moduleUnit.Transaction == null;
			await _moduleUnit.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				await _operatingProfileRepository.LockConfigurationAsync(departmentId, cancellationToken);
				var current = await GetOperatingProfileAsync(departmentId);
				if (current.Revision != proposed.Revision) throw new AdminAssistConcurrencyException();
				var now = DateTime.UtcNow;
				if (!await _operatingProfileRepository.ValidateOperatingProfileReferencesAsync(departmentId, proposed, now, cancellationToken))
					throw new ValidationException("Profile.InvalidReferences");
				proposed.Revision = checked(current.Revision + 1);
				proposed.ReviewedOnUtc = now;
				await RequireAdminAsync();
				var saved = await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(proposed), DepartmentSettingTypes.DepartmentOperatingProfile, cancellationToken);
				if (owns) _moduleUnit.CommitChanges();
				return saved;
			}
			catch { if (owns) _moduleUnit.DiscardChanges(); throw; }
		}
	}
}
