using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MockRoom.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base for view models. Hand-rolled
/// to avoid an MVVM-framework dependency and keep the build NativeAOT-clean.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
