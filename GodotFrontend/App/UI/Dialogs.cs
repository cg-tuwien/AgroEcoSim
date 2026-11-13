using Godot;

namespace GodotFrontend.App.UI;

/// <summary>File open/save through one FileDialog node, native where the platform offers it.</summary>
public partial class Dialogs : Node
{
    private FileDialog? dialog;
    private Action<string>? pending;

    public void Open(string title, string[] filters, Action<string> onPicked) => Show(title, filters, FileDialog.FileModeEnum.OpenFile, "", onPicked);

    public void Save(string title, string suggested, string[] filters, Action<string> onPicked) => Show(title, filters, FileDialog.FileModeEnum.SaveFile, suggested, onPicked);

    private void Show(string title, string[] filters, FileDialog.FileModeEnum mode, string suggested, Action<string> onPicked)
    {
        if (dialog == null)
        {
            dialog = new FileDialog
            {
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,
                Size = new Vector2I(900, 600),
            };
            dialog.FileSelected += path => { var p = pending; pending = null; p?.Invoke(path); };
            AddChild(dialog);
        }
        pending = onPicked;
        dialog.Title = title;
        dialog.FileMode = mode;
        dialog.Filters = filters;
        dialog.CurrentFile = suggested;
        dialog.PopupCentered();
    }
}
