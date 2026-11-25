using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Certification cycle auto-renew and alerts (§13a.1, Vector credential parity; LMS add-on). Each personnel certification record is a
	/// cycle (T1). A type's rule can start the next cycle by itself once the cycle's renewal hours are met (on completion, or on the
	/// expiry date), and can alert the holder's supervisors, the department admins and named members when a cycle completes, starts,
	/// nears expiry or expires. Worker 94 runs it once per department-local day for departments that have rules, asking the LMS gate once
	/// per department (R-L9); every renewal and alert is claimed before it happens, so a repeated run never doubles one. Inactive holders
	/// and inactive recipients are skipped (inactive-member-automation-policy).
	/// </summary>
	public class LmsCertificationCycleService : ILmsCertificationCycleService
	{
		/// <summary>How far back a started or expired cycle still announces itself (a worker outage of a few days is caught up).</summary>
		public const int CatchUpDays = 7;

		private readonly ILmsCertificationCycleRulesRepository _rules;
		private readonly ILmsCertificationAlertsRepository _alerts;
		private readonly ILmsProgramStatesRepository _states;
		private readonly ICertificationService _certifications;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly ILmsAccessService _access;
		private readonly ILmsProgramNotifier _notifier;
		private readonly IEventAggregator _events;

		public LmsCertificationCycleService(ILmsCertificationCycleRulesRepository rules, ILmsCertificationAlertsRepository alerts, ILmsProgramStatesRepository states,
			ICertificationService certifications, IDepartmentsService departments, IDepartmentGroupsService groups, ILmsAccessService access, ILmsProgramNotifier notifier,
			IEventAggregator events)
		{
			_rules = rules;
			_alerts = alerts;
			_states = states;
			_certifications = certifications;
			_departments = departments;
			_groups = groups;
			_access = access;
			_notifier = notifier;
			_events = events;
		}

		#region Rules

		public async Task<List<LmsCertificationCycleRule>> GetRulesAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsCertificationCycleRule>() : await _rules.GetForDepartmentAsync(departmentId) ?? new List<LmsCertificationCycleRule>();

		public async Task<LmsCertificationCycleRule> SaveRuleAsync(LmsCertificationCycleRule rule, string userId, CancellationToken cancellationToken = default)
		{
			if (rule == null || rule.DepartmentId <= 0)
				throw new InvalidOperationException("lms_program_rule_not_found");
			var type = await _certifications.GetCertificationTypeByIdAsync(rule.DepartmentCertificationTypeId);
			if (type == null || type.DepartmentId != rule.DepartmentId || type.IsDeleted || type.AppliesTo != (int)CertificationAppliesTo.Person)
				throw new InvalidOperationException("lms_program_type_invalid");
			if (!Enum.IsDefined(typeof(LmsAutoRenewModes), rule.AutoRenewMode))
				throw new InvalidOperationException("lms_program_rule_invalid");
			if (rule.AutoRenewMode != (int)LmsAutoRenewModes.Off && !LmsCertificationCycleMath.CanAutoRenew(type))
				throw new InvalidOperationException("lms_program_auto_renew_needs_hours");

			var named = rule.NamedRecipients();
			if (named.Count > 25)
				throw new InvalidOperationException("lms_program_too_many_recipients");
			var active = await _departments.GetActiveMemberUserIdsAsync(rule.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (named.Any(id => !active.Contains(id)))
				throw new InvalidOperationException("lms_program_recipient_invalid");

			var all = await GetRulesAsync(rule.DepartmentId);
			var stored = all.FirstOrDefault(r => r.LmsCertificationCycleRuleId == rule.LmsCertificationCycleRuleId && rule.LmsCertificationCycleRuleId > 0)
				?? all.FirstOrDefault(r => r.DepartmentCertificationTypeId == rule.DepartmentCertificationTypeId);
			var now = DateTime.UtcNow;
			var before = stored == null ? null : JsonConvert.SerializeObject(stored);
			var hadAlerts = stored?.HasAlerts == true;
			stored ??= new LmsCertificationCycleRule { DepartmentId = rule.DepartmentId, CreatedOn = now, CreatedByUserId = userId };
			stored.DepartmentCertificationTypeId = type.DepartmentCertificationTypeId;
			stored.AutoRenewMode = rule.AutoRenewMode;
			stored.AlertOnCompletion = rule.AlertOnCompletion;
			stored.AlertOnRenewal = rule.AlertOnRenewal;
			stored.AlertOnExpired = rule.AlertOnExpired;
			stored.AlertLeadDaysCsv = string.Join(",", rule.LeadDays());
			stored.NotifySupervisors = rule.NotifySupervisors;
			stored.NotifyAdmins = rule.NotifyAdmins;
			stored.NamedRecipientUserIds = string.Join(",", named);
			// Turning alerts on starts the renewal history from now: cycles that began earlier never announce themselves.
			if (!hadAlerts && stored.HasAlerts)
				stored.AlertsFrom = now;
			else if (stored.AlertsFrom == default)
				stored.AlertsFrom = now;
			if (before != null)
			{
				stored.UpdatedOn = now;
				stored.UpdatedByUserId = userId;
			}

			var saved = await _rules.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(rule.DepartmentId, userId, AuditLogTypes.LmsCertificationCycleRuleChanged, before, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeleteRuleAsync(int departmentId, int lmsCertificationCycleRuleId, string userId, CancellationToken cancellationToken = default)
		{
			var rule = (await GetRulesAsync(departmentId)).FirstOrDefault(r => r.LmsCertificationCycleRuleId == lmsCertificationCycleRuleId);
			if (rule == null)
				return false;
			var deleted = await _rules.DeleteAsync(rule, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.LmsCertificationCycleRuleChanged, JsonConvert.SerializeObject(rule), null);
			return deleted;
		}

		#endregion Rules

		#region Sweep

		public async Task<LmsCertificationCycleRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsCertificationCycleRunResult();
			foreach (var departmentId in await _rules.GetDepartmentIdsAsync() ?? new List<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				DateTime localToday = default;
				var claimed = false;
				try
				{
					var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
					if (department == null)
						continue;
					localToday = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).TimeConverter(department).Date;
					if (!await _states.TryClaimCycleSweepAsync(departmentId, localToday, cancellationToken))
						continue;
					claimed = true;

					// The gate once per department with due work, never per record (R-L9). No add-on: nothing renews and nothing alerts.
					if (!await _access.CanAuthorAndManageAsync(departmentId))
					{
						result.Skipped++;
						continue;
					}

					var one = await RunDepartmentAsync(departmentId, localToday, cancellationToken);
					result.Departments++;
					result.Renewed += one.Renewed;
					result.Alerts += one.Alerts;
					result.Failures += one.Failures;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Failures++;
					Logging.LogException(ex, $"LMS certification cycle sweep failed for department {departmentId}.");
					if (claimed)
						await _states.ReleaseCycleSweepAsync(departmentId, localToday, cancellationToken);
				}
			}

			return result;
		}

		public async Task<LmsCertificationCycleRunResult> RunDepartmentAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default)
		{
			var result = new LmsCertificationCycleRunResult { Departments = 1 };
			var today = localToday.Date;
			var rules = (await GetRulesAsync(departmentId)).GroupBy(r => r.DepartmentCertificationTypeId).ToDictionary(g => g.Key, g => g.First());
			if (rules.Count == 0)
				return result;

			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => !t.IsDeleted && rules.ContainsKey(t.DepartmentCertificationTypeId)).ToDictionary(t => t.DepartmentCertificationTypeId);
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var records = (await _certifications.GetCertificationsForDepartmentAsync(departmentId) ?? new List<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.DepartmentCertificationTypeId.HasValue && types.ContainsKey(r.DepartmentCertificationTypeId.Value) &&
				            r.UserId != null && active.Contains(r.UserId))
				.ToList();
			if (records.Count == 0)
				return result;

			var withHours = records.Where(r => types[r.DepartmentCertificationTypeId.Value].RenewalCreditHoursRequired > 0).Select(r => r.PersonnelCertificationId).ToList();
			var credited = withHours.Count == 0 ? new Dictionary<int, decimal>() : new Dictionary<int, decimal>(await _certifications.GetCertificationCreditTotalsAsync(withHours) ?? new Dictionary<int, decimal>());
			var byMember = records.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

			foreach (var record in records.OrderBy(r => r.PersonnelCertificationId).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var type = types[record.DepartmentCertificationTypeId.Value];
					var rule = rules[type.DepartmentCertificationTypeId];
					var mine = byMember[record.UserId];
					var hours = credited.TryGetValue(record.PersonnelCertificationId, out var h) ? h : 0m;

					// ---- Auto-renew: the next cycle, created once per cycle -------------------------------------------------
					if (LmsCertificationCycleMath.ShouldAutoRenew(rule, type, record, hours, mine, today) &&
					    await _alerts.TryClaimAsync(Claim(departmentId, record, LmsCertificationAlertKinds.AutoRenewClaim, "next"), cancellationToken))
					{
						var (start, end) = LmsCertificationCycleMath.NextCycle(record, type);
						var next = await _certifications.SaveCertificationAsync(new PersonnelCertification
						{
							DepartmentId = departmentId,
							UserId = record.UserId,
							// Only the type's own (plaintext) names are copied; protected fields of the old cycle are never read or copied.
							Name = type.Type,
							Type = type.Type,
							DepartmentCertificationTypeId = type.DepartmentCertificationTypeId,
							RecievedOn = start,
							ExpiresOn = end,
							Status = (int)PersonnelCertificationStatuses.Active
						}, cancellationToken);
						if (next != null)
						{
							mine.Add(next);
							result.Renewed++;
							Audit(departmentId, null, AuditLogTypes.LmsCertificationCycleAutoRenewed,
								JsonConvert.SerializeObject(new { record.PersonnelCertificationId, record.RecievedOn, record.ExpiresOn, CreditedHours = hours }),
								JsonConvert.SerializeObject(new { next.PersonnelCertificationId, next.RecievedOn, next.ExpiresOn, next.Status, Mode = (LmsAutoRenewModes)rule.AutoRenewMode }));
						}
					}

					// ---- Alerts ----------------------------------------------------------------------------------------------
					if (!rule.HasAlerts)
						continue;
					var name = names.TryGetValue(record.UserId, out var n) ? n : record.UserId;
					var superseded = LmsCertificationCycleMath.HasLaterCycle(record, mine);
					var live = LmsCertificationCycleMath.IsLive(record);

					if (rule.AlertOnCompletion && live && LmsCertificationCycleMath.HoursMet(type, hours))
						result.Alerts += await AlertAsync(rule, record, LmsCertificationAlertKinds.Completed, "cycle", "Prg.Notify.CertCompleted", name, type, record.ExpiresOn, cancellationToken);

					if (rule.AlertOnRenewal && live && record.RecievedOn.HasValue && record.RecievedOn.Value.Date <= today &&
					    record.RecievedOn.Value.Date >= today.AddDays(-CatchUpDays) && record.RecievedOn.Value >= rule.AlertsFrom.Date)
						result.Alerts += await AlertAsync(rule, record, LmsCertificationAlertKinds.Renewed, Key(record.RecievedOn.Value), "Prg.Notify.CertRenewed", name, type, record.ExpiresOn, cancellationToken);

					if (!record.ExpiresOn.HasValue || type.NeverExpires || superseded)
						continue;
					var days = (int)(record.ExpiresOn.Value.Date - today).TotalDays;
					if (live && rule.LeadDays().Contains(days))
						result.Alerts += await AlertAsync(rule, record, LmsCertificationAlertKinds.Expiring, Key(record.ExpiresOn.Value) + ":" + days.ToString(CultureInfo.InvariantCulture),
							"Prg.Notify.CertExpiring", name, type, record.ExpiresOn, cancellationToken);

					if (rule.AlertOnExpired && days < 0 && days >= -CatchUpDays && (live || record.Status == (int)PersonnelCertificationStatuses.Expired))
						result.Alerts += await AlertAsync(rule, record, LmsCertificationAlertKinds.Expired, Key(record.ExpiresOn.Value), "Prg.Notify.CertExpired", name, type, record.ExpiresOn, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Failures++;
					Logging.LogException(ex, $"LMS certification cycle step failed for record {record.PersonnelCertificationId} in department {departmentId}.");
				}
			}

			return result;
		}

		private static string Key(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

		private static LmsCertificationAlert Claim(int departmentId, PersonnelCertification record, LmsCertificationAlertKinds kind, string key) => new LmsCertificationAlert
		{
			DepartmentId = departmentId,
			PersonnelCertificationId = record.PersonnelCertificationId,
			Kind = (int)kind,
			AlertKey = key,
			SentOn = DateTime.UtcNow
		};

		/// <summary>Claims the alert, then sends it to the rule's recipients (never the holder themselves); the members notified.</summary>
		private async Task<int> AlertAsync(LmsCertificationCycleRule rule, PersonnelCertification record, LmsCertificationAlertKinds kind, string key, string messageKey,
			string memberName, DepartmentCertificationType type, DateTime? expiresOn, CancellationToken cancellationToken)
		{
			var recipients = await RecipientsAsync(rule, record.UserId);
			if (recipients.Count == 0)
				return 0;
			var claim = Claim(rule.DepartmentId, record, kind, key);
			if (!await _alerts.TryClaimAsync(claim, cancellationToken))
				return 0;

			var sent = await _notifier.SendAsync(rule.DepartmentId, recipients, "Prg.Notify.CertTitle", messageKey,
				culture => new object[] { memberName, type.Type, expiresOn?.ToString("d", culture) ?? string.Empty }, "CERT:" + record.PersonnelCertificationId, cancellationToken);
			await _alerts.SetRecipientsAsync(claim.LmsCertificationAlertId, sent, cancellationToken);
			return sent > 0 ? 1 : 0;
		}

		/// <summary>Supervisors (the admins of the holder's group and its parents), the department admins and named members; never the holder.</summary>
		private async Task<List<string>> RecipientsAsync(LmsCertificationCycleRule rule, string holderUserId)
		{
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (rule.NotifySupervisors)
			{
				var group = await _groups.GetGroupForUserAsync(holderUserId, rule.DepartmentId);
				if (group != null && group.DepartmentId == rule.DepartmentId)
					foreach (var admin in await _groups.GetAllAdminsForGroupAndAncestorsAsync(group.DepartmentGroupId) ?? new List<DepartmentGroupMember>())
						if (!string.IsNullOrWhiteSpace(admin?.UserId))
							ids.Add(admin.UserId);
			}
			if (rule.NotifyAdmins)
				foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(rule.DepartmentId) ?? new List<Model.Identity.IdentityUser>())
					if (!string.IsNullOrWhiteSpace(admin?.Id))
						ids.Add(admin.Id);
			foreach (var named in rule.NamedRecipients())
				ids.Add(named);

			ids.RemoveWhere(id => string.Equals(id, holderUserId, StringComparison.OrdinalIgnoreCase));
			return ids.ToList();
		}

		#endregion Sweep

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent { DepartmentId = departmentId, UserId = userId, Type = type, Before = before, After = after, Successful = true });
		}
	}
}
