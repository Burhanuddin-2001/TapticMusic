using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicPlayer.ViewModels;

/// <summary>
/// The change-notification base for the view models. Hand-written on purpose: a toolkit for this
/// would be a sixth NuGet package, and this is the whole of what the six view models need.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>
    /// Fires when a property changes. Every {x:Bind Mode=OneWay} in the app listens to this, and so
    /// does MainWindow.xaml.cs, which cannot bind at all (A7b) and instead subscribes by hand and
    /// filters on the property name.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns a backing field and announces the change, but only when the value actually differs.
    /// That equality check is load-bearing rather than tidy: the 250ms position poll writes the same
    /// value most of the time, and without it every tick would repaint the transport. The property
    /// name is supplied by the compiler, so no call site holds a string that can go stale after a
    /// rename.
    /// </summary>
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Raise(propertyName);
    }

    /// <summary>
    /// For a property with no backing field of its own, which is most of PlayerBarViewModel:
    /// those read straight through to the audio service and are re-read when it says so.
    /// </summary>
    protected void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
