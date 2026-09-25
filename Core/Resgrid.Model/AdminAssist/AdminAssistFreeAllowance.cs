using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Which entitlement admitted an Admin Assist conversation turn (enhanced-ai-addon-plan.md §5.4).</summary>
	public static class AdminAssistAskTiers
	{
		/// <summary>The department holds the Enhanced AI add-on; turns draw on its monthly token budget.</summary>
		public const string EnhancedAi = "EnhancedAi";

		/// <summary>An operator-listed self-hosted installation; billing is not consulted.</summary>
		public const string SelfHosted = "SelfHosted";

		/// <summary>The free allowance every department gets; turns are counted as answered questions.</summary>
		public const string Free = "Free";
	}

	/// <summary>The free-allowance window a question is counted in: the starter window, or the rest of a calendar month.</summary>
	public sealed record AdminAssistFreeWindow(DateTime StartUtc, DateTime EndUtc, int Allowance, bool Starter);

	/// <summary>Free-tier ledger facts for one department: answered (or in-flight) questions in the window, and attempts of any outcome in the last 24 hours.</summary>
	public sealed record AiFreeUsage(int AnsweredInWindow, int AttemptsLast24Hours);

	/// <summary>Outcome of a free-tier admission attempt; <see cref="Reservation"/> is null unless <see cref="Reason"/> is "Reserved".</summary>
	public sealed record AiFreeReservationResult(AiUsageReservation Reservation, string Reason);

	/// <summary>
	/// Admin Assist free allowance (enhanced-ai-addon-plan.md §5.4). Every department gets a starter window that opens
	/// with its first answered question, then a small allowance per calendar month (UTC). Only answered questions count;
	/// unused questions never roll over. Pure arithmetic so the policy can be tested without a database.
	/// </summary>
	public static class AdminAssistFreeAllowance
	{
		public static AdminAssistFreeWindow Current(DateTime? firstAnsweredUtc, DateTime nowUtc, int starterQuestions, int starterDays, int monthlyQuestions)
		{
			if (nowUtc.Kind != DateTimeKind.Utc || firstAnsweredUtc.HasValue && firstAnsweredUtc.Value.Kind == DateTimeKind.Local)
				throw new ArgumentException("Allowance windows are computed in UTC.");
			starterQuestions = Math.Max(0, starterQuestions);
			starterDays = Math.Max(0, starterDays);
			monthlyQuestions = Math.Max(0, monthlyQuestions);

			// No answered question yet: the starter window has not opened, so everything the department has asked counts
			// against the starter allowance and the window is reported as starting now.
			if (!firstAnsweredUtc.HasValue)
				return new AdminAssistFreeWindow(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc), nowUtc.AddDays(starterDays), starterQuestions, true);

			var first = DateTime.SpecifyKind(firstAnsweredUtc.Value, DateTimeKind.Utc);
			var starterEnd = first.AddDays(starterDays);
			if (nowUtc < starterEnd)
				return new AdminAssistFreeWindow(first, starterEnd, starterQuestions, true);

			// After the starter window: the calendar month, minus any part of it the starter window already covered.
			var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
			return new AdminAssistFreeWindow(starterEnd > monthStart ? starterEnd : monthStart, monthStart.AddMonths(1), monthlyQuestions, false);
		}
	}

	/// <summary>Free-tier ledger reads and admission on AiUsageLedger (M0237 table; Feature/Tier/CreatedOnUtc from M0238).</summary>
	public interface IAiFreeAllowanceStore
	{
		/// <summary>When the department's first Admin Assist question was answered on any tier, or null if never.</summary>
		Task<DateTime?> GetFirstAnsweredAsync(int departmentId, CancellationToken ct);

		Task<AiFreeUsage> GetFreeUsageAsync(int departmentId, AdminAssistFreeWindow window, DateTime nowUtc, CancellationToken ct);

		/// <summary>
		/// Admits one free turn under the shared admission lock. Free turns never take the last inference slot, a department
		/// has at most one live turn, and the window allowance and 24-hour attempt limit are rechecked inside the lock.
		/// Reasons: Reserved, Busy, FreeAllowanceExhausted, FreeAttemptLimit.
		/// </summary>
		Task<AiFreeReservationResult> ReserveFreeAsync(AdminAssistActor actor, DateTime nowUtc, int tokens, AdminAssistFreeWindow window, int dailyAttemptLimit, CancellationToken ct);
	}
}
