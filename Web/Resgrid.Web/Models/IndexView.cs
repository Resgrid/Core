namespace Resgrid.Web.Models
{
	public class IndexView
	{
		public ContactView Contact { get; set; }

		public IndexView()
		{
			Contact = new ContactView();
		}
	}
}