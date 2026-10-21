using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetSim.Core.Model;

/// <summary>
/// Базовый класс модели с уведомлением об изменениях.
/// Ядро не зависит от WPF, но интерфейс может привязываться к модели напрямую.
/// </summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
