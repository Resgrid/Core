namespace Resgrid.Web.Services.Models.v4.Shifts
{
	public class SignupShiftDayResult : StandardApiResponseV4Base
	{
		public string Id { get; set; }

		/// <summary>The signup is waiting for supervisor approval</summary>
		public bool ApprovalPending { get; set; }

		/// <summary>Why the signup failed (snake_case code), empty on success</summary>
		public string ErrorCode { get; set; }
	}
}
