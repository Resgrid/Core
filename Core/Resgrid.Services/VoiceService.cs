using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

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

		public VoiceService(IDepartmentVoiceRepository departmentVoiceRepository, IDepartmentVoiceChannelRepository departmentVoiceChannelRepository,
			IDepartmentVoiceUserRepository departmentVoiceUserRepository, ISubscriptionsService subscriptionsService, IDepartmentsService departmentsService,
			IVoipProvider voipProvider, IUserProfileService userProfileService)
		{
			_departmentVoiceRepository = departmentVoiceRepository;
			_departmentVoiceChannelRepository = departmentVoiceChannelRepository;
			_departmentVoiceUserRepository = departmentVoiceUserRepository;
			_subscriptionsService = subscriptionsService;
			_departmentsService = departmentsService;
			_voipProvider = voipProvider;
			_userProfileService = userProfileService;
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
	}
}
