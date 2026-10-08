using System.Globalization;
using System.Text;
using AdminDesk.Domain.Definitions;
using AdminDesk.Domain.Engine;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Money;

namespace AdminDesk.Application.Engine;

// Builds the one-line subject of a request from the definition's template. Placeholders are
// written {fieldKey}; money is shown in rupees. When the template is missing or renders to
// nothing the module name is used.
public sealed class SubjectRenderer
{
    private const int MaxLength = 200;

    public string Render(ModuleDefinition definition, FieldValues values)
    {
        var template = definition.Subject;
        if (string.IsNullOrWhiteSpace(template))
        {
            return definition.Name;
        }

        var text = new StringBuilder();
        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                text.Append(template, index, template.Length - index);
                break;
            }
            text.Append(template, index, open - index);
            var key = template.Substring(open + 1, close - open - 1);
            text.Append(Display(definition, values, key));
            index = close + 1;
        }

        var result = text.ToString().Trim();
        if (result.Length == 0 || result.All(c => !char.IsLetterOrDigit(c)))
        {
            return definition.Name;
        }
        return result.Length > MaxLength ? result[..MaxLength] : result;
    }

    private static string Display(ModuleDefinition definition, FieldValues values, string key)
    {
        if (!values.TryGet(key, out var value) || value is null)
        {
            return string.Empty;
        }

        var field = definition.Fields.FirstOrDefault(f => f.Key == key);
        if (field?.Type == FieldType.Money && value is long minor)
        {
            return MoneyConverter.ToRupees(minor).ToString("0.##", CultureInfo.InvariantCulture);
        }

        return value switch
        {
            string[] many => string.Join(", ", many),
            bool flag => flag ? "Yes" : "No",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
