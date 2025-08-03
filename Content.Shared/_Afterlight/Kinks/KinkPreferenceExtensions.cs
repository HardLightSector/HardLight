using Content.Shared.Database._Afterlight;

namespace Content.Shared._Afterlight.Kinks;

public static class KinkPreferenceExtensions
{
    public static Color GetColor(this KinkPreference preference)
    {
        return preference switch
        {
            KinkPreference.No => Color.Red,
            KinkPreference.Ask => Color.Yellow,
            KinkPreference.Yes => Color.Green,
            KinkPreference.Favourite => Color.LightSkyBlue,
            _ => Color.White
        };
    }

    public static Color GetColor(this KinkPreference? preference)
    {
        return preference switch
        {
            null => Color.White,
            _ => preference.Value.GetColor()
        };
    }
}
