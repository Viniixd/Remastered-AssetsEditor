using System.Windows.Input;

namespace Assets_Editor;

/// <summary>
/// Centralized routed commands used for editor-wide keyboard shortcuts.
/// </summary>
public static class EditorCommands
{
    public static readonly RoutedUICommand CopyFlags = new RoutedUICommand(
        "Copy Flags",
        nameof(CopyFlags),
        typeof(EditorCommands));

    public static readonly RoutedUICommand PasteFlags = new RoutedUICommand(
        "Paste Flags",
        nameof(PasteFlags),
        typeof(EditorCommands));

    public static readonly RoutedUICommand SaveItem = new RoutedUICommand(
        "Save Item",
        nameof(SaveItem),
        typeof(EditorCommands));

    public static readonly RoutedUICommand ClearFlags = new RoutedUICommand(
        "Clear Flags",
        nameof(ClearFlags),
        typeof(EditorCommands));

    public static readonly RoutedUICommand DuplicateItem = new RoutedUICommand(
        "Duplicate Item",
        nameof(DuplicateItem),
        typeof(EditorCommands));
}
