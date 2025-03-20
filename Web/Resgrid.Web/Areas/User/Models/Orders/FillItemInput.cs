namespace Resgrid.Web.Areas.User.Models.Orders
{
    public class FillItemInput
    {
		public int Id { get; set; }
		public string Name { get; set; }
		public string Number { get; set; }
		public string Note { get; set; }
		public string LeadUserId { get; set; }
		public int[] Units { get; set; }
	}
}