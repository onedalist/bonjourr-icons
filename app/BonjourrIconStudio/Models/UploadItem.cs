using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BonjourrIconStudio.Models;

public sealed class UploadItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _status = "Готов к загрузке";
    private string? _publicUrl;

    public required string LocalPath { get; init; }
    public string FileName => Path.GetFileName(LocalPath);
    public string SizeLabel
    {
        get
        {
            var bytes = new FileInfo(LocalPath).Length;
            return bytes < 1024 * 1024
                ? $"{bytes / 1024d:0.#} КБ"
                : $"{bytes / 1024d / 1024d:0.##} МБ";
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public string? PublicUrl
    {
        get => _publicUrl;
        set
        {
            if (_publicUrl == value) return;
            _publicUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPublicUrl));
        }
    }

    public bool HasPublicUrl => !string.IsNullOrWhiteSpace(PublicUrl);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
