using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Rules the CRR activity and alarm request services share (M0284): alarm counts, reason codes and the links a client
	/// posts by id (call, contact, member), which are checked against the department rather than trusted.
	/// </summary>
	internal static class CrrAlarmRules
	{
		/// <summary>A single visit or event never leaves more than this many units; a larger number is a typo.</summary>
		public const int MaxUnitsPerEntry = 10000;

		public static int Units(int value, string label)
		{
			if (value < 0) return 0;
			if (value > MaxUnitsPerEntry) throw new ArgumentException($"{label} can be at most {MaxUnitsPerEntry}.");
			return value;
		}

		public static int Reason(int value) => Enum.IsDefined(typeof(RmsAlarmReason), value) ? value : (int)RmsAlarmReason.Unspecified;

		public static int Source(int value) => Enum.IsDefined(typeof(RmsAlarmRequestSource), value) ? value : (int)RmsAlarmRequestSource.Phone;

		/// <summary>The call must be a live call of this department that the user may read.</summary>
		public static async Task RequireCallAsync(ICallsRepository calls, IRecordsAuthorizationService authorization, int departmentId, string userId, int callId)
		{
			var call = await calls.GetByIdAsync(callId);
			if (call == null || call.IsDeleted || !await authorization.CanReadSourceCallAsync(userId, departmentId, call))
				throw new ArgumentException("The call does not exist or you cannot view it.");
		}

		/// <summary>The contact must be a live contact of this department.</summary>
		public static async Task RequireContactAsync(IContactsRepository contacts, int departmentId, string contactId)
		{
			var contact = await contacts.GetByIdAsync(contactId) as Contact;
			if (contact == null || contact.DepartmentId != departmentId || contact.IsDeleted)
				throw new ArgumentException("The contact does not exist in this department.");
		}

		/// <summary>The occupancy must be a live occupancy of this department.</summary>
		public static async Task<RmsOccupancy> RequireOccupancyAsync(IRmsOccupanciesRepository occupancies, int departmentId, string occupancyId)
		{
			var occupancy = await occupancies.GetByIdForDepartmentAsync(departmentId, occupancyId);
			if (occupancy == null || occupancy.DeletedOn != null) throw new ArgumentException("The occupancy does not exist.");
			return occupancy;
		}
	}
}
