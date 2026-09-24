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
		InvalidRequest
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
