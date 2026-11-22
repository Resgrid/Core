using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Twilio
{
	/// <summary>
	/// Inbound texts for the dispatcher desk (plan D9), shared by the Twilio and SignalWire webhooks. Only senders with no member
	/// profile reach it: members keep their text commands, and dispatch-source numbers keep Text-To-Call.
	/// </summary>
	public interface IDispatcherInboundTextHandler
	{
		/// <summary>
		/// True when the desk took the text (the webhook answers with its usual empty reply); false leaves it to today's handling.
		/// </summary>
		/// <param name="departmentId">
		/// The department the texted number belongs to (its Text-To-Call number), or null for the master Resgrid number, where
		/// only a reply to an open desk conversation is taken.
		/// </param>
		Task<bool> HandleAsync(int? departmentId, string from, string to, string body, string messageSid, CancellationToken cancellationToken = default);
	}

	public class DispatcherInboundTextHandler : IDispatcherInboundTextHandler
	{
		private readonly IDispatcherDeskConfigService _dispatcherDeskConfigService;
		private readonly IDispatcherRoutingService _dispatcherRoutingService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ILimitsService _limitsService;
		private readonly INumbersService _numbersService;
		private readonly ITextCommandService _textCommandService;

		public DispatcherInboundTextHandler(IDispatcherDeskConfigService dispatcherDeskConfigService, IDispatcherRoutingService dispatcherRoutingService,
			IDepartmentSettingsService departmentSettingsService, ILimitsService limitsService, INumbersService numbersService,
			ITextCommandService textCommandService)
		{
			_dispatcherDeskConfigService = dispatcherDeskConfigService;
			_dispatcherRoutingService = dispatcherRoutingService;
			_departmentSettingsService = departmentSettingsService;
			_limitsService = limitsService;
			_numbersService = numbersService;
			_textCommandService = textCommandService;
		}

		public async Task<bool> HandleAsync(int? departmentId, string from, string to, string body, string messageSid, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(body))
				return false;

			try
			{
				// Opting out is never a desk conversation; it keeps working exactly as today.
				var command = _textCommandService.DetermineType(body)?.Type ?? TextCommandTypes.None;
				if (command == TextCommandTypes.Stop)
					return false;

				var request = new DispatcherEnqueueRequest
				{
					DepartmentId = departmentId ?? 0,
					Kind = DispatcherQueueItemKinds.Text,
					SourceAddress = DeskPhoneNumbers.ToE164(from),
					TargetAddress = DeskPhoneNumbers.ToE164(to),
					Body = body,
					ProviderMessageId = messageSid
				};

				if (!departmentId.HasValue || departmentId.Value <= 0)
				{
					// The master number: a reply to the number a dispatcher answered from. Only joining an open conversation counts
					// (DepartmentId 0 = find it in any department); HELP and anything else keep the master number's handling.
					if (command == TextCommandTypes.Help)
						return false;

					var reply = await _dispatcherRoutingService.RouteInboundMessageAsync(request, CancellationToken.None);
					return reply != null && (reply.Outcome == DispatcherRouteOutcomes.Joined || reply.Outcome == DispatcherRouteOutcomes.Duplicate);
				}

				var config = await _dispatcherDeskConfigService.GetConfigAsync(departmentId.Value);
				if (config == null || !config.Routes(DispatcherQueueItemKinds.Text))
					return false;

				// The same plan gate as every other inbound text to a department number.
				if (!await _limitsService.CanDepartmentProvisionNumberAsync(departmentId.Value))
					return false;

				// Dispatch centers sending Text-To-Call imports keep Text-To-Call.
				var dispatchNumbers = await _departmentSettingsService.GetTextToCallSourceNumbersForDepartmentAsync(departmentId.Value);
				if (!string.IsNullOrWhiteSpace(dispatchNumbers) &&
					_numbersService.DoesNumberMatchAnyPattern(dispatchNumbers.Split(',').ToList(), DeskPhoneNumbers.DigitsOnly(from)))
					return false;

				// Offered, Queued, Joined, Duplicate (a provider retry) and Overflow all mean the desk has it. On Overflow the routing
				// service has already turned the conversation into a pending call that waits in the queue (acknowledging the sender and,
				// with Enhanced AI text overflow, queueing the AI), so nothing more happens here.
				var result = await _dispatcherRoutingService.RouteInboundMessageAsync(request, CancellationToken.None);
				return (result?.Outcome ?? DispatcherRouteOutcomes.NotRouted) != DispatcherRouteOutcomes.NotRouted;
			}
			catch (Exception ex)
			{
				// The sender still gets today's handling rather than silence.
				Logging.LogException(ex, $"Dispatcher desk: inbound text {messageSid} could not be routed.");
				return false;
			}
		}
	}

	/// <summary>Phone number shapes the desk stores (E.164) and the legacy lookups expect (digits only).</summary>
	public static class DeskPhoneNumbers
	{
		public static string DigitsOnly(string number) => new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

		/// <summary>"+15551234567" from "+15551234567", "15551234567" or "+1 (555) 123-4567"; anything that is not a number unchanged.</summary>
		public static string ToE164(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				return number;

			var trimmed = number.Trim();
			var digits = DigitsOnly(trimmed);
			if (digits.Length == 0 || trimmed.Any(c => char.IsLetter(c)))
				return trimmed;

			return "+" + digits;
		}
	}
}
