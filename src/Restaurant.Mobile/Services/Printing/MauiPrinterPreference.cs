#nullable enable

using Restaurant.UI.Shared.Services.Printing;

namespace Restaurant.Mobile.Services.Printing;

/// <summary>
/// Where this tablet remembers which printer it prints to: two strings in MAUI
/// Essentials' preference store, which on Android is a private SharedPreferences file.
///
/// **This is a device preference and not a venue record**, and the distinction is the
/// same one the printer setup screen makes about where it lives. Which printer the
/// tablet in the server station is bonded to is a fact about that tablet; which
/// terminals a venue owns and what each is assigned is the back office's Devices
/// record, which the handbook rules is additive API work. Putting the pairing in a
/// database would not make it survive a factory reset, and putting the registry in
/// SharedPreferences would put it somewhere a manager cannot read.
///
/// A remembered pairing comes back as NotTested rather than Ready
/// (<c>TransportReceiptPrinter</c>'s constructor): this store records which printer
/// was chosen and nothing at all about whether that printer is switched on today.
/// </summary>
public sealed class MauiPrinterPreference : IPrinterPreference
{
    private const string IdKey = "upos.printer.device.id";
    private const string NameKey = "upos.printer.device.name";

    public string? DeviceId => Read(IdKey);

    public string? DeviceName => Read(NameKey);

    public void Remember(string deviceId, string deviceName)
    {
        try
        {
            Preferences.Default.Set(IdKey, deviceId);
            Preferences.Default.Set(NameKey, deviceName);
        }
        catch (Exception)
        {
            // A preference store that will not write is a pairing that has to be made
            // again next launch. It is not a print failure and it is not worth an
            // error on a screen whose current selection is already correct.
        }
    }

    public void Forget()
    {
        try
        {
            Preferences.Default.Remove(IdKey);
            Preferences.Default.Remove(NameKey);
        }
        catch (Exception)
        {
        }
    }

    private static string? Read(string key)
    {
        try
        {
            var value = Preferences.Default.Get(key, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
