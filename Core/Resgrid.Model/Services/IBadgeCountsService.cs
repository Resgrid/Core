using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// One badge count (v4 Counts/GetBadgeCounts): a menu entry's or tab's number for the signed-in member. Implementations
	/// are found by assembly scanning (ServicesModule.RegisterBadgeCountProviders), so a new count is one class: declare
	/// its key (<see cref="BadgeCountKeys"/>), the v4 policy that protects the list it counts, and count the rows that list
	/// would show — with an indexed COUNT or an already cached read, never by loading the list.
	/// </summary>
	public interface IBadgeCountProvider
	{
		/// <summary>The key the apps ask for (permanent once shipped).</summary>
		string Key { get; }

		/// <summary>
		/// The ResgridResources policy name the member must hold (the list endpoint's own policy), checked by the web tier;
		/// null when the count needs none.
		/// </summary>
		string RequiredPolicy { get; }

		/// <summary>The count, or null when it does not apply to this member (module or feature flag off, plan without it).</summary>
		Task<BadgeCount> GetCountAsync(BadgeCountContext context);
	}

	/// <summary>Runs the badge count providers the caller asked for and may see.</summary>
	public interface IBadgeCountsService
	{
		/// <summary>Every registered key, in a stable order.</summary>
		IReadOnlyList<string> Keys { get; }

		/// <summary>
		/// The counts for <paramref name="keys"/> (null or empty: all of them). A provider runs only when
		/// <paramref name="isPolicyGranted"/> grants its <see cref="IBadgeCountProvider.RequiredPolicy"/>; one that fails or
		/// does not apply is left out, never failing the others. Unknown keys are ignored.
		/// </summary>
		Task<List<BadgeCount>> GetBadgeCountsAsync(BadgeCountContext context, IReadOnlyCollection<string> keys, Func<string, Task<bool>> isPolicyGranted);
	}
}
