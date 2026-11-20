using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// A department's intake channels (M0323). Addresses are normalized per transport (IntakeChannelAddresses) and unique per
	/// transport + address across every department (one live channel may listen on an address); the partial unique index
	/// UX_IntakeChannels_Address backs the check. Webhook channels get a generated token as their address and a signing secret
	/// encrypted with the system key (IEncryptionService): the API host verifies it, and ASP.NET data protection keys are only
	/// persisted by the web host (Startup AddDataProtection), so a data-protection payload could not be read there.
	/// </summary>
	public class IntakeChannelsService : IIntakeChannelsService
	{
		private const string SecretPrefix = "enc1:";

		private readonly IIntakeChannelsRepository _channels;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IEncryptionService _encryption;
		private readonly TimeProvider _clock;

		public IntakeChannelsService(IIntakeChannelsRepository channels, IDepartmentSettingsService departmentSettings, IEncryptionService encryption, TimeProvider clock)
		{
			_channels = channels;
			_departmentSettings = departmentSettings;
			_encryption = encryption;
			_clock = clock;
		}

		public async Task<List<IntakeChannel>> GetChannelsForDepartmentAsync(int departmentId)
		{
			var channels = await _channels.GetLiveForDepartmentAsync(departmentId) ?? new List<IntakeChannel>();
			return channels.Where(c => c.DepartmentId == departmentId && !c.IsDeleted)
				.OrderBy(c => c.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase).ToList();
		}

		public async Task<IntakeChannel> GetChannelByIdAsync(int departmentId, string intakeChannelId)
		{
			if (string.IsNullOrWhiteSpace(intakeChannelId))
				return null;

			var channel = await _channels.GetByIdAsync(intakeChannelId);
			return channel != null && channel.DepartmentId == departmentId && !channel.IsDeleted ? channel : null;
		}

		public async Task<IntakeChannel> ResolveChannelAsync(IntakeChannelTypes channelType, string address)
		{
			var normalized = NormalizeAddress(channelType, address);
			if (normalized == null)
				return null;

			var channel = await _channels.GetLiveByAddressAsync((int)channelType, normalized);
			return channel != null && channel.IsEnabled && !channel.IsDeleted ? channel : null;
		}

		public async Task<IntakeChannel> SaveChannelAsync(IntakeChannel channel, string userId, CancellationToken cancellationToken = default)
		{
			if (channel == null)
				throw new ArgumentNullException(nameof(channel));

			var now = _clock.GetUtcNow().UtcDateTime;
			if (channel.DepartmentId <= 0)
				throw new IntakeChannelValidationException("NotFound");
			if (!Enum.IsDefined(typeof(IntakeChannelTypes), channel.ChannelType))
				throw new IntakeChannelValidationException("InvalidType");
			if (!Enum.IsDefined(typeof(IntakeProcessingModes), channel.ProcessingMode))
				throw new IntakeChannelValidationException("InvalidMode");
			if (channel.TemplateFormat.HasValue && !Enum.IsDefined(typeof(CallEmailTypes), channel.TemplateFormat.Value))
				throw new IntakeChannelValidationException("InvalidTemplate");

			var name = (channel.Name ?? string.Empty).Trim();
			if (name.Length == 0)
				throw new IntakeChannelValidationException("NameRequired");
			if (name.Length > IntakeChannel.MaxNameLength)
				name = name.Substring(0, IntakeChannel.MaxNameLength);

			IntakeChannel target;
			if (string.IsNullOrWhiteSpace(channel.IntakeChannelId))
			{
				target = new IntakeChannel
				{
					DepartmentId = channel.DepartmentId,
					ChannelType = channel.ChannelType,
					CreatedByUserId = userId,
					CreatedOn = now,
					IsEnabled = channel.IsEnabled
				};

				// The webhook URL token is generated, never typed; the secret is set by RotateWebhookSecretAsync and shown once.
				target.Address = channel.ChannelType == (int)IntakeChannelTypes.Webhook
					? IntakeChannelAddresses.NewWebhookToken()
					: await ValidAddressAsync((IntakeChannelTypes)channel.ChannelType, channel.Address, channel.DepartmentId, null);
			}
			else
			{
				target = await GetChannelByIdAsync(channel.DepartmentId, channel.IntakeChannelId);
				if (target == null)
					throw new IntakeChannelValidationException("NotFound");
				if (target.ChannelType != channel.ChannelType)
					throw new IntakeChannelValidationException("InvalidType");

				if (target.ChannelType != (int)IntakeChannelTypes.Webhook)
					target.Address = await ValidAddressAsync((IntakeChannelTypes)target.ChannelType, channel.Address, target.DepartmentId, target.IntakeChannelId);

				target.IsEnabled = channel.IsEnabled;
				target.UpdatedByUserId = userId;
				target.UpdatedOn = now;
			}

			target.Name = name;
			target.ProcessingMode = channel.ProcessingMode;
			target.TemplateFormat = channel.TemplateFormat;
			target.AllowedSenders = NormalizeSenders(channel.AllowedSenders);
			target.DefaultPriority = channel.DefaultPriority;
			target.DefaultCallTypeId = channel.DefaultCallTypeId;
			target.SendAcknowledgement = channel.SendAcknowledgement;

			return await _channels.SaveOrUpdateAsync(target, cancellationToken);
		}

		public async Task<bool> DeleteChannelAsync(int departmentId, string intakeChannelId, string userId, CancellationToken cancellationToken = default)
		{
			var channel = await GetChannelByIdAsync(departmentId, intakeChannelId);
			if (channel == null)
				return false;

			channel.IsDeleted = true;
			channel.IsEnabled = false;
			// A deleted webhook channel's URL must stop verifying even if the row is ever restored by hand.
			channel.WebhookSecretProtected = null;
			channel.UpdatedByUserId = userId;
			channel.UpdatedOn = _clock.GetUtcNow().UtcDateTime;
			await _channels.SaveOrUpdateAsync(channel, cancellationToken);
			return true;
		}

		public async Task<IntakeChannel> EnsureChannelAsync(int departmentId, IntakeChannelTypes channelType, string address, string name, string userId,
			CancellationToken cancellationToken = default)
		{
			var normalized = NormalizeAddress(channelType, address);
			if (normalized == null || channelType == IntakeChannelTypes.Webhook)
				throw new IntakeChannelValidationException("InvalidAddress");

			var now = _clock.GetUtcNow().UtcDateTime;
			var existing = await _channels.GetLiveByAddressAsync((int)channelType, normalized);
			if (existing != null)
			{
				if (existing.DepartmentId != departmentId)
					throw new IntakeChannelValidationException("AddressInUse");
				if (existing.IsEnabled)
					return existing;

				existing.IsEnabled = true;
				existing.UpdatedByUserId = userId;
				existing.UpdatedOn = now;
				return await _channels.SaveOrUpdateAsync(existing, cancellationToken);
			}

			var channelName = string.IsNullOrWhiteSpace(name) ? channelType + " " + normalized : name.Trim();
			return await _channels.SaveOrUpdateAsync(new IntakeChannel
			{
				DepartmentId = departmentId,
				ChannelType = (int)channelType,
				Address = normalized,
				Name = channelName.Length > IntakeChannel.MaxNameLength ? channelName.Substring(0, IntakeChannel.MaxNameLength) : channelName,
				ProcessingMode = (int)IntakeProcessingModes.Template,
				IsEnabled = true,
				CreatedByUserId = userId,
				CreatedOn = now
			}, cancellationToken);
		}

		public async Task DisableChannelsForAddressAsync(int departmentId, IntakeChannelTypes channelType, string address, CancellationToken cancellationToken = default)
		{
			var normalized = NormalizeAddress(channelType, address);
			if (normalized == null)
				return;

			var existing = await _channels.GetLiveByAddressAsync((int)channelType, normalized);
			if (existing == null || existing.DepartmentId != departmentId || !existing.IsEnabled)
				return;

			existing.IsEnabled = false;
			existing.UpdatedOn = _clock.GetUtcNow().UtcDateTime;
			await _channels.SaveOrUpdateAsync(existing, cancellationToken);
		}

		public string NormalizeAddress(IntakeChannelTypes channelType, string address) => IntakeChannelAddresses.Normalize(channelType, address);

		public async Task<IntakeChannel> SetEnabledAsync(int departmentId, string intakeChannelId, bool enabled, string userId, CancellationToken cancellationToken = default)
		{
			var channel = await GetChannelByIdAsync(departmentId, intakeChannelId);
			if (channel == null)
				return null;
			if (channel.IsEnabled == enabled)
				return channel;

			// Re-enabling takes the address back, so it must still be free (another department may have claimed it meanwhile).
			if (enabled)
			{
				var holder = await _channels.GetLiveByAddressAsync(channel.ChannelType, channel.Address);
				if (holder != null && !string.Equals(holder.IntakeChannelId, channel.IntakeChannelId, StringComparison.OrdinalIgnoreCase))
					throw new IntakeChannelValidationException("AddressInUse");
			}

			channel.IsEnabled = enabled;
			channel.UpdatedByUserId = userId;
			channel.UpdatedOn = _clock.GetUtcNow().UtcDateTime;
			return await _channels.SaveOrUpdateAsync(channel, cancellationToken);
		}

		public async Task<string> RotateWebhookSecretAsync(int departmentId, string intakeChannelId, string userId, CancellationToken cancellationToken = default)
		{
			var channel = await GetChannelByIdAsync(departmentId, intakeChannelId);
			if (channel == null || channel.ChannelType != (int)IntakeChannelTypes.Webhook)
				return null;

			var secret = IntakeChannelAddresses.NewWebhookSecret();
			channel.WebhookSecretProtected = SecretPrefix + _encryption.Encrypt(secret);
			channel.UpdatedByUserId = userId;
			channel.UpdatedOn = _clock.GetUtcNow().UtcDateTime;
			await _channels.SaveOrUpdateAsync(channel, cancellationToken);
			return secret;
		}

		public string GetWebhookSecret(IntakeChannel channel)
		{
			var stored = channel?.WebhookSecretProtected;
			if (string.IsNullOrWhiteSpace(stored) || !stored.StartsWith(SecretPrefix, StringComparison.Ordinal))
				return null;

			try
			{
				var secret = _encryption.Decrypt(stored.Substring(SecretPrefix.Length));
				return string.IsNullOrWhiteSpace(secret) ? null : secret;
			}
			catch (Exception ex)
			{
				// A rotated system key or a hand-edited row: the webhook fails closed (401) until the admin rotates the secret.
				Framework.Logging.LogException(ex, $"Intake webhook secret for channel {channel.IntakeChannelId} could not be read.");
				return null;
			}
		}

		private async Task<string> ValidAddressAsync(IntakeChannelTypes channelType, string address, int departmentId, string intakeChannelId)
		{
			var normalized = NormalizeAddress(channelType, address);
			if (normalized == null)
				throw new IntakeChannelValidationException("InvalidAddress");

			var holder = await _channels.GetLiveByAddressAsync((int)channelType, normalized);
			if (holder != null && !string.Equals(holder.IntakeChannelId, intakeChannelId, StringComparison.OrdinalIgnoreCase))
				throw new IntakeChannelValidationException("AddressInUse");

			// An email intake address shares the dispatch domain with departments' dispatch addresses (setting 15): one local
			// part can only mean one thing, so an address some department dispatches from cannot also be an intake address.
			if (channelType == IntakeChannelTypes.Email && (await _departmentSettings.GetDepartmentIdForDispatchEmailAsync(normalized)).HasValue)
				throw new IntakeChannelValidationException("AddressInUse");

			return normalized;
		}

		/// <summary>One pattern per entry, trimmed, blank and duplicate entries dropped; null when none remain (anyone).</summary>
		public static string NormalizeSenders(string senders)
		{
			if (string.IsNullOrWhiteSpace(senders))
				return null;

			var patterns = senders.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim()).Where(s => s.Length > 0 && s.Length <= 256).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToList();
			return patterns.Count == 0 ? null : string.Join(",", patterns);
		}
	}
}
