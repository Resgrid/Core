using System;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Gives a POST/PUT/PATCH/DELETE action (or a whole controller) the request-aborted token instead of the one
	/// <see cref="MutationCancellationTokenBinder"/> hands writes, so the work stops when the browser goes away. Use ONLY
	/// where stopping partway leaves nothing half saved: a read that is posted (a query body too big for a URL) or a proxy
	/// that hands the disconnect on to the service that does the work.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
	public sealed class CancelOnClientDisconnectAttribute : Attribute
	{
	}
}
