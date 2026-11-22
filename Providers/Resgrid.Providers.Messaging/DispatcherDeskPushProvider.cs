using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Providers.Messaging
{
	/// <summary>
	/// Desk pushes to the Dispatch app only (M0324, plan D6), through the user's Dispatch subscriber
	/// <c>{code}_Dispatch_User_{userId}</c>. The Dispatch app registers its device there (as well as on the user subscriber
	/// it shares with the Responder app, which still carries every other push), with the same Novu integrations its tokens
	/// use on the user subscriber. Best-effort: every failure is logged and answered with false, never thrown.
	/// </summary>
	public class DispatcherDeskPushProvider : IDispatcherDeskPushProvider
	{
		private readonly INovuProvider _novuProvider;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;

		public DispatcherDeskPushProvider(INovuProvider novuProvider, IDepartmentsService departmentsService, IUserProfileService userProfileService)
		{
			_novuProvider = novuProvider;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
		}

		public async Task<bool> RegisterDeviceAsync(int departmentId, string userId, int platform, string token, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token) || cancellationToken.IsCancellationRequested)
				return false;

			try
			{
				var code = await GetDepartmentCodeAsync(departmentId);
				if (code == null)
				{
					Logging.LogWarning($"DispatcherDeskPushProvider.RegisterDeviceAsync: department {departmentId} has no code; Dispatch desk device for user {userId} not registered.");
					return false;
				}

				if (!IsSupportedPlatform(platform))
				{
					Logging.LogWarning($"DispatcherDeskPushProvider.RegisterDeviceAsync: unsupported platform {platform} for user {userId} in department {departmentId}.");
					return false;
				}

				// The credential write lands on nothing unless the subscriber exists; an existing one answers as created.
				var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
				await _novuProvider.CreateDispatchUserSubscriber(userId, code, departmentId, profile?.MembershipEmail, profile?.FirstName, profile?.LastName);

				bool registered;
				switch ((Platforms)platform)
				{
					case Platforms.iOS:
						registered = await _novuProvider.UpdateDispatchUserSubscriberApns(userId, code, token);
						break;
					case Platforms.Android:
						registered = await _novuProvider.UpdateDispatchUserSubscriberFcm(userId, code, token);
						break;
					default:
						registered = await _novuProvider.AddDispatchUserSubscriberWebPushToken(userId, code, token);
						break;
				}

				if (!registered)
					Logging.LogError($"DispatcherDeskPushProvider.RegisterDeviceAsync: Novu rejected the credential write for user {userId} (platform {platform}, department {departmentId}); desk pushes will not reach this device.");

				return registered;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"DispatcherDeskPushProvider.RegisterDeviceAsync failed for user {userId} in department {departmentId} (platform {platform}).");
				return false;
			}
		}

		public async Task<bool> UnregisterDeviceAsync(int departmentId, string userId, int platform, string token, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token) || cancellationToken.IsCancellationRequested)
				return false;

			try
			{
				var code = await GetDepartmentCodeAsync(departmentId);
				if (code == null || !IsSupportedPlatform(platform))
					return false;

				var removed = platform == (int)Platforms.Web
					? await _novuProvider.RemoveDispatchUserSubscriberWebPushToken(userId, code, token)
					: await _novuProvider.RemoveDispatchUserSubscriberNativeToken(userId, code, token);

				// A device left on the desk subscriber keeps getting offers and break alerts after its dispatcher signed out.
				if (!removed)
					Logging.LogError($"DispatcherDeskPushProvider.UnregisterDeviceAsync: the token could not be removed for user {userId} (platform {platform}, department {departmentId}); that device may keep receiving desk pushes.");

				return removed;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"DispatcherDeskPushProvider.UnregisterDeviceAsync failed for user {userId} in department {departmentId} (platform {platform}).");
				return false;
			}
		}

		public async Task<bool> SendAsync(int departmentId, string userId, string title, string body, string eventCode, PushSoundTypes sound,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(eventCode) || cancellationToken.IsCancellationRequested)
				return false;

			try
			{
				var code = await GetDepartmentCodeAsync(departmentId);
				if (code == null)
				{
					Logging.LogWarning($"DispatcherDeskPushProvider.SendAsync: department {departmentId} has no code; desk push {eventCode} to user {userId} skipped.");
					return false;
				}

				return await _novuProvider.SendDispatchUserDeskPush(title ?? string.Empty, body ?? string.Empty, userId, code, eventCode, SoundType(sound));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"DispatcherDeskPushProvider.SendAsync failed for user {userId} in department {departmentId} ({eventCode}).");
				return false;
			}
		}

		/// <summary>The push "type" a sound travels as: its integer string, which the app maps to its bundled sound and channel.</summary>
		public static string SoundType(PushSoundTypes sound) => ((int)sound).ToString();

		private static bool IsSupportedPlatform(int platform) =>
			platform == (int)Platforms.iOS || platform == (int)Platforms.Android || platform == (int)Platforms.Web;

		private async Task<string?> GetDepartmentCodeAsync(int departmentId)
		{
			// The same code Devices/RegisterDevice prefixes the user subscriber with (cached department read).
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			return string.IsNullOrWhiteSpace(department?.Code) ? null : department.Code;
		}
	}
}
