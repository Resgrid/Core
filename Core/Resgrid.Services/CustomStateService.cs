using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Cache;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Services
{
	public class CustomStateService : ICustomStateService
	{
		private static string CacheKey = "CustomStates_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(7);

		private readonly ICustomStateRepository _customStateRepository;
		private readonly ICustomStateDetailRepository _customStateDetailRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;

		public CustomStateService(ICustomStateRepository customStateRepository, ICustomStateDetailRepository customStateDetailRepository,
			IEventAggregator eventAggregator, ICacheProvider cacheProvider)
		{
			_customStateRepository = customStateRepository;
			_customStateDetailRepository = customStateDetailRepository;
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
		}

		public async Task<CustomState> GetCustomSateByIdAsync(int customStateId)
		{
			return await _customStateRepository.GetCustomStatesByIdAsync(customStateId);
		}

		public async Task<List<CustomState>> GetAllActiveCustomStatesForDepartmentAsync(int departmentId)
		{
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				return states.Where(x => x.DepartmentId == departmentId && x.IsDeleted == false).ToList();

			return null;
		}

		public async Task<List<CustomState>> GetAllActiveUnitStatesForDepartmentAsync(int departmentId)
		{
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				return states.Where(x => x.DepartmentId == departmentId && x.IsDeleted == false && x.Type == (int)CustomStateTypes.Unit).ToList();

			return null;
		}

		public async Task<CustomState> GetActivePersonnelStateForDepartmentAsync(int departmentId)
		{
			CustomState state = null;
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				state = states.FirstOrDefault(x => x.DepartmentId == departmentId && x.IsDeleted == false && x.Type == (int)CustomStateTypes.Personnel);

			if (state != null)
			{
				if (state.Details == null)
					state.Details = new List<CustomStateDetail>();

				return state;
			}

			return null;
		}

		public async Task<List<CustomStateDetail>> GetCustomPersonnelStatusesOrDefaultsAsync(int departmentId)
		{
			var statuses = await GetActivePersonnelStateForDepartmentAsync(departmentId);

			if (statuses != null && statuses.GetActiveDetails().Any())
			{
				return statuses.GetActiveDetails();
			}
			else
			{
				List<CustomStateDetail> details = new List<CustomStateDetail>();
				details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.StandingBy, ButtonText = "Available" });
				details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.NotResponding, ButtonText = "Not Responding" });
				details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.Responding, ButtonText = "Responding" });
				details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.OnScene, ButtonText = "On Scene" });
				details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.AvailableStation, ButtonText = "Available Station" });
				details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.RespondingToStation, ButtonText = "Responding To Station" });
				details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.RespondingToScene, ButtonText = "Responding To Scene" });

				return details;
			}
		}

		public async Task<CustomState> GetActiveStaffingLevelsForDepartmentAsync(int departmentId)
		{
			CustomState state = null;
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				state = states.FirstOrDefault(x => x.DepartmentId == departmentId && x.IsDeleted == false && x.Type == (int)CustomStateTypes.Staffing);

			if (state != null)
			{
				if (state.Details == null)
					state.Details = new List<CustomStateDetail>();

				return state;
			}

			return null;
		}

		public async Task<List<CustomState>> GetAllCustomStatesForDepartmentAsync(int departmentId)
		{
			async Task<List<CustomState>> getCustomStates()
			{
				var states = await _customStateRepository.GetCustomStatesByDepartmentIdAsync(departmentId);

				foreach (var state in states)
				{
					if (state.Details == null)
						state.Details = new List<CustomStateDetail>();
				}

				return states.ToList();
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync<List<CustomState>>(string.Format(CacheKey, departmentId), getCustomStates,
					CacheLength);
			else
				return await getCustomStates();
		}

		public void InvalidateCustomStateInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public async Task<CustomState> SaveAsync(CustomState customState, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _customStateRepository.SaveOrUpdateAsync(customState,cancellationToken);

			_cacheProvider.Remove(string.Format(CacheKey, customState.DepartmentId));

			await _eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = customState.DepartmentId });

			return saved;
		}

		public async Task<CustomStateDetail> GetCustomDetailByIdAsync(int detailId)
		{
			return await _customStateDetailRepository.GetByIdAsync(detailId);
		}

		public async Task<CustomStateDetail> SaveDetailAsync(CustomStateDetail customStateDetail, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _customStateDetailRepository.SaveOrUpdateAsync(customStateDetail, cancellationToken);
			await _eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = customStateDetail.CustomState.DepartmentId });

			return saved;
		}

		public async Task<CustomState> DeleteAsync(CustomState customState, CancellationToken cancellationToken = default(CancellationToken))
		{
			customState.IsDeleted = true;

			foreach (var existingDetails in customState.Details)
			{
				await DeleteDetailAsync(existingDetails, cancellationToken);
			}

			return await SaveAsync(customState, cancellationToken);
		}

		public async Task<CustomStateDetail> DeleteDetailAsync(CustomStateDetail detail, CancellationToken cancellationToken = default(CancellationToken))
		{
			detail.IsDeleted = true;

			return await _customStateDetailRepository.SaveOrUpdateAsync(detail, cancellationToken);
		}

		public async Task<CustomState> UpdateAsync(CustomState state, List<CustomStateDetail> details, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var existingDetails in state.Details)
			{
				await DeleteDetailAsync(existingDetails, cancellationToken);
			}

			foreach (var detail in details)
			{
				detail.CustomStateId = state.CustomStateId;
				state.Details.Add(detail);
			}

			return await SaveAsync(state, cancellationToken);
		}

		public async Task<CustomStateDetail> GetCustomDetailForDepartmentAsync(int departmentId, int detailId)
		{
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null && states.Count > 0)
				return states.Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == detailId)).FirstOrDefault(detail => detail != null);

			return null;
		}

		public async Task<CustomStateDetail> GetCustomPersonnelStaffingAsync(int departmentId, UserState state)
		{
			if (state.State <= 25)
			{
				var detail = new CustomStateDetail();

				detail.ButtonText = state.GetStaffingText();
				detail.ButtonColor = state.GetStaffingCss();

				if (string.IsNullOrWhiteSpace(detail.ButtonColor))
					detail.ButtonColor = "label-default";

				return detail;
			}
			else
			{
				var stateDetail = await GetCustomDetailForDepartmentAsync(departmentId, state.State);

				return stateDetail;
			}
		}

		public async Task<CustomStateDetail> GetCustomPersonnelStatusAsync(int departmentId, ActionLog state)
		{
			if (state.ActionTypeId <= 25)
			{
				var detail = new CustomStateDetail();

				detail.ButtonText = state.GetActionText();
				detail.ButtonColor = state.GetActionCss();

				if (string.IsNullOrWhiteSpace(detail.ButtonColor))
					detail.ButtonColor = "label-default";

				return detail;
			}
			else
			{
				var stateDetail = await GetCustomDetailForDepartmentAsync(departmentId, state.ActionTypeId);

				return stateDetail;
			}
		}

		public async Task<CustomStateDetail> GetCustomUnitStateAsync(UnitState state)
		{
			if (state.State <= 25)
			{
				var detail = new CustomStateDetail();

				detail.ButtonText = state.GetStatusText();
				detail.ButtonColor = state.GetStatusCss();

				if (string.IsNullOrWhiteSpace(detail.ButtonColor))
					detail.ButtonColor = "label-default";

				return detail;
			}
			else
			{
				var stateDetail = await GetCustomDetailForDepartmentAsync(state.Unit.DepartmentId, state.State);

				return stateDetail;
			}
		}
	}
}
