using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;

namespace Resgrid.Services.ContentSets
{
	/// <summary>
	/// Notices of sets and sponsorships (round 10 A3; EventTypes 83-85): an update arrived in a subscriber's inbox, a proposal reached a publisher,
	/// a publisher answered a proposal, a sponsorship started or ended, an LTI tool needs registering. Sent to the department's active admins
	/// (unless setting 151 turns them off) and the current members of the roles setting 151 names, through the standard notification path,
	/// each in the recipient's own language (Areas/User/ContentSets resx), never while the department may not transmit. A notice never undoes
	/// what it reports.
	/// </summary>
	public class ContentSetNotifier : IContentSetNotifier
	{
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.ContentSets.ContentSets", typeof(SupportedLocales).Assembly);

		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentSettingsService _settings;
		private readonly IPersonnelRolesService _roles;

		public ContentSetNotifier(Lazy<ICommunicationService> communication, IDepartmentsService departments, IUserProfileService profiles, IDepartmentSettingsService settings,
			IPersonnelRolesService roles)
		{
			_communication = communication;
			_departments = departments;
			_profiles = profiles;
			_settings = settings;
			_roles = roles;
		}

		public enum Kinds
		{
			UpdateArrived = 0,
			ProposalReceived = 1,
			ProposalAnswered = 2,
			SponsorshipStarted = 3,
			SponsorshipEnded = 4,
			ToolRegistration = 5
		}

		/// <summary>The EventTypes value of a notice kind (83, 84 or 85).</summary>
		public static EventTypes EventTypeOf(Kinds kind) =>
			kind == Kinds.UpdateArrived || kind == Kinds.ToolRegistration ? EventTypes.ContentSetUpdateNotice
			: kind == Kinds.SponsorshipStarted || kind == Kinds.SponsorshipEnded ? EventTypes.LmsSponsorshipNotice
			: EventTypes.ContentSetProposalNotice;

		/// <summary>The push event code: "CU:" an inbox update, "CP:" a proposal, "SP:" a sponsorship.</summary>
		public static string EventCode(Kinds kind, int id) =>
			(kind == Kinds.UpdateArrived || kind == Kinds.ToolRegistration ? "CU:" : kind == Kinds.SponsorshipStarted || kind == Kinds.SponsorshipEnded ? "SP:" : "CP:") + id.ToString(CultureInfo.InvariantCulture);

		private static string BaseUrl => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/');

		public static string InboxLink() => BaseUrl + "/User/ContentSets/Inbox";

		public static string ProposalsLink() => BaseUrl + "/User/ContentSets/Proposals";

		public static string SponsorshipsLink() => BaseUrl + "/User/ContentSets/Sponsorships";

		public static string LtiToolsLink() => BaseUrl + "/User/LmsStandards/Tools";

		public static CultureInfo Culture(string language) => TrainingRecordNoticeService.Culture(language);

		/// <summary>A notice string in the recipient's language (English when a key is missing).</summary>
		public static string Text(string key, CultureInfo culture)
		{
			var value = Strings.GetString(key, culture);
			return string.IsNullOrEmpty(value) ? Strings.GetString(key, CultureInfo.GetCultureInfo("en")) ?? key : value;
		}

		private static string Plain(string value, int max = 200)
		{
			var text = StringHelpers.HtmlToPlainText(value ?? string.Empty)?.Trim();
			if (string.IsNullOrEmpty(text))
				return string.Empty;
			return text.Length > max ? text.Substring(0, max) + "..." : text;
		}

		/// <summary>The title and message of a notice in a language (public for the tests).</summary>
		public static (string Title, string Message) Compose(Kinds kind, CultureInfo culture, string setName, string itemTitle, string otherDepartment, int count, string link)
		{
			var title = Text("Notice." + kind + ".Title", culture);
			var body = string.Format(culture, Text("Notice." + kind + ".Body", culture), Plain(setName), Plain(itemTitle), Plain(otherDepartment), count);
			return (title, string.IsNullOrWhiteSpace(link) ? body : body + " " + link);
		}

		public async Task UpdatesArrivedAsync(int subscriberDepartmentId, string setName, string publisherName, IReadOnlyList<ContentSetUpdate> updates,
			CancellationToken cancellationToken = default)
		{
			if (updates == null || updates.Count == 0 || !await NotifiesAsync(subscriberDepartmentId))
				return;
			var first = updates[0];
			await SendToAdminsAsync(subscriberDepartmentId, Kinds.UpdateArrived, first.ContentSetUpdateId,
				culture => Compose(Kinds.UpdateArrived, culture, setName, first.Title, publisherName, updates.Count, InboxLink()), cancellationToken);
		}

		public async Task ProposalReceivedAsync(ContentSetProposal proposal, string setName, string itemTitle, CancellationToken cancellationToken = default)
		{
			if (proposal == null || !await NotifiesAsync(proposal.PublisherDepartmentId))
				return;
			await SendToAdminsAsync(proposal.PublisherDepartmentId, Kinds.ProposalReceived, proposal.ContentSetProposalId,
				culture => Compose(Kinds.ProposalReceived, culture, setName, itemTitle, proposal.SubscriberName, 1, ProposalsLink()), cancellationToken);
		}

		/// <summary>Setting 151: whether the department hears about updates and proposals (sponsorship notices always go: it sees who pays).</summary>
		private async Task<bool> NotifiesAsync(int departmentId) => (await SettingsAsync(departmentId)).NotifyManagers;

		private async Task<ContentSetSettings> SettingsAsync(int departmentId)
		{
			try
			{
				return ContentSetSettings.Parse((await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.ContentSetSettings))?.Setting);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new ContentSetSettings();
			}
		}

		/// <summary>
		/// Who hears a department's set notices: its active admins when setting 151 keeps them on, plus the members of the roles it names (only
		/// the department's own roles count). When nobody would hear a notice that must go (a sponsorship, a tool to register), the admins do.
		/// </summary>
		public async Task<List<string>> RecipientsAsync(int departmentId, bool mustReach)
		{
			var settings = await SettingsAsync(departmentId);
			var recipients = new List<string>();
			if (settings.NotifyAdmins)
				recipients.AddRange(await AdminsAsync(departmentId));
			if (settings.NotifyRoleIds.Count > 0)
			{
				var own = ((await _roles.GetRolesForDepartmentAsync(departmentId)) ?? new List<PersonnelRole>())
					.Where(r => r != null && r.DepartmentId == departmentId).Select(r => r.PersonnelRoleId).ToHashSet();
				foreach (var roleId in settings.NotifyRoleIds.Where(own.Contains))
					recipients.AddRange(((await _roles.GetAllMembersOfRoleAsync(roleId)) ?? new List<PersonnelRoleUser>())
						.Where(m => m != null && !string.IsNullOrWhiteSpace(m.UserId)).Select(m => m.UserId));
			}
			if (recipients.Count == 0 && mustReach && !settings.NotifyAdmins)
				recipients.AddRange(await AdminsAsync(departmentId));
			return recipients.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		private async Task<List<string>> AdminsAsync(int departmentId) =>
			(await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Resgrid.Model.Identity.IdentityUser>())
				.Where(u => u != null && !string.IsNullOrWhiteSpace(u.Id)).Select(u => u.Id).ToList();

		public async Task ToolRegistrationNeededAsync(int subscriberDepartmentId, string setName, string toolName, string publisherName, CancellationToken cancellationToken = default)
		{
			if (subscriberDepartmentId <= 0)
				return;
			await SendToAdminsAsync(subscriberDepartmentId, Kinds.ToolRegistration, 0,
				culture => Compose(Kinds.ToolRegistration, culture, setName, toolName, publisherName, 1, LtiToolsLink()), cancellationToken, mustReach: true);
		}

		public async Task ProposalAnsweredAsync(ContentSetProposal proposal, string setName, string itemTitle, string publisherName, CancellationToken cancellationToken = default)
		{
			if (proposal == null || !await NotifiesAsync(proposal.DepartmentId))
				return;
			publisherName ??= (await _departments.GetDepartmentByIdAsync(proposal.PublisherDepartmentId, false))?.Name;
			// The member who proposed hears the answer first; the department's admins see it with the inbox.
			if (!string.IsNullOrWhiteSpace(proposal.AuthorUserId))
				await SendAsync(proposal.DepartmentId, proposal.AuthorUserId, Kinds.ProposalAnswered, proposal.ContentSetProposalId,
					culture => Compose(Kinds.ProposalAnswered, culture, setName, itemTitle, publisherName, 1, ProposalsLink()), cancellationToken);
		}

		public Task SponsorshipChangedAsync(int sponsoredDepartmentId, int sponsorshipId, int seatLimit, bool started, string sponsorName, CancellationToken cancellationToken = default)
		{
			if (sponsoredDepartmentId <= 0)
				return Task.CompletedTask;
			var kind = started ? Kinds.SponsorshipStarted : Kinds.SponsorshipEnded;
			return SendToAdminsAsync(sponsoredDepartmentId, kind, sponsorshipId,
				culture => Compose(kind, culture, null, null, sponsorName, seatLimit, SponsorshipsLink()), cancellationToken, mustReach: true);
		}

		private async Task SendToAdminsAsync(int departmentId, Kinds kind, int id, Func<CultureInfo, (string Title, string Message)> compose, CancellationToken cancellationToken,
			bool mustReach = false)
		{
			if (departmentId <= 0 || !ConfigHelper.CanTransmit(departmentId))
				return;
			List<string> admins;
			try
			{
				admins = await RecipientsAsync(departmentId, mustReach);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Set notice recipients could not be read.");
				return;
			}

			foreach (var userId in admins)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await SendAsync(departmentId, userId, kind, id, compose, cancellationToken);
			}
		}

		private async Task<bool> SendAsync(int departmentId, string userId, Kinds kind, int id, Func<CultureInfo, (string Title, string Message)> compose, CancellationToken cancellationToken)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !ConfigHelper.CanTransmit(departmentId))
				return false;
			try
			{
				var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				if (!active.Contains(userId))
					return false;
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				if (department == null)
					return false;

				var profile = await _profiles.GetProfileByUserIdAsync(userId, false);
				var (title, message) = compose(Culture(profile?.Language));
				var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
				return await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, profile, false, EventCode(kind, id));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Set notice {kind} ({(int)EventTypeOf(kind)}) for department {departmentId} could not be sent.");
				return false;
			}
		}
	}

	/// <summary>Content set and sponsorship work behind the publish hook: a publisher's protocol or course version went into force.</summary>
	public sealed class ContentSetPublishedHook : IContentVersionPublishedHook
	{
		// The library and course services hold this hook; the set service holds them. Lazy breaks the construction cycle.
		private readonly Lazy<IContentSetService> _sets;

		public ContentSetPublishedHook(Lazy<IContentSetService> sets)
		{
			_sets = sets;
		}

		public async Task OnVersionPublishedAsync(ContentVersionPublished published, CancellationToken cancellationToken = default)
		{
			if (published == null || published.DepartmentId <= 0 || published.ItemId <= 0)
				return;
			var kind = published.Kind == ContentVersionKinds.CourseVersion ? ContentSetItemKinds.LmsCourse : ContentSetItemKinds.Protocol;
			await _sets.Value.OnSourcePublishedAsync(published.DepartmentId, kind, published.ItemId, cancellationToken);
		}
	}

	/// <summary>
	/// Revocation (round 10 A3): when a link is disabled, deleted or stops sharing a kind, set subscriptions over it are suspended, LMS
	/// sponsorships over it end (lapse rules run from now) and open countersignature requests over it are voided. Every read re-checks the link
	/// as well, so this only makes the cut visible at once.
	/// </summary>
	public sealed class ContentSetLinkAccessListener : IDepartmentLinkAccessListener
	{
		// The set, sponsorship and countersignature services depend on the link service, which holds its listeners: Lazy breaks the cycle.
		private readonly Lazy<IContentSetService> _sets;
		private readonly Lazy<ILmsSponsorshipService> _sponsorships;
		private readonly Lazy<IMutualAidCountersignService> _countersign;
		private readonly Lazy<IRecordDeploymentPartnerService> _deploymentPartners;

		public ContentSetLinkAccessListener(Lazy<IContentSetService> sets, Lazy<ILmsSponsorshipService> sponsorships, Lazy<IMutualAidCountersignService> countersign,
			Lazy<IRecordDeploymentPartnerService> deploymentPartners)
		{
			_sets = sets;
			_sponsorships = sponsorships;
			_countersign = countersign;
			_deploymentPartners = deploymentPartners;
		}

		public async Task OnLinkAccessReducedAsync(DepartmentLink stored, DepartmentLink updated, CancellationToken cancellationToken = default)
		{
			if (stored == null || stored.DepartmentLinkId <= 0)
				return;
			var now = DateTime.UtcNow;
			await _sets.Value.SuspendLapsedAsync(stored.DepartmentLinkId, now, cancellationToken);
			await _sponsorships.Value.EndLapsedAsync(stored.DepartmentLinkId, now, cancellationToken);
			// Countersignature needs the link itself (any share kind): only a disabled or deleted link voids the request.
			if (updated == null || !updated.LinkEnabled)
			{
				await _countersign.Value.LapseForLinkAsync(stored.DepartmentLinkId, cancellationToken);
				await _deploymentPartners.Value.LapseForLinkAsync(stored.DepartmentLinkId, cancellationToken);
			}
		}
	}
}
