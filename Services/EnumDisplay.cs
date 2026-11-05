using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace FirstWebAppInDocker.Services;

/// <summary>
/// Henter det norske visningsnavnet fra [Display(Name = "...")]
/// på en enum-verdi, f.eks. ResourceType.Generator → "Strøm / aggregat".
/// </summary>
public static class EnumDisplay
{
    public static string Name(Enum value)
    {
        MemberInfo? member =
            value.GetType()
                .GetMember(value.ToString())
                .FirstOrDefault();

        return member?
            .GetCustomAttribute<DisplayAttribute>()?
            .GetName()
            ?? value.ToString();
    }
}
