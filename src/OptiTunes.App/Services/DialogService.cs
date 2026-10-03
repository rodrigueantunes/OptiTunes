using System.Windows;
using Microsoft.Win32;

namespace OptiTunes.App.Services;

public interface IDialogService
{
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string defaultName);
    void ShowError(string message);
}

public sealed class DialogService : IDialogService
{
    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string message) =>
        MessageBox.Show(message, "OptiTunes", MessageBoxButton.OK, MessageBoxImage.Warning);
}
