using System;
using System.ComponentModel;
using System.Linq;
using System.Web.Mvc;

namespace Resgrid.Web.Services.ModelBinders
{
	//public class AliasFormModelBinder : DefaultModelBinder
	//{
	//	protected override void BindProperty(ControllerContext controllerContext, ModelBindingContext bindingContext,
	//			PropertyDescriptor propertyDescriptor)
	//	{
	//		var aliasAttribute = TryGetAttribute<AliasAttribute>(propertyDescriptor);
	//		if (aliasAttribute != null)
	//		{
	//			var strValue = controllerContext.HttpContext.Request.Form[aliasAttribute.Name];
	//			var value = Convert.ChangeType(strValue, propertyDescriptor.PropertyType);

	//			propertyDescriptor.SetValue(bindingContext.Model, value);
	//		}
	//		else
	//		{
	//			base.BindProperty(controllerContext, bindingContext, propertyDescriptor);
	//		}
	//	}

	//	private T TryGetAttribute<T>(PropertyDescriptor propertyDescriptor) where T : Attribute
	//	{
	//		return propertyDescriptor.Attributes
	//			.OfType<T>()
	//			.FirstOrDefault();
	//	}
	//}
}