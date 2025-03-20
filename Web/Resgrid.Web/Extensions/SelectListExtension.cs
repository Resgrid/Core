using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Model
{
	public static class EnumHelpers
	{
		public static SelectList ToSelectList<TEnum>(this TEnum enumObj)
		{
			var values = from TEnum e in Enum.GetValues(typeof(TEnum))
						 let fi = e.GetType().GetField(e.ToString())
						 where fi.GetCustomAttributes(true).All(attr => attr.GetType().Name != typeof(NotMappedAttribute).Name)
						 select new { Id = e, Name = e.ToString() };

			return new SelectList(values, "Id", "Name", enumObj);
		}

		public static SelectList ToSelectListInt<TEnum>(this TEnum enumObj)
		{
			var values = from TEnum e in Enum.GetValues(typeof(TEnum))
									 select new { Id = ((int)(dynamic)e), Name = e.ToString() };

			return new SelectList(values, "Id", "Name", ((int)(dynamic)enumObj));
		}

		public static SelectList ToSelectListDescription<TEnum>(this TEnum obj)
		where TEnum : struct, IComparable, IFormattable, IConvertible // correct one
		{

			return new SelectList(Enum.GetValues(typeof(TEnum)).OfType<Enum>()
				.Select(x =>
					new SelectListItem
					{
						Text = x.DisplayName(),
						Value = (Convert.ToInt32(x)).ToString()
					}), "Value", "Text");

		}


		public static string DisplayName(this Enum value)
		{
			FieldInfo field = value.GetType().GetField(value.ToString());

			System.ComponentModel.DataAnnotations.DisplayAttribute attribute
					= Attribute.GetCustomAttribute(field, typeof(System.ComponentModel.DataAnnotations.DisplayAttribute))
						as System.ComponentModel.DataAnnotations.DisplayAttribute;

			return attribute == null ? value.ToString() : attribute.Name;
		}
	}
}

namespace System.Web
{
	public static class EnumHelpers
	{
		private static Type GetNonNullableModelType(ModelMetadata modelMetadata)
		{
			Type realModelType = modelMetadata.ModelType;

			Type underlyingType = Nullable.GetUnderlyingType(realModelType);
			if (underlyingType != null)
			{
				realModelType = underlyingType;
			}
			return realModelType;
		}

		private static readonly SelectListItem[] SingleEmptyItem = new[] { new SelectListItem { Text = "", Value = "" } };

		public static string GetEnumDescription<TEnum>(TEnum value)
		{
			FieldInfo fi = value.GetType().GetField(value.ToString());

			DescriptionAttribute[] attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

			if ((attributes != null) && (attributes.Length > 0))
				return attributes[0].Description;
			else
				return value.ToString();
		}

				//public static MvcHtmlString EnumDropDownListFor<TModel, TEnum>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, TEnum>> expression)
				//{
				//    return EnumDropDownListFor(htmlHelper, expression, null);
				//}

				//public static MvcHtmlString EnumDropDownListFor<TModel, TEnum>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, TEnum>> expression, object htmlAttributes)
				//{
				//    ModelMetadata metadata = ModelMetadata.FromLambdaExpression(expression, htmlHelper.ViewData);
				//    Type enumType = GetNonNullableModelType(metadata);
				//    IEnumerable<TEnum> values = Enum.GetValues(enumType).Cast<TEnum>();

				//    IEnumerable<SelectListItem> items = from value in values
				//                                                                            select new SelectListItem
				//                                                                            {
				//                                                                                Text = GetEnumDescription(value),
				//                                                                                Value = value.ToString(),
				//                                                                                Selected = value.Equals(metadata.Model)
				//                                                                            };

				//    // If the enum is nullable, add an 'empty' item to the collection
				//    if (metadata.IsNullableValueType)
				//        items = SingleEmptyItem.Concat(items);

				//    return htmlHelper.DropDownListFor(expression, items, htmlAttributes);
				//}
	}
}
