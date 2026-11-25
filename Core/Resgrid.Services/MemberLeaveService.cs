using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Member leave of absence (R-C2). A date range in the department's days: it pauses training due dates and reminders
	/// while it runs and shifts them by its length afterwards (TrainingDueDates.ApplyLeave). Neutral, so T3 requirements
	/// reuse it.
	/// </summary>
	public class MemberLeaveService : IMemberLeaveService
	{
		/// <summary>The longest leave one period may cover.</summary>
		public const int MaxLeaveDays = 730;

		private readonly IMemberLeavePeriodsRepository _leaveRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;

		public MemberLeaveService(IMemberLeavePeriodsRepository leaveRepository, IDepartmentsService departmentsService, IEventAggregator eventAggregator)
		{
			_leaveRepository = leaveRepository;
			_departmentsService = departmentsService;
			_eventAggregator = eventAggregator;
		}

		public async Task<List<MemberLeavePeriod>> GetForDepartmentAsync(int departmentId)
		{
			return departmentId <= 0 ? new List<MemberLeavePeriod>() : await _leaveRepository.GetForDepartmentAsync(departmentId) ?? new List<MemberLeavePeriod>();
		}

		public async Task<List<MemberLeavePeriod>> GetForMemberAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<MemberLeavePeriod>();

			return await _leaveRepository.GetForMemberAsync(departmentId, userId) ?? new List<MemberLeavePeriod>();
		}

		public async Task<Dictionary<string, List<MemberLeavePeriod>>> GetByMemberAsync(int departmentId)
		{
			return (await GetForDepartmentAsync(departmentId))
				.Where(l => !string.IsNullOrWhiteSpace(l.UserId))
				.GroupBy(l => l.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderBy(l => l.StartsOn).ToList(), StringComparer.OrdinalIgnoreCase);
		}

		public async Task<MemberLeavePeriod> SaveAsync(int departmentId, string userId, DateTime startLocalDate, DateTime endLocalDate, MemberLeaveKinds kind, string note,
			string actingUserId, int memberLeavePeriodId = 0, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || endLocalDate.Date < startLocalDate.Date || (endLocalDate.Date - startLocalDate.Date).TotalDays > MaxLeaveDays)
				return null;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId);
			if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			MemberLeavePeriod period;

			if (memberLeavePeriodId > 0)
			{
				period = await _leaveRepository.GetByIdAsync(memberLeavePeriodId);
				if (period == null || period.DepartmentId != departmentId)
					return null;
			}
			else
			{
				period = new MemberLeavePeriod { DepartmentId = departmentId, CreatedOn = DateTime.UtcNow, CreatedByUserId = actingUserId };
			}

			var before = memberLeavePeriodId > 0 ? JsonConvert.SerializeObject(period) : null;
			period.UserId = member.UserId;
			period.StartsOn = TrainingDueDates.StartOfLocalDayUtc(startLocalDate, department);
			period.EndsOn = TrainingDueDates.EndOfLocalDayUtc(endLocalDate, department);
			period.Kind = Enum.IsDefined(typeof(MemberLeaveKinds), kind) ? (int)kind : (int)MemberLeaveKinds.Other;
			period.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim().Length > 500 ? note.Trim().Substring(0, 500) : note.Trim();

			var saved = await _leaveRepository.SaveOrUpdateAsync(period, cancellationToken, true);
			Audit(departmentId, actingUserId, before, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeleteAsync(int departmentId, int memberLeavePeriodId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var period = await _leaveRepository.GetByIdAsync(memberLeavePeriodId);
			if (period == null || period.DepartmentId != departmentId)
				return false;

			var deleted = await _leaveRepository.DeleteForDepartmentAsync(departmentId, memberLeavePeriodId, cancellationToken);
			if (deleted)
				Audit(departmentId, actingUserId, JsonConvert.SerializeObject(period), null);
			return deleted;
		}

		private void Audit(int departmentId, string userId, string before, string after)
		{
			_eventAggregator?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.MemberLeavePeriodChanged,
				Before = before,
				After = after,
				Successful = true
			});
		}
	}
}
