using System;
using System.Collections.Generic;
using System.Linq;
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

		private readonly IGenericDataRepository<CustomState> _customStateRepository;
		private readonly IGenericDataRepository<CustomStateDetail> _customStateDetailRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;

		public CustomStateService(IGenericDataRepository<CustomState> customStateRepository, IGenericDataRepository<CustomStateDetail> customStateDetailRepository,
			IEventAggregator eventAggregator, ICacheProvider cacheProvider)
		{
			_customStateRepository = customStateRepository;
			_customStateDetailRepository = customStateDetailRepository;
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
		}

		public CustomState GetCustomSateById(int customStateId)
		{
			return _customStateRepository.GetAll().FirstOrDefault(x => x.CustomStateId == customStateId);
		}

		public List<CustomState> GetAllActiveCustomStatesForDepartment(int departmentId)
		{
			var states = GetAllCustomStatesForDepartment(departmentId);

			if (states != null)
				return states.Where(x => x.DepartmentId == departmentId && x.IsDeleted == false).ToList();

			return null;
		}

		public List<CustomState> GetAllActiveUnitStatesForDepartment(int departmentId)
		{
			var states = GetAllCustomStatesForDepartment(departmentId);

			if (states != null)
				return states.Where(x => x.DepartmentId == departmentId && x.IsDeleted == false && x.Type == (int)CustomStateTypes.Unit).ToList();

			return null;
		}

		public CustomState GetActivePersonnelStateForDepartment(int departmentId)
		{
			CustomState state = null;
			var states = GetAllCustomStatesForDepartment(departmentId);

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

		public CustomState GetActiveStaffingLevelsForDepartment(int departmentId)
		{
			CustomState state = null;
			var states = GetAllCustomStatesForDepartment(departmentId);

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

		public List<CustomState> GetAllCustomStatesForDepartment(int departmentId)
		{
			Func<List<CustomState>> getCustomStates = delegate()
			{
				var states = _customStateRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();

				foreach (var state in states)
				{
					if (state.Details == null)
						state.Details = new List<CustomStateDetail>();
				}

				return states;
			};

			if (Config.SystemBehaviorConfig.CacheEnabled)
				return _cacheProvider.Retrieve<List<CustomState>>(string.Format(CacheKey, departmentId), getCustomStates,
					CacheLength);
			else
				return getCustomStates();
		}

		public void InvalidateCustomStateInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public CustomState Save(CustomState customState)
		{
			_customStateRepository.SaveOrUpdate(customState);

			_cacheProvider.Remove(string.Format(CacheKey, customState.DepartmentId));

			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = customState.DepartmentId });

			return customState;
		}

		public CustomStateDetail GetCustomDetailById(int detailId)
		{
			return _customStateDetailRepository.GetAll().FirstOrDefault(x => x.CustomStateDetailId == detailId);
		}

		public CustomStateDetail SaveDetail(CustomStateDetail customStateDetail)
		{
			_customStateDetailRepository.SaveOrUpdate(customStateDetail);
			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = customStateDetail.CustomState.DepartmentId });

			return customStateDetail;
		}

		public void Delete(CustomState customState)
		{
			customState.IsDeleted = true;

			foreach (var existingDetails in customState.Details)
			{
				DeleteDetail(existingDetails);
			}

			Save(customState);
		}

		public void DeleteDetail(CustomStateDetail detail)
		{
			detail.IsDeleted = true;

			_customStateDetailRepository.SaveOrUpdate(detail);
		}

		public CustomState Update(CustomState state, List<CustomStateDetail> details)
		{
			foreach (var existingDetails in state.Details)
			{
				DeleteDetail(existingDetails);
			}

			foreach (var detail in details)
			{
				state.Details.Add(detail);
			}

			return Save(state);
		}

		public CustomStateDetail GetCustomDetailForDepartment(int departmentId, int detailId)
		{
			var states = GetAllCustomStatesForDepartment(departmentId);

			if (states != null && states.Count > 0)
				return states.Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == detailId)).FirstOrDefault(detail => detail != null);

			return null;
		}

		public CustomStateDetail GetCustomPersonnelStaffing(int departmentId, UserState state)
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
				var stateDetail = GetCustomDetailForDepartment(departmentId, state.State);

				return stateDetail;
			}
		}

		public CustomStateDetail GetCustomPersonnelStatus(int departmentId, ActionLog state)
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
				var stateDetail = GetCustomDetailForDepartment(departmentId, state.ActionTypeId);

				return stateDetail;
			}
		}

		public CustomStateDetail GetCustomUnitState(UnitState state)
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
				var stateDetail = GetCustomDetailForDepartment(state.Unit.DepartmentId, state.State);

				return stateDetail;
			}
		}
	}
}
