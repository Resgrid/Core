using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Communications;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// contacts.preplans (plan §7.6, §9A.2): GetPreplansDueForReviewAsync (the Contacts page's overdue marker), longest
	/// overdue first. A company name is shown as is (not catalogued); a person contact's name resolves through
	/// ResolveContactsForReadAsync with the request's grant (contacts.firstname/lastname, ViewProtectedContactData). No
	/// pre-plan content is selected.
	/// </summary>
	[DashboardWidget("contacts.preplans")]
	public class ContactsPreplansWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IContactsService _contactsService;
		private readonly IProtectedReadService _protectedReadService;

		public ContactsPreplansWidgetProvider(IContactsService contactsService, IProtectedReadService protectedReadService)
		{
			_contactsService = contactsService;
			_protectedReadService = protectedReadService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var due = (await _contactsService.GetPreplansDueForReviewAsync(departmentId) ?? new List<ContactPreplan>())
				.Where(p => p != null && p.DepartmentId == departmentId && !p.IsDeleted && p.NextReviewDue.HasValue && !string.IsNullOrWhiteSpace(p.ContactId))
				.GroupBy(p => p.ContactId, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.OrderBy(p => p.NextReviewDue).First())
				.ToList();

			var data = new ContactsPreplansWidgetData();
			var contacts = await _contactsService.GetContactsByIdsAsync(departmentId, due.Select(p => p.ContactId)) ?? new Dictionary<string, Contact>();

			// A pre-plan whose contact was deleted (or belongs elsewhere) is not listed, as the Contacts page lists no row for it.
			var listed = due
				.Where(p => contacts.TryGetValue(p.ContactId, out var c) && c != null && c.DepartmentId == departmentId && !c.IsDeleted)
				.OrderBy(p => p.NextReviewDue)
				.ToList();
			data.DueCount = listed.Count;

			var shown = listed.Take(context.GetInt("maxItems", 8)).Select(p => (Preplan: p, Contact: contacts[p.ContactId])).ToList();

			// Only person names need the broker; a company name is never catalogued.
			var people = shown.Select(x => x.Contact).Where(c => string.IsNullOrWhiteSpace(c.CompanyName)).ToList();
			if (people.Count > 0)
				await _protectedReadService.ResolveContactsForReadAsync(departmentId, people, context.ProtectedGrantToken, context.UserId, cancellationToken);

			var canReveal = context.Viewer?.CanRevealAdp(PermissionTypes.ViewProtectedContactData) ?? false;
			foreach (var (preplan, contact) in shown)
			{
				var dueOn = preplan.NextReviewDue.Value;
				data.Items.Add(new ContactsPreplanItem
				{
					ContactId = contact.ContactId,
					ContactType = contact.ContactType,
					Name = DisplayName(contact, canReveal),
					NextReviewDueUtc = dueOn,
					DaysOverdue = Math.Max(0, (int)Math.Floor((context.NowUtc - dueOn).TotalDays)),
					Url = "/User/Contacts/View?contactId=" + Uri.EscapeDataString(contact.ContactId)
				});
			}

			return data;
		}

		internal static DashboardProtectedText DisplayName(Contact contact, bool canReveal)
		{
			if (!string.IsNullOrWhiteSpace(contact.CompanyName) && !WidgetProtectedText.IsSealed(contact.CompanyName))
				return DashboardProtectedText.Plain(contact.CompanyName.Trim());

			if (WidgetProtectedText.IsSealed(contact.FirstName) || WidgetProtectedText.IsSealed(contact.LastName))
				return DashboardProtectedText.Hidden(canReveal);

			return DashboardProtectedText.Plain(string.Join(" ", new[] { contact.FirstName, contact.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())));
		}
	}
}
