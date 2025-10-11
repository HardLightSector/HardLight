using System.Diagnostics.CodeAnalysis;
using Robust.Client.UserInterface;

namespace Content.Client._Afterlight.UserInterface;

public static class ALUserInterfaceExtensions
{
    public static bool TryFindParent<T>(this Control control, [NotNullWhen(true)] out T? parent)
    {
        while (control.Parent != null)
        {
            control = control.Parent;
            if (control is not T parentOfType)
                continue;

            parent = parentOfType;
            return true;
        }

        parent = default;
        return false;
    }
}