using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	public partial class ChecklistsService
	{
		public async Task<ChecklistReminderSettingsInput> ReminderSettingsAsync(ChecklistActor actor)
		{
			await RequireWriteAsync(actor, true);
			var row = (await _store.ListAsync<DepartmentChecklistSettings>(actor.DepartmentId, take: 1)).SingleOrDefault();
			return row == null ? new ChecklistReminderSettingsInput() : new ChecklistReminderSettingsInput { Revision = row.Revision, Enabled = row.RemindersEnabled, NotifyAtShiftStart = row.NotifyAtShiftStart, FixedDigestTime = row.FixedDigestMinute.HasValue ? System.TimeOnly.MinValue.AddMinutes(row.FixedDigestMinute.Value).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null,
				NotifyBeforeMinutes = row.NotifyBeforeMinutes, NotifyMissed = row.NotifyMissed, DigestMode = row.DigestMode, EscalateAfterMinutes = row.EscalateAfterMinutes };
		}
		public async Task SaveReminderSettingsAsync(ChecklistActor actor, ChecklistReminderSettingsInput input)
		{
			await RequireWriteAsync(actor, true);
			if (input == null || input.NotifyBeforeMinutes < 0 || input.NotifyBeforeMinutes > 10080 || input.EscalateAfterMinutes < 1 || input.EscalateAfterMinutes > 43200)
				throw new ChecklistException(400, "ReminderValidation");
			int? fixedMinute = null;
			if (!string.IsNullOrWhiteSpace(input.FixedDigestTime))
			{
				if (!System.TimeOnly.TryParseExact(input.FixedDigestTime, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time))
					throw new ChecklistException(400, "ReminderTimeValidation");
				fixedMinute = time.Hour * 60 + time.Minute;
			}
			await TransactionAsync(actor, async events =>
			{
				var row = (await _store.ListAsync<DepartmentChecklistSettings>(actor.DepartmentId, take: 1)).SingleOrDefault();
				var insert = row == null;
				if (!insert) { Revision(row, input.Revision); row.Revision++; }
				else { if (input.Revision != 0) throw new ChecklistException(409, "This item changed. Reload before saving."); row = New<DepartmentChecklistSettings>(actor); }
				if (input.Enabled && !row.RemindersEnabled) row.RemindersActiveFromUtc = _clock.GetUtcNow().UtcDateTime;
				if (insert || row.RemindersEnabled != input.Enabled || row.NotifyAtShiftStart != input.NotifyAtShiftStart || row.FixedDigestMinute != fixedMinute) row.DigestActiveFromUtc = _clock.GetUtcNow().UtcDateTime;
				row.RemindersEnabled = input.Enabled; row.NotifyBeforeMinutes = input.NotifyBeforeMinutes; row.NotifyMissed = input.NotifyMissed;
				row.DigestMode = input.DigestMode; row.EscalateAfterMinutes = input.EscalateAfterMinutes;
				row.NotifyAtShiftStart = input.NotifyAtShiftStart;
				row.FixedDigestMinute = fixedMinute;
				row.LastDigestSweepUtc = _clock.GetUtcNow().UtcDateTime;
				// Content is left intact; only non-sensitive typed notification controls are edited.
				await PersistAsync(actor, row, insert); await AuditAsync(actor, row, AuditLogTypes.ChecklistReminderSettingsUpdated);
				return true;
			});
		}
	}
}
