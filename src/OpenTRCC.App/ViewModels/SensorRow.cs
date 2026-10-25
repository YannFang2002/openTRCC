using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenTRCC.App.ViewModels;

/// <summary>One row of the sensor browser.</summary>
public sealed class SensorRow : INotifyPropertyChanged
{
    private string _value = "—";

    public SensorRow(string id, string name, string unit, string source, string category)
    {
        Id = id;
        Name = name;
        Unit = unit;
        Source = source;
        Category = category;
    }

    public string Id { get; }

    public string Name { get; }

    public string Unit { get; }

    public string Source { get; }

    public string Category { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
