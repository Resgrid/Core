using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The knowledge gaps report (§16.4): questions the department's content did not cover and answers given a thumbs-down. Seen by the
	/// people who can fix the content: department administrators, protocol authors and approvers, and members who create or assign
	/// trainings (training officers). Each of them turns a gap into a draft protocol change or a new training; the author writes it.
	/// </summary>
	public class KnowledgeGapsService : IKnowledgeGapsService
	{
		public const int MaxPage = 200;

		private readonly IKnowledgeGapsRepository _repository;
		private readonly IProtocolAccessService _protocolAccess;
		private readonly IAuthorizationService _authorization;
		private readonly IEventAggregator _eventAggregator;

		public KnowledgeGapsService(IKnowledgeGapsRepository repository, IProtocolAccessService protocolAccess, IAuthorizationService authorization, IEventAggregator eventAggregator)
		{
			_repository = repository;
			_protocolAccess = protocolAccess;
			_authorization = authorization;
			_eventAggregator = eventAggregator;
		}

		public async Task<bool> CanSeeGapsAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return false;
			try
			{
				var reader = await _protocolAccess.GetReaderAsync(departmentId, userId);
				if (reader == null || !reader.IsCurrentMember)
					return false;
				if (reader.IsDepartmentAdmin || reader.MayAuthor || reader.MayApprove)
					return true;
				var training = await _authorization.GetTrainingManagementScopeAsync(userId, departmentId);
				return training != null && (training.CanCreate || training.AssignRight.HoldsAny);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Knowledge gaps access could not be read.");
				return false;
			}
		}

		public async Task<List<KnowledgeGap>> GetGapsAsync(int departmentId, int? status, int skip, int take, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0)
				return new List<KnowledgeGap>();
			return await _repository.GetForDepartmentAsync(departmentId, ValidStatus(status), Math.Max(0, skip), Math.Clamp(take, 1, MaxPage), cancellationToken) ?? new List<KnowledgeGap>();
		}

		public Task<int> CountAsync(int departmentId, int? status, CancellationToken cancellationToken = default) =>
			departmentId <= 0 ? Task.FromResult(0) : _repository.CountAsync(departmentId, ValidStatus(status), cancellationToken);

		public async Task<KnowledgeGap> GetGapAsync(int departmentId, int knowledgeGapId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || knowledgeGapId <= 0)
				return null;
			var gap = await _repository.GetByIdAsync(departmentId, knowledgeGapId, cancellationToken);
			return gap != null && gap.DepartmentId == departmentId ? gap : null;
		}

		public async Task<KnowledgeGap> RecordAsync(KnowledgeGap gap, CancellationToken cancellationToken = default)
		{
			if (gap == null || gap.DepartmentId <= 0)
				return null;
			gap.Status = (int)KnowledgeGapStatuses.Open;
			gap.ResolvedOn = null;
			gap.ResolvedByUserId = null;
			gap.ResolutionNote = null;
			if (gap.AskedOn == default)
				gap.AskedOn = DateTime.UtcNow;
			return await _repository.InsertAsync(gap, cancellationToken);
		}

		public async Task<bool> SetStatusAsync(int departmentId, int knowledgeGapId, KnowledgeGapStatuses status, string userId, string note, CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(KnowledgeGapStatuses), status))
				return false;
			var gap = await GetGapAsync(departmentId, knowledgeGapId, cancellationToken);
			if (gap == null)
				return false;

			var before = gap.Status;
			gap.Status = (int)status;
			gap.ResolvedOn = status == KnowledgeGapStatuses.Open ? null : DateTime.UtcNow;
			gap.ResolvedByUserId = status == KnowledgeGapStatuses.Open ? null : userId;
			var trimmed = (note ?? string.Empty).Trim();
			gap.ResolutionNote = status == KnowledgeGapStatuses.Open || trimmed.Length == 0 ? null
				: trimmed.Length > KnowledgeGap.ResolutionNoteMaxLength ? trimmed.Substring(0, KnowledgeGap.ResolutionNoteMaxLength) : trimmed;

			if (!await _repository.UpdateStatusAsync(gap, cancellationToken))
				return false;

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.KnowledgeGapStatusChanged,
				Before = JsonConvert.SerializeObject(new { gap.KnowledgeGapId, Status = before }),
				After = JsonConvert.SerializeObject(new { gap.KnowledgeGapId, gap.Status, gap.ResolutionNote }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return true;
		}

		private static int? ValidStatus(int? status) => status.HasValue && Enum.IsDefined(typeof(KnowledgeGapStatuses), status.Value) ? status : null;
	}
}
