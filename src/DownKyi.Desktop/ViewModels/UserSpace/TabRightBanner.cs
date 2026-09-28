using CommunityToolkit.Mvvm.ComponentModel;

namespace DownKyi.ViewModels.UserSpace;

internal class TabRightBanner : ObservableObject
{
    public int Id { get; set; }

    private bool isEnabled;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    private string label = string.Empty;

    public string Label
    {
        get => label;
        set => SetProperty(ref label, value);
    }

    private string count = string.Empty;

    public string Count
    {
        get => count;
        set => SetProperty(ref count, value);
    }
}
