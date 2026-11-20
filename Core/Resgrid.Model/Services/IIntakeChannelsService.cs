using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// A department's intake channels (M0323, call-intake-and-ai-destinations-plan.md §4): the addresses whose messages become
	/// pending calls. Implemented by workstream C; the Dedicated Number add-on (workstream D) uses
	/// <see cref="EnsureChannelAsync"/> / <see cref="DisableChannelsForAddressAsync"/> when it provisions or releases a number.
	/// </summary>
	public interface IIntakeChannelsService
	{
		/// <summary>The department's channels (deleted ones excluded), by name.</summary>
		Task<List<IntakeChannel>> GetChannelsForDepartmentAsync(int departmentId);

		/// <summary>The department's channel by id, else null.</summary>
		Task<IntakeChannel> GetChannelByIdAsync(int departmentId, string intakeChannelId);

		/// <summary>The enabled channel listening on this transport and (normalized) address, else null.</summary>
		Task<IntakeChannel> ResolveChannelAsync(IntakeChannelTypes channelType, string address);

		/// <summary>Creates or updates a channel (address normalized, uniqueness per transport + address enforced).</summary>
		Task<IntakeChannel> SaveChannelAsync(IntakeChannel channel, string userId, CancellationToken cancellationToken = default);

		/// <summary>Soft-deletes the department's channel; false when it does not exist.</summary>
		Task<bool> DeleteChannelAsync(int departmentId, string intakeChannelId, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The department's channel on this transport and address, created (enabled, Template mode) when missing and re-enabled
		/// when disabled. Idempotent; used when a dedicated number is provisioned (SMS and Voice channels).
		/// </summary>
		Task<IntakeChannel> EnsureChannelAsync(int departmentId, IntakeChannelTypes channelType, string address, string name, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>Disables the department's channels on this transport and address (number released or lapsed).</summary>
		Task DisableChannelsForAddressAsync(int departmentId, IntakeChannelTypes channelType, string address, CancellationToken cancellationToken = default);

		/// <summary>The address as channels store it for the transport (E.164 numbers, lower-cased email local parts, …).</summary>
		string NormalizeAddress(IntakeChannelTypes channelType, string address);

		/// <summary>Turns the department's channel on or off; null when it does not exist.</summary>
		Task<IntakeChannel> SetEnabledAsync(int departmentId, string intakeChannelId, bool enabled, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Webhook channels: generates and stores a new signing secret (the old one stops working at once) and returns it in clear,
		/// the only time it is ever shown. Null when the channel is not the department's webhook channel.
		/// </summary>
		Task<string> RotateWebhookSecretAsync(int departmentId, string intakeChannelId, string userId, CancellationToken cancellationToken = default);

		/// <summary>The webhook channel's signing secret for verification; null when none is set or it cannot be read.</summary>
		string GetWebhookSecret(IntakeChannel channel);
	}

	/// <summary>A channel that cannot be saved; <see cref="Code"/> names the problem for the admin page.</summary>
	public sealed class IntakeChannelValidationException : System.InvalidOperationException
	{
		/// <summary>"InvalidAddress", "AddressInUse", "InvalidType", "InvalidMode", "InvalidTemplate", "NameRequired", "NotFound".</summary>
		public string Code { get; }

		public IntakeChannelValidationException(string code) : base("Intake channel: " + code)
		{
			Code = code;
		}
	}
}
