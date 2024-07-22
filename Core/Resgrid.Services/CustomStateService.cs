using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

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
				return GetDefaultPersonStatuses();
			}
		}

		public async Task<List<CustomStateDetail>> GetCustomPersonnelStaffingsOrDefaultsAsync(int departmentId)
		{
			var statuses = await GetActiveStaffingLevelsForDepartmentAsync(departmentId);

			if (statuses != null && statuses.GetActiveDetails().Any())
			{
				return statuses.GetActiveDetails();
			}
			else
			{
				return GetDefaultPersonStaffings();
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

				if (states != null)
				{
					foreach (var state in states)
					{
						if (state.Details == null)
							state.Details = new List<CustomStateDetail>();
					}

					return states.ToList();
				}
				else
				{
					return new List<CustomState>();
				}
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
			{
				var result = await _cacheProvider.RetrieveAsync<List<CustomState>>(string.Format(CacheKey, departmentId), getCustomStates, CacheLength);

				if (result == null || result.Count() <= 0)
					return await getCustomStates();

				return result;
			}
			else
				return await getCustomStates();
		}

		public void InvalidateCustomStateInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public async Task<CustomState> SaveAsync(CustomState customState, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _customStateRepository.SaveOrUpdateAsync(customState, cancellationToken);

			await _cacheProvider.RemoveAsync(string.Format(CacheKey, customState.DepartmentId));

			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = customState.DepartmentId });

			return saved;
		}

		public async Task<CustomStateDetail> GetCustomDetailByIdAsync(int detailId)
		{
			return await _customStateDetailRepository.GetByIdAsync(detailId);
		}

		public async Task<CustomStateDetail> SaveDetailAsync(CustomStateDetail customStateDetail, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _customStateDetailRepository.SaveOrUpdateAsync(customStateDetail, cancellationToken);
			_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = departmentId });

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
			var missingDetails = state.Details.Where(p => !details.Any(p2 => p2.CustomStateDetailId == p.CustomStateDetailId)).ToList();

			if (missingDetails != null && missingDetails.Any())
			{
				foreach (var missingDetail in missingDetails)
				{
					await DeleteDetailAsync(missingDetail, cancellationToken);
					state.Details.Remove(missingDetail);
				}
			}

			foreach (var detail in details)
			{
				detail.CustomStateId = state.CustomStateId;
				if (detail.CustomStateDetailId == 0)
				{
					state.Details.Add(detail);
				}
				else
				{
					var existingDetail = state.Details.FirstOrDefault(x => x.CustomStateDetailId == detail.CustomStateDetailId);

					if (existingDetail != null)
					{
						existingDetail.Order = detail.Order;
					}
				}
			}

			return await SaveAsync(state, cancellationToken);
		}

		public async Task<CustomStateDetail> GetCustomDetailForDepartmentAsync(int departmentId, int detailId)
		{
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null && states.Count > 0)
			{
				var detail = states.Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == detailId)).FirstOrDefault(detail => detail != null);
				return detail;
			}

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

		public List<CustomStateDetail> GetDefaultUnitStatuses()
		{
			List<CustomStateDetail> details = new List<CustomStateDetail>();
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Responding, ButtonText = "Responding", ButtonColor = "#32db64", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Available, ButtonText = "Available", ButtonColor = "#d1dade", TextColor = "5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Unavailable, ButtonText = "Unavailable", ButtonColor = "" });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Committed, ButtonText = "Committed", ButtonColor = "#50b8de", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.CallsAndStations });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Delayed, ButtonText = "Delayed", ButtonColor = "" });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.OnScene, ButtonText = "On Scene", ButtonColor = "#69BB7B", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Staging, ButtonText = "Staging", ButtonColor = "#ffc900", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Returning, ButtonText = "Returning", ButtonColor = "#387ef5", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.OutOfService, ButtonText = "Out of Service", ButtonColor = "#ff6b69", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Cancelled, ButtonText = "Cancelled", ButtonColor = "#ff6b69" });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Released, ButtonText = "Released", ButtonColor = "" });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Manual, ButtonText = "Manual", ButtonColor = "" });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Enroute, ButtonText = "Enroute", ButtonColor = "" });

			return details;

		}

		public List<CustomStateDetail> GetDefaultPersonStatuses()
		{
			List<CustomStateDetail> details = new List<CustomStateDetail>();
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.Responding, ButtonText = "Responding", ButtonColor = "#449d44", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.CallsAndStations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.NotResponding, ButtonText = "Not Responding", ButtonColor = "#ed5565", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.OnScene, ButtonText = "On Scene", ButtonColor = "#262626", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.StandingBy, ButtonText = "Standing By", ButtonColor = "#d1dade", TextColor = "5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.AvailableStation, ButtonText = "Available Station", ButtonColor = "#d1dade", TextColor = "5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.RespondingToStation, ButtonText = "Responding to Station", ButtonColor = "#449d44", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.RespondingToScene, ButtonText = "Responding to Scene", ButtonColor = "#449d44", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });

			return details;
		}

		public List<CustomStateDetail> GetDefaultPersonStaffings()
		{
			List<CustomStateDetail> details = new List<CustomStateDetail>();
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Available, ButtonText = "Available", ButtonColor = "#d1dade", TextColor = "5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Delayed, ButtonText = "Delayed", ButtonColor = "#f8ac59", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Unavailable, ButtonText = "Unavailable", ButtonColor = "#ed5565", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Committed, ButtonText = "Committed", ButtonColor = "#23c6c8", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.OnShift, ButtonText = "On Shift", ButtonColor = "#228bcb", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
		

			return details;
		}
	}
}
