namespace Resgrid.Model.Dashboard.Widgets.Administration
{
	/// <summary>department.info (plan §7.11): today's Department Info ibox.</summary>
	public sealed class DepartmentInfoWidgetData
	{
		public int DepartmentId { get; set; }

		public string DepartmentCode { get; set; }

		public string TextToCallNumber { get; set; }
	}
}
