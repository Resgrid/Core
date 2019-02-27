using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IShiftsService
	{
		List<Shift> GetAllShiftsByDepartment(int departmentId);
		Shift GetShiftById(int shiftId);
		Shift SaveShift(Shift shift);
		void UpdateShiftPersonnel(Shift shift, List<ShiftPerson> personnel);
		void UpdateShiftDates(Shift shift, List<ShiftDay> days);
		void UpdateShiftGroups(Shift shift, List<ShiftGroup> groups);
		List<Shift> GetShiftsStartingNextDay(DateTime currentTime);
		string GenerateShiftNotificationText(Shift shift);
		void DeleteShift(Shift shift);
		bool IsShiftDayFilled(int shiftDayId);
		ShiftDay GetShiftDayById(int shitDayId);
		Dictionary<int, Dictionary<int, int>> GetShiftDayNeeds(int shiftDayId);
		ShiftSignup SignupForShiftDay(int shiftId, DateTime shiftDay, int departmentGroupId, string userId);
		ShiftSignup GetShiftSignupById(int shiftSignupId);
		bool IsUserSignedUpForShiftDay(ShiftDay shiftDay, string userId);
		List<ShiftSignup> GetShiftSignpsForShiftDay(int shiftDayId);
		List<ShiftSignup> GetShiftSignupsForUser(string userId);
		void DeleteShiftSignup(ShiftSignup signup);
		void DeleteShiftGroupsByGroupId(int departmentGroupId);
		ShiftDay GetShiftDayForSignup(int shiftSignupId);
		ShiftSignupTrade SaveTrade(ShiftSignupTrade trade);
		List<ShiftSignupTrade> GetOpenTradeRequestsForUser(string userId);
		ShiftSignupTrade GetShiftTradeById(int shiftTradeId);
		string GenerateShiftTradeNotificationText(UserProfile profile, ShiftSignupTrade trade);
		void RejectTradeRequest(int shiftTradeId, string userId, string reason);
		string GenerateShiftTradeRejectionText(UserProfile profile, ShiftSignupTrade trade);
		void ProposeShiftDaysForTrade(int shiftTradeId, string userId, string reason, List<int> signups);
		string GenerateShiftTradeProposedText(UserProfile profile, ShiftSignupTrade trade);
		string GenerateShiftTradeFilledText(UserProfile tradeProfile, ShiftSignupTrade trade);
		List<ShiftStaffing> GetAllShiftStaffings();
		List<ShiftStaffing> GetAllShiftStaffingsForDepartment(int departmentId);
		ShiftStaffing GetShiftStaffingByShiftDay(int shiftId, DateTime shiftDay);
		ShiftStaffing SaveShiftStaffing(ShiftStaffing staffing);
		List<ShiftDay> GetShiftDaysForDay(DateTime currentTime, int departmentId);
		List<ShiftGroup> GetShiftGroupsByGroupId(int departmentGroupId);
	}
}
