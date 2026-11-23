namespace Resgrid.Model
{
	/// <summary>
	/// Why a shift signup, trade or roster change was refused. Serialized to API clients as snake_case codes.
	/// </summary>
	public enum ShiftActionErrors
	{
		None = 0,
		NotFound,
		NotAllowed,
		AlreadySignedUp,
		InvalidGroup,
		DayInPast,
		NotOnShift,
		TradeExists,
		NoUsers,
		InvalidOffer,
		NotPending,
		AlreadyOnRoster,
		InvalidRequest,

		/// <summary>The caller does not meet the requirements to take the slot (roles, already on the day).</summary>
		NotEligible,

		/// <summary>Every slot the group offers for the roles the member holds is taken (see <see cref="ShiftSignupEligibility"/>).</summary>
		GroupFull,

		/// <summary>The group lists the roles it needs and the member holds none of them.</summary>
		RoleRequired
	}

	public class ShiftActionResult<T>
	{
		public T Item { get; set; }

		public ShiftActionErrors Error { get; set; }

		public bool Success => Error == ShiftActionErrors.None;

		public static ShiftActionResult<T> Ok(T item)
		{
			return new ShiftActionResult<T> { Item = item, Error = ShiftActionErrors.None };
		}

		public static ShiftActionResult<T> Fail(ShiftActionErrors error)
		{
			return new ShiftActionResult<T> { Error = error };
		}
	}
}
