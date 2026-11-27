using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 10 A7: the department's LMS learner seat holders for an assignment, read at most once per actor (<see cref="LmsActor.SeatHolders"/>):
	/// a manager's request, a worker's department run or the requirement engine (which passes the licensed members it already read, R-L9).
	/// Fails closed: when the seats cannot be read nobody is seated, so nobody is enrolled in department LMS content.
	/// </summary>
	internal static class LmsLearnerSeatReads
	{
		public static async Task<HashSet<string>> SeatHoldersAsync(this ILmsGateService gate, LmsActor actor)
		{
			if (actor == null)
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (actor.SeatHolders != null)
				return actor.SeatHolders;

			HashSet<string> holders;
			try
			{
				holders = gate == null ? null : await gate.GetLearnerSeatHoldersAsync(actor.DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The department's LMS seats could not be read for an assignment.");
				holders = null;
			}

			actor.SeatHolders = new HashSet<string>(holders ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			return actor.SeatHolders;
		}
	}
}
