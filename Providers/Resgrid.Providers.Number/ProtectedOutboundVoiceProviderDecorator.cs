using System;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Providers.NumberProvider
{
	/// <summary>
	/// Outbound-boundary net for voice dispatch (ADP plan section 7.5, queue side). The provider is
	/// handed the Call entity and builds the spoken prompt from it, so an unsanitized call would be
	/// read aloud as base64 — a phone call that is both a disclosure and useless to the responder.
	///
	/// Unlike the text channels this does not scrub field by field: a prompt full of "REDACTED" is
	/// no better than one full of ciphertext. It re-runs the projection the caller should have run,
	/// which yields the properly worded generic dispatch, and logs that the caller skipped it.
	/// </summary>
	public class ProtectedOutboundVoiceProviderDecorator : IOutboundVoiceProvider
	{
		private readonly IOutboundVoiceProvider _inner;
		private readonly IProtectedProjectionService _protectedProjectionService;

		public ProtectedOutboundVoiceProviderDecorator(IOutboundVoiceProvider inner,
			IProtectedProjectionService protectedProjectionService)
		{
			_inner = inner;
			_protectedProjectionService = protectedProjectionService;
		}

		public async Task<bool> CommunicateCallAsync(string phoneNumber, UserProfile profile, Call call)
		{
			try
			{
				if (call != null && HasEnvelopedField(call))
				{
					Logging.LogError($"ADP outbound net caught an unsanitized voice dispatch for department {call.DepartmentId} " +
						$"(call {call.CallId}); rebuilding it through the protected projection. The caller is missing its safe projection.");

					call = await _protectedProjectionService.BuildNotificationSafeCallAsync(call.DepartmentId, call,
						ProtectedDataEgressChannel.Voice);
				}
			}
			catch (Exception ex)
			{
				// A failure here must not silence a dispatch call, but it must not let ciphertext be
				// spoken either — fall back to the value-free shell.
				Logging.LogException(ex, "ProtectedOutboundVoiceProviderDecorator failed while sanitizing a voice dispatch");
				call = BuildMinimalShell(call);
			}

			return await _inner.CommunicateCallAsync(phoneNumber, profile, call);
		}

		public Task<bool> SendVoiceVerificationCallAsync(string phoneNumber, string userId, int contactType)
			=> _inner.SendVoiceVerificationCallAsync(phoneNumber, userId, contactType);

		public Task<bool> SendCommunicationTestCallAsync(string phoneNumber, string responseToken)
			=> _inner.SendCommunicationTestCallAsync(phoneNumber, responseToken);

		private static bool HasEnvelopedField(Call call)
		{
			return ProtectedDataEnvelope.HasEnvelopePrefix(call.Name) ||
				   ProtectedDataEnvelope.HasEnvelopePrefix(call.NatureOfCall) ||
				   ProtectedDataEnvelope.HasEnvelopePrefix(call.Address) ||
				   ProtectedDataEnvelope.HasEnvelopePrefix(call.Type) ||
				   ProtectedDataEnvelope.HasEnvelopePrefix(call.Notes);
		}

		private static Call BuildMinimalShell(Call call)
		{
			if (call == null)
				return null;

			return new Call
			{
				CallId = call.CallId,
				DepartmentId = call.DepartmentId,
				Department = call.Department,
				Number = call.Number,
				Priority = call.Priority,
				CallPriority = call.CallPriority,
				State = call.State,
				LoggedOn = call.LoggedOn,
				Name = string.IsNullOrWhiteSpace(call.Number) ? "Protected dispatch" : call.Number
			};
		}
	}
}
