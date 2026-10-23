using System.ComponentModel;
using System.Windows.Controls;
using SkinForge.UI.Localization;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI.Pages;

public partial class ChampionDrawerView : UserControl
{
    public ChampionDrawerView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ChampionDrawer old) old.PropertyChanged -= OnChanged;
            if (e.NewValue is ChampionDrawer current) { current.PropertyChanged += OnChanged; Sync(current); }
        };
        Unloaded += (_, _) => { if (DataContext is ChampionDrawer vm) vm.PropertyChanged -= OnChanged; };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ChampionDrawer vm && (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(ChampionDrawer.IsFavorite) or nameof(ChampionDrawer.ChampionIsFavorite))) Sync(vm);
    }

    // The two favourite buttons swap glyph and wording; a handful of lines here beats a trigger forest in XAML.
    private void Sync(ChampionDrawer vm)
    {
        FavSkinIcon.Filled = vm.IsFavorite;
        FavSkinText.Text = Loc.T(vm.IsFavorite ? "fav_remove_skin" : "fav_add_skin");
        FavChampionIcon.Filled = vm.ChampionIsFavorite;
        FavChampionIcon.SetResourceReference(Controls.Icon.ForegroundProperty, vm.ChampionIsFavorite ? "AccentBrush" : "TextPrimaryBrush");
    }
}
