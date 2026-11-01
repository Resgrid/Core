using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Communications
{
	/// <summary>
	/// voice.ptt (plan §7.5): the Voice page's channel list (open channels; closed on-demand incident channels dropped) and
	/// GetCurrentUtilizationForLiveKit's seats. Utilization reads billing and LiveKit; when either fails the channels still
	/// show with UtilizationAvailable = false.
	/// </summary>
	[DashboardWidget("voice.ptt")]
	public class VoicePttWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IVoiceService _voiceService;

		public VoicePttWidgetProvider(IVoiceService voiceService)
		{
			_voiceService = voiceService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var data = new VoicePttWidgetData();
			var voice = await _voiceService.GetVoiceSettingsForDepartmentAsync(context.DepartmentId);

			if (voice?.Channels != null)
			{
				data.Channels = voice.Channels
					.Where(c => c != null && c.DepartmentId == context.DepartmentId && !c.ClosedOn.HasValue)
					.OrderByDescending(c => c.IsDefault)
					.ThenBy(c => c.IsOnDemand)
					.ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
					.Select(c => new VoicePttChannel
					{
						DepartmentVoiceChannelId = c.DepartmentVoiceChannelId,
						Name = c.Name,
						IsDefault = c.IsDefault,
						IsOnDemand = c.IsOnDemand,
						CallId = c.CallId,
						ConferenceNumber = c.ConferenceNumber
					})
					.ToList();
			}

			try
			{
				var utilization = await _voiceService.GetCurrentUtilizationForLiveKit(context.DepartmentId);
				if (utilization != null)
				{
					data.UtilizationAvailable = true;
					data.SeatLimit = utilization.SeatLimit;
					data.CurrentlyActive = utilization.CurrentlyActive;
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dashboard voice.ptt utilization unavailable for department {context.DepartmentId}");
			}

			return data;
		}
	}
}
