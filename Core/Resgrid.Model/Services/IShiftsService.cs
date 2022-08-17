using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{

	/// <summary>Interface IShiftsService</summary>
	public interface IShiftsService
	{

		/// <summary>Gets all shifts by department asynchronous.</summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Shift&gt;&gt;.</returns>
		Task<List<Shift>> GetAllShiftsByDepartmentAsync(int departmentId);


		/// <summary>
		/// Gets the shift by identifier asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <returns>Task&lt;Shift&gt;.</returns>
		Task<Shift> GetShiftByIdAsync(int shiftId);


		/// <summary>Saves the shift asynchronous.</summary>
		/// <param name="shift">The shift.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Shift&gt;.</returns>
		Task<Shift> SaveShiftAsync(Shift shift, CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Updates the shift personnel.</summary>
		/// <param name="shift">The shift.</param>
		/// <param name="newPersonnel">The new personnel.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> UpdateShiftPersonnel(Shift shift, List<ShiftPerson> newPersonnel,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Updates the shift dates asynchronous.</summary>
		/// <param name="shift">The shift.</param>
		/// <param name="days">The days.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> UpdateShiftDatesAsync(Shift shift, List<ShiftDay> days,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Updates the shift groups asynchronous.</summary>
		/// <param name="shift">The shift.</param>
		/// <param name="groups">The groups.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> UpdateShiftGroupsAsync(Shift shift, List<ShiftGroup> groups,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Deletes the shift.</summary>
		/// <param name="shift">The shift.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteShift(Shift shift, CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Deletes the shift groups by group identifier asynchronous.</summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteShiftGroupsByGroupIdAsync(int departmentGroupId,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>
		///   <para>
		/// Rejects the trade request asynchronous.
		/// </para>
		/// </summary>
		/// <param name="shiftTradeId">The shift trade identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="reason">The reason.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> RejectTradeRequestAsync(int shiftTradeId, string userId, string reason,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Proposes the shift days for trade asynchronous.</summary>
		/// <param name="shiftTradeId">The shift trade identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="reason">The reason.</param>
		/// <param name="signups">The signups.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ProposeShiftDaysForTradeAsync(int shiftTradeId, string userId, string reason, List<int> signups,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Gets the shifts starting next day asynchronous.</summary>
		/// <param name="currentTime">The current time.</param>
		/// <returns>Task&lt;List&lt;Shift&gt;&gt;.</returns>
		Task<List<Shift>> GetShiftsStartingNextDayAsync(DateTime currentTime);


		/// <summary>Gets the shift days for day asynchronous.</summary>
		/// <param name="currentTime">The current time.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;ShiftDay&gt;&gt;.</returns>
		Task<List<ShiftDay>> GetShiftDaysForDayAsync(DateTime currentTime, int departmentId);


		/// <summary>Generates the shift notification text.</summary>
		/// <param name="shift">The shift.</param>
		/// <returns>System.String.</returns>
		string GenerateShiftNotificationText(Shift shift);


		/// <summary>Generates the shift trade notification text.</summary>
		/// <param name="profile">The profile.</param>
		/// <param name="trade">The trade.</param>
		/// <returns>System.String.</returns>
		string GenerateShiftTradeNotificationText(UserProfile profile, ShiftSignupTrade trade);


		/// <summary>Generates the shift trade rejection text.</summary>
		/// <param name="profile">The profile.</param>
		/// <param name="trade">The trade.</param>
		/// <returns>System.String.</returns>
		string GenerateShiftTradeRejectionText(UserProfile profile, ShiftSignupTrade trade);


		/// <summary>Generates the shift trade proposed text.</summary>
		/// <param name="profile">The profile.</param>
		/// <param name="trade">The trade.</param>
		/// <returns>System.String.</returns>
		string GenerateShiftTradeProposedText(UserProfile profile, ShiftSignupTrade trade);


		/// <summary>Generates the shift trade filled text.</summary>
		/// <param name="tradeProfile">The trade profile.</param>
		/// <param name="trade">The trade.</param>
		/// <returns>System.String.</returns>
		string GenerateShiftTradeFilledText(UserProfile tradeProfile, ShiftSignupTrade trade);


		/// <summary>Gets the shift day by identifier asynchronous.</summary>
		/// <param name="shiftDayId">The shift day identifier.</param>
		/// <returns>Task&lt;ShiftDay&gt;.</returns>
		Task<ShiftDay> GetShiftDayByIdAsync(int shiftDayId);


		/// <summary>Determines whether [is shift day filled asynchronous] [the specified shift day identifier].</summary>
		/// <param name="shiftDayId">The shift day identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> IsShiftDayFilledAsync(int shiftDayId);


		/// <summary>Gets the shift day needs asynchronous.</summary>
		/// <param name="shiftDayId">The shift day identifier.</param>
		/// <returns>Task&lt;Dictionary&lt;System.Int32, Dictionary&lt;System.Int32, System.Int32&gt;&gt;&gt;.</returns>
		Task<Dictionary<int, Dictionary<int, int>>> GetShiftDayNeedsAsync(int shiftDayId);


		/// <summary>Signups for shift day asynchronous.</summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <param name="shiftDay">The shift day.</param>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ShiftSignup&gt;.</returns>
		Task<ShiftSignup> SignupForShiftDayAsync(int shiftId, DateTime shiftDay, int departmentGroupId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Gets the shift signup by identifier asynchronous.</summary>
		/// <param name="shiftSignupId">The shift signup identifier.</param>
		/// <returns>Task&lt;ShiftSignup&gt;.</returns>
		Task<ShiftSignup> GetShiftSignupByIdAsync(int shiftSignupId);


		/// <summary>Determines whether [is user signed up for shift day asynchronous] [the specified shift day].</summary>
		/// <param name="shiftDay">The shift day.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> IsUserSignedUpForShiftDayAsync(ShiftDay shiftDay, string userId, int? departmentGroupId);


		/// <summary>Gets the shift signps for shift day asynchronous.</summary>
		/// <param name="shiftDayId">The shift day identifier.</param>
		/// <returns>Task&lt;List&lt;ShiftSignup&gt;&gt;.</returns>
		Task<List<ShiftSignup>> GetShiftSignpsForShiftDayAsync(int shiftDayId);


		/// <summary>Gets the shift day for signup asynchronous.</summary>
		/// <param name="shiftSignupId">The shift signup identifier.</param>
		/// <returns>Task&lt;ShiftDay&gt;.</returns>
		Task<ShiftDay> GetShiftDayForSignupAsync(int shiftSignupId);


		/// <summary>Gets the shift signups for user asynchronous.</summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;ShiftSignup&gt;&gt;.</returns>
		Task<List<ShiftSignup>> GetShiftSignupsForUserAsync(string userId);


		/// <summary>Deletes the shift signup asynchronous.</summary>
		/// <param name="signup">The signup.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteShiftSignupAsync(ShiftSignup signup,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Saves the trade asynchronous.</summary>
		/// <param name="trade">The trade.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ShiftSignupTrade&gt;.</returns>
		Task<ShiftSignupTrade> SaveTradeAsync(ShiftSignupTrade trade,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Gets the open trade requests for user asynchronous.</summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;ShiftSignupTrade&gt;&gt;.</returns>
		Task<List<ShiftSignupTrade>> GetOpenTradeRequestsForUserAsync(string userId);


		/// <summary>Gets the shift trade by identifier asynchronous.</summary>
		/// <param name="shiftTradeId">The shift trade identifier.</param>
		/// <returns>Task&lt;ShiftSignupTrade&gt;.</returns>
		Task<ShiftSignupTrade> GetShiftTradeByIdAsync(int shiftTradeId);


		/// <summary>Gets all shift staffings asynchronous.</summary>
		/// <returns>Task&lt;List&lt;ShiftStaffing&gt;&gt;.</returns>
		Task<List<ShiftStaffing>> GetAllShiftStaffingsAsync();


		/// <summary>Gets all shift staffings for department asynchronous.</summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;ShiftStaffing&gt;&gt;.</returns>
		Task<List<ShiftStaffing>> GetAllShiftStaffingsForDepartmentAsync(int departmentId);


		/// <summary>Gets the shift staffing by shift day asynchronous.</summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <param name="shiftDay">The shift day.</param>
		/// <returns>Task&lt;ShiftStaffing&gt;.</returns>
		Task<ShiftStaffing> GetShiftStaffingByShiftDayAsync(int shiftId, DateTime shiftDay);


		/// <summary>Saves the shift staffing asynchronous.</summary>
		/// <param name="staffing">The staffing.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ShiftStaffing&gt;.</returns>
		Task<ShiftStaffing> SaveShiftStaffingAsync(ShiftStaffing staffing,
			CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>Gets the shift groups by group identifier asynchronous.</summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <returns>Task&lt;List&lt;ShiftGroup&gt;&gt;.</returns>
		Task<List<ShiftGroup>> GetShiftGroupsByGroupIdAsync(int departmentGroupId);

		/// <summary>
		/// Populates the shift data.
		/// </summary>
		/// <param name="shift">The shift.</param>
		/// <param name="getDepartment">if set to <c>true</c> [get department].</param>
		/// <param name="getPersonnel">if set to <c>true</c> [get personnel].</param>
		/// <param name="getGroups">if set to <c>true</c> [get groups].</param>
		/// <param name="getSignups">if set to <c>true</c> [get signups].</param>
		/// <param name="getAdmins">if set to <c>true</c> [get admins].</param>
		/// <returns>Task&lt;Shift&gt;.</returns>
		Task<Shift> PopulateShiftData(Shift shift, bool getDepartment, bool getPersonnel, bool getGroups,
			bool getSignups, bool getAdmins);


		/// <summary>
		/// 
		/// </summary>
		/// <param name="shift"></param>
		/// <param name="shiftDay"></param>
		/// <returns></returns>
		Task<bool> IsShiftDayFilledWithObjAsync(Shift shift, ShiftDay shiftDay);

		/// <summary>
		/// 
		/// </summary>
		/// <param name="shift"></param>
		/// <param name="shiftDay"></param>
		/// <returns></returns>
		Task<Dictionary<int, Dictionary<int, int>>> GetShiftDayNeedsObjAsync(Shift shift, ShiftDay shiftDay);


		Task<List<ShiftSignup>> GetShiftSignupsByDepartmentGroupIdAndDayAsync(int departmentGroupId, DateTime shiftDayDate);
	}
}
