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
	/// <summary>
	/// Orchestrates on-demand incident tactical voice channels on top of the existing department voice addon.
	/// Logs open/close to the command timeline via repositories (does not depend on IIncidentCommandService, so
	/// IncidentCommandService can call this on command-close without a circular dependency).
	/// </summary>
	public class IncidentVoiceService : IIncidentVoiceService
	{
		private readonly IVoiceService _voiceService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICommandLogEntryRepository _commandLogEntryRepository;
		private readonly IIncidentCommandRepository _incidentCommandRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICoreEventService _coreEventService;

		public IncidentVoiceService(
			IVoiceService voiceService,
			IDepartmentsService departmentsService,
			ICommandLogEntryRepository commandLogEntryRepository,
			IIncidentCommandRepository incidentCommandRepository,
			IEventAggregator eventAggregator,
			ICoreEventService coreEventService)
		{
			_voiceService = voiceService;
			_departmentsService = departmentsService;
			_commandLogEntryRepository = commandLogEntryRepository;
			_incidentCommandRepository = incidentCommandRepository;
			_eventAggregator = eventAggregator;
			_coreEventService = coreEventService;
		}

		public async Task<bool> CanUseVoiceAsync(int departmentId)
		{
			return await _voiceService.CanDepartmentUseVoiceAsync(departmentId);
		}

		public async Task<DepartmentVoiceChannel> CreateIncidentChannelAsync(int departmentId, int callId, string name, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!await _voiceService.CanDepartmentUseVoiceAsync(departmentId))
				return null;

			// The call must have an active command owned by the caller's department. CallId is an auto-increment
			// integer (guessable), so this prevents opening a channel against another department's call.
			var commands = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			var command = commands?.FirstOrDefault(x => x.CallId == callId && x.Status == (int)IncidentCommandStatus.Active);
			if (command == null)
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department == null)
				return null;

			var channelName = string.IsNullOrWhiteSpace(name) ? $"Incident {callId}" : name;

			var channel = await _voiceService.SaveChannelToVoipProviderAsync(department, channelName, cancellationToken);
			if (channel == null)
				return null;

			channel.CallId = callId;
			channel.IsOnDemand = true;
			channel.ClosedOn = null;
			channel = await _voiceService.SaveOrUpdateVoiceChannelAsync(channel, departmentId, cancellationToken);

			await WriteLogAsync(departmentId, callId, CommandLogEntryType.ChannelOpened, $"Tactical channel '{channelName}' opened", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentChannelOpenedEvent>(new IncidentChannelOpenedEvent { DepartmentId = departmentId, CallId = callId, DepartmentVoiceChannelId = channel.DepartmentVoiceChannelId, Name = channelName });
			return channel;
		}

		public async Task<List<DepartmentVoiceChannel>> GetChannelsForCallAsync(int departmentId, int callId)
		{
			var voice = await _voiceService.GetVoiceSettingsForDepartmentAsync(departmentId);
			if (voice?.Channels == null)
				return new List<DepartmentVoiceChannel>();

			return voice.Channels.Where(c => c.IsOnDemand && c.CallId == callId && c.ClosedOn == null).ToList();
		}

		public async Task<bool> CloseIncidentChannelsForCallAsync(int departmentId, int callId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var channels = await GetChannelsForCallAsync(departmentId, callId);
			if (channels == null || !channels.Any())
				return false;

			foreach (var channel in channels)
			{
				channel.ClosedOn = DateTime.UtcNow;
				await _voiceService.SaveOrUpdateVoiceChannelAsync(channel, departmentId, cancellationToken);
			}

			await WriteLogAsync(departmentId, callId, CommandLogEntryType.ChannelClosed, $"{channels.Count} tactical channel(s) closed", userId, cancellationToken);
			return true;
		}

		private async Task WriteLogAsync(int departmentId, int callId, CommandLogEntryType type, string description, string userId, CancellationToken cancellationToken)
		{
			var commands = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			var command = commands?.FirstOrDefault(x => x.CallId == callId && x.Status == (int)IncidentCommandStatus.Active);
			if (command == null)
				return;

			var entry = new CommandLogEntry
			{
				CommandLogEntryId = Guid.NewGuid().ToString(),
				IncidentCommandId = command.IncidentCommandId,
				DepartmentId = departmentId,
				CallId = callId,
				EntryType = (int)type,
				Description = description,
				UserId = userId,
				OccurredOn = DateTime.UtcNow
			};

			await _commandLogEntryRepository.SaveOrUpdateAsync(entry, cancellationToken);

			// Real-time: channel open/close is a board change.
			await _coreEventService.IncidentCommandUpdatedAsync(departmentId, callId);
		}
	}
}
