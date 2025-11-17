using Tibia.Protobuf.Appearances;

namespace Assets_Editor;

/// <summary>
/// Global clipboard for copying and pasting appearance flags between editor windows.
/// </summary>
public static class FlagClipboard
{
    private static AppearanceFlags _copiedFlags;

    public static bool HasFlags => _copiedFlags != null;

    public static void Copy(AppearanceFlags source)
    {
        if (source == null)
        {
            _copiedFlags = null;
            return;
        }

        _copiedFlags = source.Clone();
    }

    public static AppearanceFlags GetClone()
    {
        return _copiedFlags?.Clone();
    }
}
