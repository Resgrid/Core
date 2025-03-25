using System.ComponentModel.DataAnnotations;
namespace Resgrid.Web.Services.Models.Services.Inputs.V1
{
	public class NewCallInput
	{
		public string UserName { get; set; }
        public string DepartmentId { get; set; }
        public string DepartmentCode { get; set; }
        [Required]
        public string Priority { get; set; }
        [Required]
        public string Name { get; set; }

        [Required(ErrorMessage="The Nature of call field")]
        public string NatureOfCall { get; set; }

		public string Notes { get; set; }
		public string Address { get; set; }
		public string GeoLocationData { get; set; }
	}
}