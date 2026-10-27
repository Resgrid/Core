using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// Self-service links (plan §9.28, D17). A link names one participant and its purposes; only the SHA-256 hash of its 256-bit token
	/// is stored, so the token cannot be recovered from the database. The page shows that participant's own support information —
	/// status, check-in code, positions held, meal windows — and lodging only after a one-time code when the department requires it
	/// (setting 91). Protected participant fields are never read here.
	/// </summary>
	public sealed class IncidentSelfServiceService : IIncidentSelfServiceService
	{
		private const int CodeMinutes = 10;
		private const int MaxCodeAttempts = 5;
		private static readonly int[] HoldingNights = { (int)IncidentLodgingAssignmentStatuses.Held, (int)IncidentLodgingAssignmentStatuses.Confirmed, (int)IncidentLodgingAssignmentStatuses.Occupied };

		private readonly IIncidentLookupRepository _lookup;
		private readonly IIncidentBackOfficeAccessService _access;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly IIncidentLedgerRepository<IncidentSelfServiceLink> _links;
		private readonly IIncidentLedgerService<IncidentSelfServiceLink> _linkService;
		private readonly IIncidentLedgerRepository<IncidentParticipant> _participants;
		private readonly IIncidentLedgerRepository<IncidentActivation> _activations;
		private readonly IIncidentLedgerRepository<IncidentPositionAssignment> _assignments;
		private readonly IIncidentLedgerRepository<IncidentPositionDefinition> _positions;
		private readonly IIncidentLedgerRepository<IncidentMealPlan> _meals;
		private readonly IIncidentLedgerRepository<IncidentLodgingAssignment> _lodging;
		private readonly IIncidentLedgerRepository<IncidentLodgingProperty> _properties;
		private readonly IIncidentNotificationRoutingService _routing;
		private readonly IDepartmentsService _departments;
		private readonly Model.Providers.ICacheProvider _cache;

		public IncidentSelfServiceService(IIncidentLookupRepository lookup, IIncidentBackOfficeAccessService access, IIncidentBackOfficeSettingsService settings,
			IIncidentLedgerRepository<IncidentSelfServiceLink> links, IIncidentLedgerService<IncidentSelfServiceLink> linkService,
			IIncidentLedgerRepository<IncidentParticipant> participants, IIncidentLedgerRepository<IncidentActivation> activations,
			IIncidentLedgerRepository<IncidentPositionAssignment> assignments, IIncidentLedgerRepository<IncidentPositionDefinition> positions,
			IIncidentLedgerRepository<IncidentMealPlan> meals, IIncidentLedgerRepository<IncidentLodgingAssignment> lodging,
			IIncidentLedgerRepository<IncidentLodgingProperty> properties, IIncidentNotificationRoutingService routing, IDepartmentsService departments,
			Model.Providers.ICacheProvider cache)
		{
			_lookup = lookup;
			_access = access;
			_settings = settings;
			_links = links;
			_linkService = linkService;
			_participants = participants;
			_activations = activations;
			_assignments = assignments;
			_positions = positions;
			_meals = meals;
			_lodging = lodging;
			_properties = properties;
			_routing = routing;
			_departments = departments;
			_cache = cache;
		}

		internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

		private static string NewToken()
		{
			var bytes = RandomNumberGenerator.GetBytes(32);
			return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		public async Task<IncidentSelfServiceIssue> IssueAsync(IncidentActor actor, string participantId, IncidentSelfServicePurposes purposes, CancellationToken cancellationToken = default)
		{
			var participant = await _participants.GetForDepartmentAsync(actor.DepartmentId, participantId, false, cancellationToken);
			if (participant == null)
				throw new IncidentRuleException("ParticipantNotFound");
			var policy = await _settings.GetAsync<IncidentSelfServicePolicy>(actor.DepartmentId);
			var end = new[] { participant.TentativeReleaseOn, participant.TimeoutOn, DateTime.UtcNow }.Where(d => d.HasValue).Max(d => d.Value);
			var token = NewToken();
			var link = await _linkService.SaveAsync(actor, new IncidentSelfServiceLink
			{
				IncidentActivationId = participant.IncidentActivationId,
				SubjectType = (int)IncidentSelfServiceSubjects.Participant,
				SubjectId = participant.IncidentParticipantId,
				TokenHash = Hash(token),
				Purposes = (int)(purposes | IncidentSelfServicePurposes.View),
				ExpiresOn = end.AddDays(Math.Max(1, policy.LinkLifetimeDays))
			}, null, cancellationToken);
			return new IncidentSelfServiceIssue { LinkId = link.IncidentSelfServiceLinkId, Token = token, ExpiresOn = link.ExpiresOn };
		}

		public async Task RevokeAsync(IncidentActor actor, string linkId, CancellationToken cancellationToken = default)
		{
			var link = await _linkService.GetAsync(actor, linkId, false, cancellationToken);
			if (link == null)
				throw new IncidentRuleException("RecordNotFound");
			if (link.RevokedOn.HasValue)
				return;
			link.RevokedOn = DateTime.UtcNow;
			link.RevokedByUserId = actor.UserId;
			await _linkService.SaveAsync(actor, link, link.RowVersion, cancellationToken);
		}

		/// <summary>The live link and its participant, or null for anything that should look like an unknown token.</summary>
		private async Task<(IncidentSelfServiceLink Link, IncidentParticipant Participant, IncidentActivation Activation)?> ResolveAsync(string token, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(token) || token.Length < 20 || token.Length > 100)
				return null;
			var found = await _lookup.FindSelfServiceLinkAsync(Hash(token), cancellationToken);
			if (found == null || !await _access.IsEnabledAsync(found.Value.DepartmentId))
				return null;
			var link = await _links.GetForDepartmentAsync(found.Value.DepartmentId, found.Value.LinkId, false, cancellationToken);
			if (link == null || link.RevokedOn.HasValue || link.ExpiresOn <= DateTime.UtcNow || link.SubjectType != (int)IncidentSelfServiceSubjects.Participant)
				return null;
			var participant = await _participants.GetForDepartmentAsync(link.DepartmentId, link.SubjectId, false, cancellationToken);
			var activation = participant == null ? null : await _activations.GetForDepartmentAsync(link.DepartmentId, participant.IncidentActivationId, false, cancellationToken);
			if (participant == null || activation == null || activation.Status == (int)IncidentActivationStatuses.Sealed)
				return null;
			return (link, participant, activation);
		}

		public async Task<IncidentSelfServicePage> GetPageAsync(string token, CancellationToken cancellationToken = default)
		{
			var resolved = await ResolveAsync(token, cancellationToken);
			if (resolved == null)
				return new IncidentSelfServicePage();
			var (link, participant, activation) = resolved.Value;
			var departmentId = link.DepartmentId;
			var now = DateTime.UtcNow;

			link.LastUsedOn = now;
			link.UseCount++;
			await _links.UpdateVersionedAsync(link, link.RowVersion, null, cancellationToken);

			var policy = await _settings.GetAsync<IncidentSelfServicePolicy>(departmentId);
			var page = new IncidentSelfServicePage
			{
				Available = true,
				DepartmentName = (await _departments.GetDepartmentByIdAsync(departmentId))?.Name,
				IncidentName = activation.Name,
				ShortCode = activation.ShortCode,
				IsExercise = activation.IsExercise,
				TimeZoneId = activation.TimeZoneId,
				ParticipantName = participant.DisplayName,
				SupportStatus = participant.SupportStatus,
				CheckInCode = participant.CheckInCode,
				ExpectedArrivalOn = participant.ExpectedArrivalOn,
				CheckedInOn = participant.CheckedInOn,
				ExpiresOn = link.ExpiresOn
			};

			var query = new IncidentLedgerQuery { ActivationIds = new[] { activation.IncidentActivationId }, Take = 200 };
			var open = (await _assignments.ListAsync(departmentId, query, cancellationToken))
				.Where(a => (string.Equals(a.IncidentParticipantId, participant.IncidentParticipantId, StringComparison.OrdinalIgnoreCase)
					|| (participant.UserId != null && string.Equals(a.UserId, participant.UserId, StringComparison.OrdinalIgnoreCase)))
					&& (!a.EndOn.HasValue || a.EndOn > now)).ToList();
			if (open.Count > 0)
				page.Positions = (await _positions.GetByIdsAsync(departmentId, open.Select(a => a.IncidentPositionDefinitionId), cancellationToken)).Select(p => p.Name).ToList();

			if (participant.NeedsMeals)
				page.Meals = (await _meals.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activation.IncidentActivationId }, Take = 50 }
					.Between("WindowEnd", now, now.AddDays(2)).Order("WindowStart"), cancellationToken))
					.Where(m => m.Status is (int)IncidentMealPlanStatuses.Planned or (int)IncidentMealPlanStatuses.Active)
					.Select(m => new IncidentSelfServiceMeal { SiteName = m.SiteName, MealType = m.MealType, WindowStart = m.WindowStart, WindowEnd = m.WindowEnd }).ToList();

			if ((link.Purposes & (int)IncidentSelfServicePurposes.Lodging) != 0)
			{
				var verified = link.VerifiedOn.HasValue;
				page.LodgingNeedsCode = policy.RequireOneTimeCodeForLodging && !verified;
				page.CanSendCode = page.LodgingNeedsCode && !string.IsNullOrWhiteSpace(participant.UserId);
				page.LodgingAllowed = !page.LodgingNeedsCode;
				if (page.LodgingAllowed)
				{
					var stays = (await _lodging.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activation.IncidentActivationId }, Take = 20 }
						.Where("IncidentParticipantId", participant.IncidentParticipantId), cancellationToken))
						.Where(a => HoldingNights.Contains(a.Status) && a.LastNight >= now.Date).ToList();
					var properties = (await _properties.GetByIdsAsync(departmentId, stays.Select(s => s.IncidentLodgingPropertyId), cancellationToken))
						.ToDictionary(p => p.IncidentLodgingPropertyId, StringComparer.OrdinalIgnoreCase);
					page.Lodging = stays.OrderBy(s => s.FirstNight).Select(s => properties.TryGetValue(s.IncidentLodgingPropertyId, out var p)
						? new IncidentSelfServiceLodging { PropertyName = p.Name, Address = p.Address, CheckInHours = p.CheckInHours, FirstNight = s.FirstNight, LastNight = s.LastNight }
						: new IncidentSelfServiceLodging { FirstNight = s.FirstNight, LastNight = s.LastNight }).ToList();
				}
			}
			return page;
		}

		public async Task<bool> SendCodeAsync(string token, CancellationToken cancellationToken = default)
		{
			var resolved = await ResolveAsync(token, cancellationToken);
			if (resolved == null || string.IsNullOrWhiteSpace(resolved.Value.Participant.UserId))
				return false;
			var (link, participant, activation) = resolved.Value;
			var code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6", CultureInfo.InvariantCulture);
			link.OneTimeCodeHash = Hash(link.IncidentSelfServiceLinkId + ":" + code);
			link.OneTimeCodeExpiresOn = DateTime.UtcNow.AddMinutes(CodeMinutes);
			await _links.UpdateVersionedAsync(link, link.RowVersion, null, cancellationToken);
			return await _routing.NotifyUserAsync(IncidentActor.System(link.DepartmentId), activation, EventTypes.IncidentLodgingAssignment, participant.UserId,
				culture => IncidentNoticeText.Format(culture, "Notice.SelfServiceCode", code, CodeMinutes), cancellationToken);
		}

		public async Task<bool> VerifyCodeAsync(string token, string code, CancellationToken cancellationToken = default)
		{
			var resolved = await ResolveAsync(token, cancellationToken);
			if (resolved == null || string.IsNullOrWhiteSpace(code))
				return false;
			var link = resolved.Value.Link;
			if (link.OneTimeCodeHash == null || !link.OneTimeCodeExpiresOn.HasValue || link.OneTimeCodeExpiresOn <= DateTime.UtcNow)
				return false;
			// A code survives five wrong guesses at most; then it is withdrawn and a new one must be sent.
			var attempts = await _cache.IncrementAsync("ibo-selfservice-code:" + link.IncidentSelfServiceLinkId + ":" + link.OneTimeCodeHash.Substring(0, 12), TimeSpan.FromMinutes(CodeMinutes + 1));
			if (attempts > MaxCodeAttempts)
			{
				link.OneTimeCodeHash = null;
				link.OneTimeCodeExpiresOn = null;
				await _links.UpdateVersionedAsync(link, link.RowVersion, null, cancellationToken);
				return false;
			}
			var expected = Encoding.ASCII.GetBytes(link.OneTimeCodeHash);
			var actual = Encoding.ASCII.GetBytes(Hash(link.IncidentSelfServiceLinkId + ":" + code.Trim()));
			if (!CryptographicOperations.FixedTimeEquals(expected, actual))
				return false;
			link.VerifiedOn = DateTime.UtcNow;
			link.OneTimeCodeHash = null;
			link.OneTimeCodeExpiresOn = null;
			await _links.UpdateVersionedAsync(link, link.RowVersion, null, cancellationToken);
			return true;
		}
	}
}
