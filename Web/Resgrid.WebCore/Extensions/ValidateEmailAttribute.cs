using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Extensions
{
	public class ValidateEmailAttribute : RegularExpressionAttribute
	{
		public ValidateEmailAttribute()
			: base(@"[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?")
		{
		}
	}
}