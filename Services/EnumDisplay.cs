/*
    EnumDisplay.cs – norske navn på kategorier

    Hva filen gjør: Henter det norske navnet fra [Display(Name=...)], for eksempel Generator →
    "Strøm / aggregat".
*/

using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace FirstWebAppInDocker.Services;

/*
    Henter det norske visningsnavnet fra [Display(Name = "...")]
    på en enum-verdi, f.eks. ResourceType.Generator → "Strøm / aggregat".
*/
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
