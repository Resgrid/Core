using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Fills the destination of a new call nobody entered in a form from the department's destination rules (plan D8,
	/// <see cref="IDestinationAutoFillService"/>). Every condition must hold, else the call is left as it is: the department's
	/// destination suggestions are Suggest and fill; the call is new and came from an unattended source (<see cref="UnattendedSources"/>)
	/// or a recurring schedule; it is active or pending; it has no destination POI, no free destination and no route; it has a
	/// usable point; at least one destination rule matched it; and a destination meets the requirements. The suggestion has
	/// <see cref="TimeBudget"/> to answer; a slow or failed lookup fills nothing and never fails the save.
	/// </summary>
	public class DestinationAutoFillService : IDestinationAutoFillService
	{
		/// <summary>How long the suggestion (rules, candidates and drive times) may take before the call is saved without a destination.</summary>
		public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(8);

		/// <summary>
		/// Call sources nobody types into a call form. <see cref="CallSources.User"/> (the web forms), an unset source (0: the apps'
		/// call form over the v4 API) and <see cref="CallSources.Imported"/> (closed historical calls) are never filled; a source
		/// added later is not filled until it is listed here.
		/// </summary>
		public static readonly IReadOnlyCollection<int> UnattendedSources = new HashSet<int>
		{
			(int)CallSources.EmailImport,
			(int)CallSources.AudioImport,
			(int)CallSources.Chatbot,
			(int)CallSources.Intake,
			(int)CallSources.VoiceIntake
		};

		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly INearestDestinationService _nearestDestinationService;

		public DestinationAutoFillService(IDepartmentSettingsService departmentSettingsService, INearestDestinationService nearestDestinationService)
		{
			_departmentSettingsService = departmentSettingsService;
			_nearestDestinationService = nearestDestinationService;
		}

		public async Task<int?> ChooseAsync(Call call, CancellationToken cancellationToken = default(CancellationToken))
		{
			return (await ChooseDestinationAsync(call, cancellationToken))?.PoiId;
		}

		public async Task<DestinationAutoFillChoice> ChooseDestinationAsync(Call call, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				if (!IsEligible(call) || !TryReadPoint(call.GeoLocationData, out var latitude, out var longitude))
					return null;

				// Read first: in Suggest mode nothing is filled, so no drive times are looked up for nothing.
				var config = await _departmentSettingsService.GetDestinationSuggestionConfigAsync(call.DepartmentId);
				if (config == null || config.GetMode() != DestinationSuggestionModes.SuggestAndFill)
					return null;

				using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					budget.CancelAfter(TimeBudget);

					DestinationSuggestions suggestions;
					try
					{
						suggestions = await _nearestDestinationService.SuggestDestinationsAsync(new DestinationSuggestionRequest
						{
							DepartmentId = call.DepartmentId,
							Latitude = latitude,
							Longitude = longitude,
							CallTypeId = call.CallTypeId,
							CallType = call.Type,
							CallPriority = call.Priority,
							MaxResults = 1
						}, budget.Token);
					}
					catch (OperationCanceledException) when (budget.IsCancellationRequested)
					{
						Logging.LogInfo($"Destination auto-fill for a new call of department {call.DepartmentId} took longer than {TimeBudget.TotalSeconds:0} s; the call is saved without a destination.");
						return null;
					}

					// Drive times cut short by the budget are not a ranking to fill from.
					if (budget.IsCancellationRequested)
						return null;

					return Pick(suggestions);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Destination auto-fill for a new call of department {call?.DepartmentId} failed; the call is saved without a destination.");
				return null;
			}
		}

		/// <summary>
		/// True when the call may have its destination filled: new, from an unattended source or a recurring schedule, active or
		/// pending, and with no destination of any kind (POI, free destination, or a route whose end is the destination).
		/// </summary>
		public static bool IsEligible(Call call)
		{
			if (call == null || call.CallId != 0 || call.DepartmentId <= 0)
				return false;

			var unattended = (call.RecurringCallId.HasValue && call.RecurringCallId.Value > 0) || UnattendedSources.Contains(call.CallSource);
			if (!unattended)
				return false;

			if (call.State != (int)CallStates.Active && call.State != (int)CallStates.Pending)
				return false;

			if ((call.DestinationPoiId.HasValue && call.DestinationPoiId.Value > 0) || CallLocations.HasFreeDestination(call) ||
			    !String.IsNullOrWhiteSpace(call.DestinationW3W) || !String.IsNullOrWhiteSpace(call.RoutePlanId))
				return false;

			return true;
		}

		/// <summary>
		/// The destination to fill from a suggestion: only in Suggest and fill, only when a destination rule matched the call (set-aside
		/// rules do not count), and only the suggested destination (the best one that meets the requirements and is not closed).
		/// </summary>
		public static DestinationAutoFillChoice Pick(DestinationSuggestions suggestions)
		{
			if (suggestions == null || suggestions.Mode != DestinationSuggestionModes.SuggestAndFill)
				return null;

			var rules = suggestions.Requirements?.AppliedRules ?? new List<DestinationRule>();
			if (rules.Count == 0 || !(suggestions.SuggestedPoiId > 0))
				return null;

			var destination = (suggestions.Destinations ?? new List<NearestDestination>())
				.FirstOrDefault(x => x?.Poi != null && x.Poi.PoiId == suggestions.SuggestedPoiId.Value);
			if (destination == null)
				return null;

			return new DestinationAutoFillChoice
			{
				PoiId = destination.Poi.PoiId,
				PoiName = PoiDisplayHelper.GetDisplayName(destination.Poi, destination.Poi.Type?.Name),
				RuleNames = rules.Where(x => x != null).Select(x => x.Name).ToList()
			};
		}

		/// <summary>The system call note recorded on the call (English, as every system call note is).</summary>
		public static string BuildNoteText(string poiName)
		{
			return $"Destination set from the department's destination rules: {poiName}";
		}

		/// <summary>
		/// Reads the call's "lat,lon" point. Older rows were sometimes written with the server's culture, so besides the invariant
		/// form a comma decimal separator ("50,8,3,8") and a semicolon or space between the two numbers are accepted. False for
		/// anything else, an envelope of a protected department, or an unusable point.
		/// </summary>
		public static bool TryReadPoint(string value, out double latitude, out double longitude)
		{
			if (CallLocations.TryParsePoint(value, out latitude, out longitude))
				return true;

			latitude = 0;
			longitude = 0;

			if (String.IsNullOrWhiteSpace(value))
				return false;

			var text = value.Trim();
			var commaParts = text.Split(',');
			if (commaParts.Length == 4)
				return TryInvariant(commaParts[0] + "." + commaParts[1], commaParts[2] + "." + commaParts[3], out latitude, out longitude);

			var parts = text.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 2)
				return TryInvariant(parts[0].Replace(',', '.'), parts[1].Replace(',', '.'), out latitude, out longitude);

			return false;
		}

		private static bool TryInvariant(string latitudeText, string longitudeText, out double latitude, out double longitude)
		{
			longitude = 0;
			if (!double.TryParse(latitudeText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) ||
			    !double.TryParse(longitudeText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
			{
				latitude = 0;
				longitude = 0;
				return false;
			}

			if (CallLocations.IsValidPoint(latitude, longitude))
				return true;

			latitude = 0;
			longitude = 0;
			return false;
		}
	}
}
