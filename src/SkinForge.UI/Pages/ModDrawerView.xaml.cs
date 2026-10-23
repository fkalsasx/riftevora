using System.ComponentModel;
using System.Windows.Controls;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI.Pages;

public partial class ModDrawerView : UserControl
{
    public ModDrawerView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ModDrawer old) old.PropertyChanged -= OnChanged;
            if (e.NewValue is ModDrawer current) { current.PropertyChanged += OnChanged; Sync(current); }
        };
        Unloaded += (_, _) => { if (DataContext is ModDrawer vm) vm.PropertyChanged -= OnChanged; };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ModDrawer vm && (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ModDrawer.IsFavorite))) Sync(vm);
    }

    private void Sync(ModDrawer vm)
    {
        FavIcon.Filled = vm.IsFavorite;
        FavIcon.SetResourceReference(Controls.Icon.ForegroundProperty, vm.IsFavorite ? "AccentBrush" : "TextPrimaryBrush");
    }
}
