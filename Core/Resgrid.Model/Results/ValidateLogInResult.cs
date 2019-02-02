namespace Resgrid.Model.Results
{
	public class ValidateLogInResult
	{
		public bool Successful { get; set; }
		public bool IsLockedOut { get; set; }
		public bool NotAllowed { get; set; }
	}
}