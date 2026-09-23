using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;

namespace Resgrid.Services
{
	/// <summary>
	/// The nightly sweep (Workforce &amp; Business Operations plan, Phase D5; worker 34). Runs once per department per
	/// department-local day: expire pass, expiring pass on the configured lead days, the same two passes over unit
	/// records, the enforcement pass (Enforce removes after grace, WarnOnly only notifies, Off evaluates nothing) and
	/// the admin digest. The worker claims the local day (TryClaimSweepDayAsync) before it runs, which is what keeps the
	/// expiring passes idempotent: the expire pass is idempotent on its own, the notifications are not.
	/// </summary>
	public partial class CertificationService
	{
		public Task<bool> TryClaimSweepDayAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default)
			=> _settings.TryClaimSweepAsync(departmentId, localToday.Date, cancellationToken);

		public Task ReleaseSweepDayAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default)
			=> _settings.ReleaseSweepClaimAsync(departmentId, localToday.Date, cancellationToken);

		public async Task<List<int>> GetDepartmentsForSweepAsync()
		{
			var ids = new HashSet<int>();
			foreach (var id in await _personnelCertificationRepository.GetDepartmentIdsWithTypedRecordsAsync() ?? Enumerable.Empty<int>()) ids.Add(id);
			foreach (var id in await _unitRecords.GetDepartmentIdsAsync() ?? Enumerable.Empty<int>()) ids.Add(id);
			foreach (var id in await _requirements.GetDepartmentIdsAsync() ?? Enumerable.Empty<int>()) ids.Add(id);
			return ids.Where(id => id > 0).OrderBy(id => id).ToList();
		}

		public async Task<CertificationSweepResult> RunExpirySweepAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default)
		{
			var today = localToday.Date;
			var result = new CertificationSweepResult { DepartmentId = departmentId };
			var settings = await GetCertificationSettingsAsync(departmentId);
			var leadDays = settings.GetNotifyLeadDays();
			var horizon = today.AddDays(leadDays.Count > 0 ? leadDays.Max() : 60);
			var types = (await GetAllCertificationTypesByDepartmentAsync(departmentId)).Where(t => !t.IsDeleted).ToDictionary(t => t.DepartmentCertificationTypeId);
			// Deleted, disabled and hidden members are out of every pass: their records are neither expired nor announced,
			// they are not notified, enforced against or named in the digest, and no admin among them receives it. The expire
			// pass catches up on its own if a member is re-enabled (any live record past its date is expired on the next run).
			var activeMembers = await ActiveMemberUserIdsAsync(departmentId);
			var nameCache = new Dictionary<string, string>();
			async Task<string> Name(string userId)
			{
				if (!nameCache.TryGetValue(userId, out var name)) nameCache[userId] = name = await DisplayNameAsync(userId);
				return name;
			}

			// ---- Pass 1 + 2: personnel records ---------------------------------------------------------------------
			var records = (await _personnelCertificationRepository.GetExpiringAsync(departmentId, horizon.AddDays(1)))?
				.Where(r => r.IsTyped && types.ContainsKey(r.DepartmentCertificationTypeId.Value) && r.UserId != null && activeMembers.Contains(r.UserId)).ToList() ?? new List<PersonnelCertification>();
			foreach (var record in records)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var type = types[record.DepartmentCertificationTypeId.Value];
				if (type.NeverExpires || !record.ExpiresOn.HasValue) continue;
				var live = record.Status is (int)PersonnelCertificationStatuses.Active or (int)PersonnelCertificationStatuses.Trainee or (int)PersonnelCertificationStatuses.PendingVerification;
				if (!live) continue;
				var days = (int)(record.ExpiresOn.Value.Date - today).TotalDays;

				if (days < 0)
				{
					var before = Snapshot(record);
					record.Status = (int)PersonnelCertificationStatuses.Expired;
					record.StatusChangedOn = DateTime.UtcNow;
					record.StatusChangedByUserId = SystemUserId;
					record.StatusReason = "expired";
					await ProtectRecordBeforeSaveAsync(record, departmentId, cancellationToken);
					await _personnelCertificationRepository.SaveOrUpdateAsync(record, cancellationToken);
					Audit(departmentId, SystemUserId, AuditLogTypes.CertificationStatusChanged, before, Snapshot(record));
					_eventAggregator.SendMessage(new CertificationExpiredEvent { DepartmentId = departmentId, Certification = record, TypeCode = type.Code, TypeName = type.Type });
					result.Expired++;
					result.ExpiredNames.Add($"{await Name(record.UserId)} – {type.Type}");
				}
				else if (leadDays.Contains(days))
				{
					_eventAggregator.SendMessage(new CertificationExpiringEvent { DepartmentId = departmentId, Certification = record, DaysUntilExpiry = days, TypeCode = type.Code, TypeName = type.Type });
					if (settings.NotifyCertificationHolder)
						await NotifyUserAsync(departmentId, record.UserId, $"Your {type.Type} certification expires in {days} day{(days == 1 ? "" : "s")} ({record.ExpiresOn.Value:yyyy-MM-dd}).");
					result.ExpiringNotified++;
					result.ExpiringNames.Add($"{await Name(record.UserId)} – {type.Type} ({days}d)");
				}
			}

			// ---- Pass 2b: unit records ------------------------------------------------------------------------------
			var unitRecords = (await _unitRecords.GetExpiringAsync(departmentId, horizon.AddDays(1)))?.Where(u => types.ContainsKey(u.DepartmentCertificationTypeId)).ToList() ?? new List<UnitCertification>();
			var unitNames = new Dictionary<int, string>();
			foreach (var record in unitRecords)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var type = types[record.DepartmentCertificationTypeId];
				if (type.NeverExpires || !record.ExpiresOn.HasValue || record.Status != (int)UnitCertificationStatuses.Active) continue;
				var days = (int)(record.ExpiresOn.Value.Date - today).TotalDays;
				if (!unitNames.TryGetValue(record.UnitId, out var unitName))
				{
					var unit = await _units.Value.GetUnitByIdAsync(record.UnitId);
					unitNames[record.UnitId] = unitName = unit?.Name ?? $"Unit {record.UnitId}";
				}

				if (days < 0)
				{
					var before = Snapshot(record);
					record.Status = (int)UnitCertificationStatuses.Expired;
					record.StatusChangedOn = DateTime.UtcNow;
					record.StatusChangedByUserId = SystemUserId;
					record.StatusReason = "expired";
					// The meta read carries no bytes; the update must not null the stored file.
					var full = await _unitRecords.GetByIdWithDataAsync(record.UnitCertificationId);
					if (full != null) record.Data = full.Data;
					await SaveProtectedAsync(_unitRecords, record, full, u => u.UnitCertificationId.ToString(), CertificationProtectedFields.Unit, MarkProtected, departmentId, cancellationToken);
					record.Data = null;
					Audit(departmentId, SystemUserId, AuditLogTypes.UnitCertificationStatusChanged, before, Snapshot(record));
					_eventAggregator.SendMessage(new UnitCertificationExpiredEvent { DepartmentId = departmentId, Certification = record, UnitName = unitName, TypeCode = type.Code, TypeName = type.Type });
					result.UnitsExpired++;
					result.ExpiredNames.Add($"{unitName} – {type.Type}");
				}
				else if (leadDays.Contains(days))
				{
					_eventAggregator.SendMessage(new UnitCertificationExpiringEvent { DepartmentId = departmentId, Certification = record, UnitName = unitName, TypeCode = type.Code, TypeName = type.Type, DaysUntilExpiry = days });
					result.UnitsExpiringNotified++;
					result.ExpiringNames.Add($"{unitName} – {type.Type} ({days}d)");
				}
			}

			// ---- Pass 3: enforcement -------------------------------------------------------------------------------
			var mode = (CertificationEnforcementModes)settings.EnforcementMode;
			if (mode != CertificationEnforcementModes.Off)
			{
				var requirements = await GetAllRoleRequirementsAsync(departmentId);
				foreach (var roleId in requirements.Where(r => r.IsMandatory).Select(r => r.PersonnelRoleId).Distinct())
				{
					cancellationToken.ThrowIfCancellationRequested();
					var role = await _roles.Value.GetRoleByIdAsync(roleId);
					if (role == null || role.DepartmentId != departmentId) continue;
					var members = (await _roles.Value.GetAllMembersOfRoleAsync(roleId))?.Where(m => m?.UserId != null && activeMembers.Contains(m.UserId)).ToList() ?? new List<PersonnelRoleUser>();
					if (members.Count == 0) continue;
					var roleRequirements = requirements.Where(r => r.PersonnelRoleId == roleId).ToList();
					var memberIds = members.Select(m => m.UserId).Distinct().ToList();
					var memberRecords = (await GetCertificationsForDepartmentAsync(departmentId, memberIds)).GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => (IReadOnlyList<PersonnelCertification>)g.ToList());

					foreach (var member in members.GroupBy(m => m.UserId).Select(g => g.First()))
					{
						var evaluation = CertificationRequirementEvaluator.Evaluate(roleId, member.UserId, roleRequirements, memberRecords.TryGetValue(member.UserId, out var mine) ? mine : Array.Empty<PersonnelCertification>(), types, settings, today);
						if (evaluation.Qualified) continue;
						var failing = evaluation.Violations.Where(v => v.IsMandatory).OrderBy(v => v.ViolationStartedOn ?? DateTime.MaxValue).First();
						var name = await Name(member.UserId);

						if (mode == CertificationEnforcementModes.Enforce && CertificationRequirementEvaluator.IsPastGrace(evaluation.RemovalDueOn, today))
						{
							var membershipRows = members.Where(m => m.UserId == member.UserId).ToList();
							await _roles.Value.DeleteRoleUsersAsync(membershipRows, cancellationToken);
							Audit(departmentId, SystemUserId, AuditLogTypes.RoleMemberRemovedByCertification,
								JsonConvert.SerializeObject(new { membership = membershipRows.Select(m => new { m.PersonnelRoleUserId, m.PersonnelRoleId, m.UserId }), requirement = failing }),
								JsonConvert.SerializeObject(new { roleId, role.Name, member.UserId, removedOn = today, evaluation.RemovalDueOn }));
							_eventAggregator.SendMessage(new CertificationRoleRemovedEvent
							{
								DepartmentId = departmentId, UserId = member.UserId, PersonnelRoleId = roleId, RoleName = role.Name, DepartmentCertificationTypeId = failing.DepartmentCertificationTypeId,
								TypeCode = failing.TypeCode, TypeName = failing.TypeName, ExpiresOn = failing.ViolationStartedOn, GraceDeadline = evaluation.RemovalDueOn
							});
							if (settings.NotifyCertificationHolder)
								await NotifyUserAsync(departmentId, member.UserId, $"You were removed from the {role.Name} role: the required {failing.TypeName} certification is no longer valid.");
							result.Removed++;
							result.RemovedNames.Add($"{name} – {role.Name} ({failing.TypeName})");
						}
						else
						{
							result.InGrace++;
							result.InGraceNames.Add($"{name} – {role.Name} ({failing.TypeName}{(evaluation.RemovalDueOn.HasValue ? $", until {evaluation.RemovalDueOn:yyyy-MM-dd}" : string.Empty)})");
							// "Removal pending" once per lead-day schedule, and only when the department will actually remove.
							if (mode == CertificationEnforcementModes.Enforce && settings.NotifyCertificationHolder && evaluation.RemovalDueOn.HasValue)
							{
								var daysLeft = (int)(evaluation.RemovalDueOn.Value.Date - today).TotalDays + 1;
								if (leadDays.Contains(daysLeft))
									await NotifyUserAsync(departmentId, member.UserId, $"Your {role.Name} role membership ends on {evaluation.RemovalDueOn.Value.AddDays(1):yyyy-MM-dd} unless your {failing.TypeName} certification is renewed.");
							}
						}
					}
				}
			}

			// ---- Pass 4: admin digest ------------------------------------------------------------------------------
			if (settings.SendAdminDigest && (result.Expired + result.ExpiringNotified + result.UnitsExpired + result.UnitsExpiringNotified + result.InGrace + result.Removed) > 0)
			{
				try
				{
					var admins = await _departments.Value.GetActiveAdminsForDepartmentAsync(departmentId);
					var lines = new List<string> { $"Certification summary for {today:yyyy-MM-dd}: {result.Expired + result.UnitsExpired} expired, {result.ExpiringNotified + result.UnitsExpiringNotified} expiring, {result.InGrace} in grace, {result.Removed} removed from roles." };
					if (result.ExpiredNames.Count > 0) lines.Add("Expired: " + string.Join("; ", result.ExpiredNames.Take(25)));
					if (result.ExpiringNames.Count > 0) lines.Add("Expiring: " + string.Join("; ", result.ExpiringNames.Take(25)));
					if (result.InGraceNames.Count > 0) lines.Add("In grace: " + string.Join("; ", result.InGraceNames.Take(25)));
					if (result.RemovedNames.Count > 0) lines.Add("Removed: " + string.Join("; ", result.RemovedNames.Take(25)));
					var message = string.Join(" ", lines);
					foreach (var admin in admins ?? new List<Model.Identity.IdentityUser>())
						await NotifyUserAsync(departmentId, admin.UserId, message, "Certification digest");
					result.DigestSent = admins != null && admins.Count > 0;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Certification digest for department {departmentId} could not be sent.");
				}
			}

			return result;
		}

		/// <summary>Direct holder / admin notification through the member's own delivery preferences. Never throws.</summary>
		private async Task NotifyUserAsync(int departmentId, string userId, string message, string title = "Certification")
		{
			if (string.IsNullOrWhiteSpace(userId) || _communication == null) return;
			try
			{
				var department = await _departments.Value.GetDepartmentByIdAsync(departmentId, false);
				var number = await _departmentSettings.Value.GetTextToCallNumberForDepartmentAsync(departmentId);
				await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Certification notification to {userId} in department {departmentId} failed.");
			}
		}
	}
}
