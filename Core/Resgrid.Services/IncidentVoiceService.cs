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
		private readonly IVoiceTransmissionLogRepository _voiceTransmissionLogRepository;
		private readonly IIncidentCommandRepository _incidentCommandRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICoreEventService _coreEventService;

		public IncidentVoiceService(
			IVoiceService voiceService,
			IDepartmentsService departmentsService,
			ICommandLogEntryRepository commandLogEntryRepository,
			IVoiceTransmissionLogRepository voiceTransmissionLogRepository,
			IIncidentCommandRepository incidentCommandRepository,
			IEventAggregator eventAggregator,
			ICoreEventService coreEventService)
		{
			_voiceService = voiceService;
			_departmentsService = departmentsService;
			_commandLogEntryRepository = commandLogEntryRepository;
			_voiceTransmissionLogRepository = voiceTransmissionLogRepository;
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

		public async Task<VoiceTransmissionLog> LogTransmissionAsync(VoiceTransmissionLog log, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (log == null || string.IsNullOrWhiteSpace(log.DepartmentVoiceChannelId) || string.IsNullOrWhiteSpace(log.UserId))
				return null;

			// The channel must be one of this call's open incident channels for the caller's department —
			// prevents writing transmissions against another department's channel ids.
			var channels = await GetChannelsForCallAsync(log.DepartmentId, log.CallId);
			if (channels == null || channels.All(c => c.DepartmentVoiceChannelId != log.DepartmentVoiceChannelId))
				return null;

			if (string.IsNullOrWhiteSpace(log.VoiceTransmissionLogId))
				log.VoiceTransmissionLogId = Guid.NewGuid().ToString();
			if (log.StartedOn == default(DateTime))
				log.StartedOn = DateTime.UtcNow;

			// Append-only insert (see WriteLogAsync note about pre-set GUIDs and SaveOrUpdateAsync).
			return await _voiceTransmissionLogRepository.InsertAsync(log, cancellationToken);
		}

		public async Task<List<VoiceTransmissionLog>> GetTransmissionLogForCallAsync(int departmentId, int callId)
		{
			var logs = await _voiceTransmissionLogRepository.GetAllByDepartmentIdAsync(departmentId);
			if (logs == null)
				return new List<VoiceTransmissionLog>();

			return logs.Where(l => l.CallId == callId).OrderByDescending(l => l.StartedOn).ToList();
		}

		private async Task WriteLogAsync(int departmentId, int callId, CommandLogEntryType type, string description, string userId, CancellationToken cancellationToken)
		{
			var commands = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			// Match the call's command regardless of status (most-recent first): the close-command flow sets the
			// command to Closed before auto-closing its channels, so an Active-only filter would silently drop the
			// channel-closed timeline entry and its real-time board update.
			var command = commands?.Where(x => x.CallId == callId).OrderByDescending(x => x.EstablishedOn).FirstOrDefault();
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

			// Append-only insert: a pre-set GUID would make SaveOrUpdateAsync issue a 0-row UPDATE instead of inserting.
			await _commandLogEntryRepository.InsertAsync(entry, cancellationToken);

			// Real-time: channel open/close is a board change.
			await _coreEventService.IncidentCommandUpdatedAsync(departmentId, callId);
		}
	}
}
