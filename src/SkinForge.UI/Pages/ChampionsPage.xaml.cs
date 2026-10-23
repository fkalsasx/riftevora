using System.Windows;
using System.Windows.Controls;
using SkinForge.UI.Controls;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI.Pages;

public partial class ChampionsPage : UserControl
{
    public ChampionsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ChampionsViewModel old) old.ScrollRequested -= OnScroll;
            if (e.NewValue is ChampionsViewModel current) current.ScrollRequested += OnScroll;
        };
        Unloaded += (_, _) => { if (DataContext is ChampionsViewModel vm) vm.ScrollRequested -= OnScroll; };
    }

    private void OnScroll(int index)
    {
        var grid = DataContext is ChampionsViewModel { IsTable: true } ? TableGrid : CardGrid;
        Visuals.Find<VirtualGridPanel>(grid)?.ScrollToItem(index);
    }
}
