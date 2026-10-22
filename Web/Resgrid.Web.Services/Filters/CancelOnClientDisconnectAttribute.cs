using System;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// Gives a POST/PUT/PATCH/DELETE action (or a whole controller) the request-aborted token instead of the one
	/// <see cref="MutationCancellationTokenBinder"/> hands writes, so the work stops when the client goes away. Use ONLY
	/// where stopping partway leaves nothing half saved: a read that is posted (a preview, a report run) or work built to
	/// settle its own cancellation (Admin Assist Ask and diagnostics release their lease and meter the outcome).
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
	public sealed class CancelOnClientDisconnectAttribute : Attribute
	{
	}
}
