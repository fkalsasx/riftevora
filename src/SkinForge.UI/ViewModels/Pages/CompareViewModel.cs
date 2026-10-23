using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkinForge.Core.Models;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public sealed partial class CompareViewModel : PageViewModel
{
    public CompareViewModel(AppServices app, ShellViewModel shell) : base(app, shell) { }
    public override string Key => "compare";
    public IReadOnlyList<OfficialChampion> Champions => App.Index.Champions;
    public IReadOnlyList<OfficialSkin> Skins { get; private set; } = [];
    [ObservableProperty] private OfficialChampion? _champion;
    [ObservableProperty] private OfficialSkin? _left;
    [ObservableProperty] private OfficialSkin? _right;
    public override Task ActivateAsync()
    {
        Champion ??= Champions.FirstOrDefault(c => c.Id == "Ahri") ?? Champions.FirstOrDefault();
        OnPropertyChanged(nameof(Champions));
        return Task.CompletedTask;
    }
    partial void OnChampionChanged(OfficialChampion? value)
    {
        Skins = value == null ? [] : App.Index.SkinsOf(value.Id).Where(s => s.Rarity != "Kroma").ToArray();
        OnPropertyChanged(nameof(Skins));
        Left = Skins.FirstOrDefault(); Right = Skins.Skip(1).FirstOrDefault() ?? Left;
    }
    [RelayCommand] private void Swap() { var previous = Left; Left = Right; Right = previous; }
    [RelayCommand] private void FavoriteLeft() { if (Left != null) Shell.ToggleFavorite(CardFactory.Skin(Left, App)); }
    [RelayCommand] private void FavoriteRight() { if (Right != null) Shell.ToggleFavorite(CardFactory.Skin(Right, App)); }
}
