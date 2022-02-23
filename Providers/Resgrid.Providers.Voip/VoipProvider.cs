using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Voip.Kazoo.Model;
using System;
using System.Threading.Tasks;

namespace Resgrid.Providers.Voip
{
	public class VoipProvider : IVoipProvider
	{
		private readonly KazooProvider _kazooProvider;

		public VoipProvider()
		{
			_kazooProvider = new KazooProvider();
		}

		public async Task<string> CreateUserIfNotExistsAsync(string voipSystemUserId, string emailAddress, UserProfile profile, int departmentId)
		{
			if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.Kazoo)
			{
				if (!String.IsNullOrWhiteSpace(voipSystemUserId))
				{
					var user = await _kazooProvider.GetUser(voipSystemUserId);

					if (user != null)
						return voipSystemUserId;
				}

				var kazooUser = new KazooCreateUserRequest();
				kazooUser.EmailAddress = emailAddress;
				kazooUser.FirstName = profile.FirstName;
				kazooUser.LastName = $"{profile.LastName} ({departmentId})";
				kazooUser.Username = $"Web{profile.UserId}";
				kazooUser.Password = RandomGenerator.GenerateRandomString(8, 12, false, false, false, true, true, false, null);

				var newUser = await _kazooProvider.CreateUser(kazooUser);

				if (newUser != null)
					return newUser.Id;
			}

			return null;
		}

		public async Task<string> CreateDeviceForUserIfNotExistsAsync(string voipSystemUserId, string voipSystemDeviceId, UserProfile profile, int departmentId)
		{
			if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.Kazoo)
			{
				if (!String.IsNullOrWhiteSpace(voipSystemDeviceId))
				{
					var user = await _kazooProvider.GetDevice(voipSystemDeviceId);

					if (user != null)
						return voipSystemDeviceId;
				}

				var kazooDevice = new KazooCreateDeviceRequest();
				kazooDevice.Name = $"{departmentId}: {profile.FirstName} {profile.LastName} SIP Device";
				kazooDevice.Username = profile.UserId.Replace("-", "");
				kazooDevice.Password = Hashing.ComputeMD5Hash($"{profile.UserId}{Config.SymmetricEncryptionConfig.InitVector}");
				kazooDevice.OwnerId = voipSystemUserId;
				kazooDevice.DeviceType = "sip_device";
				kazooDevice.Sip = new Sip();
				kazooDevice.Sip.Username = kazooDevice.Username;
				kazooDevice.Sip.Password = kazooDevice.Password;

				var newDevice = await _kazooProvider.CreateDevice(kazooDevice);

				if (newDevice != null)
					return newDevice.Id;
			}

			return null;
		}

		public async Task<Tuple<string, string>> CreateConferenceIfNotExistsAsync(string voipSystemConferenceId, int departmentId, string name, string pin, int number)
		{
			if (Config.SystemBehaviorConfig.VoipProviderType == Config.VoipProviderTypes.Kazoo)
			{
				if (!String.IsNullOrWhiteSpace(voipSystemConferenceId))
				{
					var conference = await _kazooProvider.GetConference(voipSystemConferenceId);

					if (conference != null)
						return new Tuple<string, string>(voipSystemConferenceId, "");
				}

				var kazooConf = new KazooCreateConferenceRequest();
				kazooConf.Name = $"{departmentId}:{name}";
				kazooConf.PlayEntryTone = false;
				kazooConf.PlayExitTone = false;
				kazooConf.Member = new MemberRequest();
				kazooConf.Member.JoinMuted = false;
				kazooConf.Member.Pins = new System.Collections.Generic.List<string>();
				kazooConf.Member.Pins.Add(pin);
				kazooConf.Profile = new ProfileRequest();
				kazooConf.Profile.AloneSound = "";
				kazooConf.Profile.EnterSound = "";
				kazooConf.Profile.ExitSound = "";

				var newConf = await _kazooProvider.CreateConference(kazooConf);

				if (newConf != null)
				{
					var kazooCallflow = new KazooCreateCallflowRequest();
					kazooCallflow.Name = $"{departmentId}:{name} Callflow";
					kazooCallflow.Numbers = new System.Collections.Generic.List<string>();
					kazooCallflow.Numbers.Add(number.ToString());
					kazooCallflow.Flow = new FlowRequest();
					kazooCallflow.Flow.Data = new FlowData();
					kazooCallflow.Flow.Data.Id = newConf.Id;
					kazooCallflow.Flow.Module = "conference";

					var newCallflow = await _kazooProvider.CreateCallflow(kazooCallflow);

					if (newCallflow != null)
						return new Tuple<string, string>(newConf.Id, newCallflow.Id);


					return new Tuple<string, string>(newConf.Id, "");
				}
			}

			return null;
		}

		public async Task<string> CreateOpenViduSessionAndGetToken(string sessionId)
		{
			var openViduProvider = new OpenViduProvider();
			var createSessionResult = await openViduProvider.CreateSession(sessionId);
			var createTokenResult = await openViduProvider.CreateToken(createSessionResult);

			if (createTokenResult != null)
				return createTokenResult.Token;

			return null;
		}
	}
}
