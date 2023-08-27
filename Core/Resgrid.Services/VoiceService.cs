using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Voip;

namespace Resgrid.Services
{
	public class VoiceService : IVoiceService
	{
		private readonly IDepartmentVoiceRepository _departmentVoiceRepository;
		private readonly IDepartmentVoiceChannelRepository _departmentVoiceChannelRepository;
		private readonly IDepartmentVoiceUserRepository _departmentVoiceUserRepository;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IVoipProvider _voipProvider;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentAudioRepository _departmentAudioRepository;

		public VoiceService(IDepartmentVoiceRepository departmentVoiceRepository, IDepartmentVoiceChannelRepository departmentVoiceChannelRepository,
			IDepartmentVoiceUserRepository departmentVoiceUserRepository, ISubscriptionsService subscriptionsService, IDepartmentsService departmentsService,
			IVoipProvider voipProvider, IUserProfileService userProfileService, IDepartmentAudioRepository departmentAudioRepository)
		{
			_departmentVoiceRepository = departmentVoiceRepository;
			_departmentVoiceChannelRepository = departmentVoiceChannelRepository;
			_departmentVoiceUserRepository = departmentVoiceUserRepository;
			_subscriptionsService = subscriptionsService;
			_departmentsService = departmentsService;
			_voipProvider = voipProvider;
			_userProfileService = userProfileService;
			_departmentAudioRepository = departmentAudioRepository;
		}

		public async Task<bool> CanDepartmentUseVoiceAsync(int departmentId)
		{
			var addonPlans = await _subscriptionsService.GetAllAddonPlansByTypeAsync(PlanAddonTypes.PTT);
			var addonPayment = await _subscriptionsService.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, addonPlans.Select(x => x.PlanAddonId).ToList());

			if (addonPayment != null && addonPayment.Count > 0)
				return true;

			return false;
		}

		public async Task<DepartmentVoice> GetVoiceSettingsForDepartmentAsync(int departmentId)
		{
			var departmentVoice = await _departmentVoiceRepository.GetDepartmentVoiceByDepartmentIdAsync(departmentId);

			return departmentVoice;
		}

		public async Task<bool> InitializeDepartmentUsersWithVoipProviderAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (await CanDepartmentUseVoiceAsync(departmentId))
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
				var voice = await GetOrCreateDepartmentVoiceRecordAsync(department);
				var users = await _departmentsService.GetAllUsersForDepartmentAsync(departmentId, true, true);
				var userProfiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId, true);
				
				if (users != null && users.Any())
				{
					foreach (var user in users)
					{
						var profile = userProfiles[user.UserId];

						if (profile != null)
						{
							await SaveUserToVoipProviderAsync(voice, profile, user.Email, cancellationToken);
						}
					}

					return true;
				}
			}

			return false;
		}

		public async Task<DepartmentVoice> GetOrCreateDepartmentVoiceRecordAsync(Department department, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (await CanDepartmentUseVoiceAsync(department.DepartmentId))
			{
				var voice = await _departmentVoiceRepository.GetDepartmentVoiceByDepartmentIdAsync(department.DepartmentId);

				if (voice != null)
					return voice;

				voice = new DepartmentVoice();
				voice.DepartmentId = department.DepartmentId;
				voice.StartConferenceNumber = await GetNextConferenceExtensionBaseNumber();

				var savedVoice = await _departmentVoiceRepository.SaveOrUpdateAsync(voice, cancellationToken);

				return savedVoice;
			}

			return null;
		}

		public async Task<DepartmentVoiceUser> SaveUserToVoipProviderAsync(DepartmentVoice voice, UserProfile profile, string emailAddress, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.Kazoo)
			{
				if (await CanDepartmentUseVoiceAsync(voice.DepartmentId))
				{
					var userVoice = await _departmentVoiceUserRepository.GetDepartmentVoiceUserByUserIdAsync(profile.UserId);
					string systemUserId = string.Empty;
					string deviceId = string.Empty;

					if (userVoice != null)
					{
						if (!string.IsNullOrWhiteSpace(userVoice.SystemUserId))
							systemUserId = userVoice.SystemUserId;

						if (!string.IsNullOrWhiteSpace(userVoice.SystemDeviceId))
							deviceId = userVoice.SystemDeviceId;
					}

					systemUserId = await _voipProvider.CreateUserIfNotExistsAsync(systemUserId, emailAddress, profile, voice.DepartmentId);
					deviceId = await _voipProvider.CreateDeviceForUserIfNotExistsAsync(systemUserId, deviceId, profile, voice.DepartmentId);

					if (userVoice == null)
						userVoice = new DepartmentVoiceUser();

					userVoice.DepartmentVoiceId = voice.DepartmentVoiceId;
					userVoice.UserId = profile.UserId;
					userVoice.SystemUserId = systemUserId;
					userVoice.SystemDeviceId = deviceId;

					var savedResult = await _departmentVoiceUserRepository.SaveOrUpdateAsync(userVoice, cancellationToken, true);

					return savedResult;
				}
			}

			return null;
		}

		public async Task<DepartmentVoiceChannel> SaveChannelToVoipProviderAsync(Department department, string name, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (await CanDepartmentUseVoiceAsync(department.DepartmentId))
			{
				var voice = await _departmentVoiceRepository.GetDepartmentVoiceByDepartmentIdAsync(department.DepartmentId);
				int confNumber = voice.StartConferenceNumber;
				bool isDefault = true;
				DepartmentVoiceChannel channel = null;
				var existingChannels = await _departmentVoiceChannelRepository.GetDepartmentVoiceChannelByDepartmentIdAsync(voice.DepartmentId);

				if (existingChannels != null && existingChannels.Any())
				{
					channel = existingChannels.FirstOrDefault(x => x.Name == name);
					confNumber = existingChannels.OrderByDescending(x => x.ConferenceNumber).First().ConferenceNumber + Config.VoipConfig.BaseChannelExtensionBump;
					isDefault = false;
				}

				if (channel == null)
					channel = new DepartmentVoiceChannel();

				if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.Kazoo)
				{
					var conference = await _voipProvider.CreateConferenceIfNotExistsAsync(channel.DepartmentVoiceChannelId, voice.DepartmentId, name,
					_departmentsService.ConvertDepartmentCodeToDigitPin(department.Code), confNumber);

					if (conference != null)
					{
						channel.DepartmentId = department.DepartmentId;
						channel.DepartmentVoiceId = voice.DepartmentVoiceId;
						channel.ConferenceNumber = confNumber;
						channel.SystemConferenceId = conference.Item1;
						channel.SystemCallflowId = conference.Item2;
						channel.Name = name;
						channel.IsDefault = isDefault;

						var savedChannel = await _departmentVoiceChannelRepository.SaveOrUpdateAsync(channel, cancellationToken);

						return savedChannel;
					}
				}
				else
				{
					channel.DepartmentId = department.DepartmentId;
					channel.DepartmentVoiceId = voice.DepartmentVoiceId;
					channel.ConferenceNumber = confNumber;
					channel.SystemConferenceId = "NA";
					channel.SystemCallflowId = "NA";
					channel.Name = name;
					channel.IsDefault = isDefault;

					var savedChannel = await _departmentVoiceChannelRepository.SaveOrUpdateAsync(channel, cancellationToken);

					return savedChannel;
				}
			}


			return null;
		}

		public async Task<string> GetOpenViduSessionToken(string sessionId)
		{
			var result = await _voipProvider.CreateOpenViduSessionAndGetToken(sessionId);

			return result;
		}

		public async Task<DepartmentVoiceChannel> GetDepartmentVoiceChannelByIdAsync(string departmentVoiceChannelId)
		{
			var result = await _departmentVoiceChannelRepository.GetByIdAsync(departmentVoiceChannelId);

			return result;
		}

		public async Task<bool> DeleteDepartmentVoiceChannelAsync(DepartmentVoiceChannel channel, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = await _departmentVoiceChannelRepository.DeleteAsync(channel, cancellationToken);

			return result;
		}

		private async Task<int> GetNextConferenceExtensionBaseNumber()
		{
			var voiceDepartments = await _departmentVoiceRepository.GetAllAsync();
			var latestVoiceDepartment = voiceDepartments.OrderByDescending(x => x.StartConferenceNumber).FirstOrDefault();

			if (latestVoiceDepartment == null)
				return Config.VoipConfig.BaseChannelExtensionNumber;

			return latestVoiceDepartment.StartConferenceNumber + Config.VoipConfig.BaseChannelExtensionBump;
		}

		public async Task<DepartmentVoiceChannel> GetVoiceChannelByIdAsync(string voiceChannelId)
		{
			return await _departmentVoiceChannelRepository.GetByIdAsync(voiceChannelId);
		}

		public async Task<DepartmentVoiceChannel> SaveOrUpdateVoiceChannelAsync(DepartmentVoiceChannel voiceChannel, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (voiceChannel.IsDefault)
			{
				var voiceChannels = await _departmentVoiceChannelRepository.GetDepartmentVoiceChannelByDepartmentIdAsync(departmentId);

				if (voiceChannels != null && voiceChannels.Any())
				{
					foreach (var channel in voiceChannels)
					{
						channel.IsDefault = false;
						await _departmentVoiceChannelRepository.SaveOrUpdateAsync(channel, cancellationToken, true);
					}
				}
			}
			
			return await _departmentVoiceChannelRepository.SaveOrUpdateAsync(voiceChannel, cancellationToken, true);
		}

		public async Task<DepartmentVoiceUtilization> GetCurrentUtilizationForLiveKit(int departmentId)
		{
			var result = new DepartmentVoiceUtilization();
			var addonPlans = await _subscriptionsService.GetAllAddonPlansByTypeAsync(PlanAddonTypes.PTT);
			var addonPayment = await _subscriptionsService.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, addonPlans.Select(x => x.PlanAddonId).ToList());

			if (addonPayment != null && addonPayment.Count > 0)
			{
				foreach (var payment in addonPayment)
				{
					result.SeatLimit += (int)(payment.Quantity * 10);
				}

				var livekitProvider = new LiveKitProvider();
				var voiceChannels = await _departmentVoiceChannelRepository.GetDepartmentVoiceChannelByDepartmentIdAsync(departmentId);

				if (voiceChannels != null)
				{
					foreach (var channel in voiceChannels)
					{
						var participants = await livekitProvider.ListRoomParticipants(channel.DepartmentVoiceChannelId);

						if (participants != null)
							result.CurrentlyActive += participants.Count();
					}
				}
			}

			return result;
		}

		public async Task<DepartmentAudio> SaveDepartmentAudioAsync(DepartmentAudio departmentAudio, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _departmentAudioRepository.SaveOrUpdateAsync(departmentAudio, cancellationToken);
		}

		public async Task<List<DepartmentAudio>> GetDepartmentAudiosByDepartmentIdAsync(int departmentId)
		{
			var items = await _departmentAudioRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null)
				return items.ToList();

			return new List<DepartmentAudio>();
		}

		public async Task<DepartmentAudio> GetDepartmentAudioByIdAsync(string id)
		{
			return await _departmentAudioRepository.GetByIdAsync(id);
		}

		public async Task<bool> DeleteDepartmentAudioAsync(DepartmentAudio audio, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = await _departmentAudioRepository.DeleteAsync(audio, cancellationToken);

			return result;
		}
	}
}
