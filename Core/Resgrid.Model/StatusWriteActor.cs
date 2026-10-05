using System;
using System.Threading;

namespace Resgrid.Model
{
	/// <summary>
	/// Who is submitting statuses on the current request or job, so the status write choke points
	/// (<c>UnitsService.SetUnitStateAsync</c>, <c>ActionLogsService.SaveActionLogAsync</c>/<c>SaveAllActionLogsAsync</c>) can
	/// stamp <c>SetByUserId</c>/<c>SetByOrigin</c> (M0260) without every caller threading the actor through. Entry points
	/// open a scope: the Web and API request filters for signed-in members, and the SMS, voice, chatbot, schedule and
	/// maintenance paths for theirs. A row that already carries an actor keeps it, and a write outside any scope is stored
	/// with none (shown as unknown), never with a guess.
	/// </summary>
	public static class StatusWriteActor
	{
		private sealed class Context
		{
			public string UserId;
			public StatusSetOrigins Origin;
		}

		private static readonly AsyncLocal<Context> Current = new AsyncLocal<Context>();

		/// <summary>The member submitting, or null when none is known.</summary>
		public static string UserId => Current.Value?.UserId;

		/// <summary>The current origin, or <see cref="StatusSetOrigins.Unknown"/> outside any scope.</summary>
		public static StatusSetOrigins Origin => Current.Value?.Origin ?? StatusSetOrigins.Unknown;

		/// <summary>Opens a scope; disposing it restores the enclosing one.</summary>
		public static IDisposable Begin(string userId, StatusSetOrigins origin)
		{
			var previous = Current.Value;
			Current.Value = new Context { UserId = string.IsNullOrWhiteSpace(userId) ? null : userId, Origin = origin };
			return new Scope(() => Current.Value = previous);
		}

		/// <summary>
		/// Opens a scope for statuses Resgrid applies on someone's behalf (dispatch statuses, holds): the member who caused
		/// it stays the actor and the origin says it was automatic.
		/// </summary>
		public static IDisposable BeginAutomation(StatusSetOrigins origin)
		{
			return Begin(UserId, origin);
		}

		/// <summary>Fills an unstamped unit state from the current scope.</summary>
		public static void Stamp(UnitState state)
		{
			if (state == null || state.SetByOrigin.HasValue || Current.Value == null)
				return;

			state.SetByUserId = Current.Value.UserId;
			state.SetByOrigin = (int)Current.Value.Origin;
		}

		/// <summary>Fills an unstamped personnel status from the current scope.</summary>
		public static void Stamp(ActionLog actionLog)
		{
			if (actionLog == null || actionLog.SetByOrigin.HasValue || Current.Value == null)
				return;

			actionLog.SetByUserId = Current.Value.UserId;
			actionLog.SetByOrigin = (int)Current.Value.Origin;
		}

		private sealed class Scope : IDisposable
		{
			private Action _restore;

			public Scope(Action restore)
			{
				_restore = restore;
			}

			public void Dispose()
			{
				Interlocked.Exchange(ref _restore, null)?.Invoke();
			}
		}
	}
}
