using System;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.NumberProvider
{
	/// <summary>
	/// Outbound-boundary net for SMS (ADP plan section 7.5, queue side). Every text the platform
	/// sends goes through <see cref="ITextMessageProvider"/>, so this covers dispatch, notifications,
	/// chatbot replies and worker output in one place.
	///
	/// An SMS is the worst channel to leak into: it is delivered to a carrier, stored on a handset,
	/// and cannot be recalled. It is also the worst channel to block — a scrubbed dispatch still
	/// tells a responder to open the app, an unsent one tells them nothing. So this scrubs and logs
	/// rather than refusing.
	/// </summary>
	public class ProtectedTextMessageProviderDecorator : ITextMessageProvider
	{
		private readonly ITextMessageProvider _inner;

		public ProtectedTextMessageProviderDecorator(ITextMessageProvider inner)
		{
			_inner = inner;
		}

		public async Task<bool> SendTextMessage(string number, string message, string departmentNumber,
			MobileCarriers carrier, int departmentId, bool forceGateway = false, bool isCall = false,
			int maxLengthOverride = 0)
		{
			try
			{
				if (ProtectedOutboundGuard.MightContainEnvelope(message))
				{
					message = ProtectedOutboundGuard.Scrub(message, out var scrubbed);

					if (scrubbed > 0)
					{
						// No number, no content — the department and channel are what identify the
						// broken path.
						Logging.LogError($"ADP outbound net scrubbed {scrubbed} enveloped value(s) from an SMS for department {departmentId} " +
							$"({(isCall ? "dispatch" : "notification")}). A notification path is missing its protected projection.");
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ProtectedTextMessageProviderDecorator failed while sanitizing an outbound SMS for department {departmentId}");
			}

			return await _inner.SendTextMessage(number, message, departmentNumber, carrier, departmentId,
				forceGateway, isCall, maxLengthOverride);
		}
	}
}
