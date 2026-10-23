using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.Workspace;
using SkinForge.UI.Localization;
using SkinForge.UI.Services;

namespace SkinForge.UI.ViewModels;

public enum ToastTone { Info, Success, Warning, Error }

public sealed partial class ToastItem : ObservableObject
{
    public required string Message { get; init; }
    public ToastTone Tone { get; init; }
    public string? ActionLabel { get; init; }
    public Func<Task>? Action { get; init; }
    public bool HasAction => ActionLabel != null && Action != null;
    public string ToneKey => Tone.ToString().ToLowerInvariant();
    [ObservableProperty] private bool _isClosing;
}

/// <summary>Base for everything shown in the modal layer. The shell owns the single active dialog.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    public abstract string Title { get; }
    public Action? Close { get; set; }
    [RelayCommand] protected void Dismiss() => Close?.Invoke();
}

public sealed partial class ConfirmDialog(string title, string message, string confirmLabel, bool isDanger, Func<Task> onConfirm) : DialogViewModel
{
    public override string Title => title;
    public string Message => message;
    public string ConfirmLabel => confirmLabel;
    public bool IsDanger => isDanger;
    [RelayCommand] private async Task ConfirmAsync() { Close?.Invoke(); await onConfirm(); }
}

public sealed partial class PromptDialog(string title, string message, string hint, string initial, string confirmLabel, Func<string, Task> onConfirm) : DialogViewModel
{
    public override string Title => title;
    public string Message => message;
    public string Hint => hint;
    public string ConfirmLabel => confirmLabel;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand))] private string _text = initial;
    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Text);
    [RelayCommand(CanExecute = nameof(CanConfirm))] private async Task ConfirmAsync() { Close?.Invoke(); await onConfirm(Text.Trim()); }
}

/// <summary>Shown once before the first hand-off to LTK. Honest about third-party risk; remembered after acceptance.</summary>
public sealed partial class RiskDialog(string target, Func<Task> onAccept) : DialogViewModel
{
    public override string Title => Loc.T("risk_title");
    public string Target => target;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AcceptCommand))] private bool _understood;
    private bool CanAccept() => Understood;
    [RelayCommand(CanExecute = nameof(CanAccept))] private async Task AcceptAsync() { Close?.Invoke(); await onAccept(); }
}

public sealed partial class ImportItem : ObservableObject
{
    public required string Name { get; init; }
    [ObservableProperty] private int _stage;            // 0..6 ImportStage + 1; 6 = done
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _failed;
    [ObservableProperty] private string _error = "";
    public double Progress => Stage / 6d;
    partial void OnStageChanged(int value) => OnPropertyChanged(nameof(Progress));
}

public sealed partial class ImportDialog : DialogViewModel
{
    public override string Title => Loc.T("import_title");
    public ObservableCollection<ImportItem> Items { get; } = [];
    [ObservableProperty] private bool _finished;
    [ObservableProperty] private string _summary = "";
    public Action? GoToLibrary { get; set; }
    [RelayCommand] private void OpenLibrary() { Close?.Invoke(); GoToLibrary?.Invoke(); }
}

public sealed partial class CollectionChoice : ObservableObject
{
    public required CollectionData Collection { get; init; }
    public string Name => Collection.Name;
    public int Count => Collection.Items.Count;
    [ObservableProperty] private bool _isMember;
}

public sealed partial class CollectionPickerDialog : DialogViewModel
{
    private readonly AppServices _app;
    private readonly string _reference;
    private readonly Action<string> _notify;
    public CollectionPickerDialog(AppServices app, string reference, string itemTitle, Action<string> notify)
    {
        _app = app; _reference = reference; ItemTitle = itemTitle; _notify = notify;
        Rebuild();
    }
    public override string Title => Loc.T("collection_add_title");
    public string ItemTitle { get; }
    public ObservableCollection<CollectionChoice> Choices { get; } = [];
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CreateCommand))] private string _newName = "";
    public bool HasChoices => Choices.Count > 0;

    private void Rebuild()
    {
        Choices.Clear();
        foreach (var c in _app.Store.Data.Collections) Choices.Add(new CollectionChoice { Collection = c, IsMember = c.Items.Contains(_reference) });
        OnPropertyChanged(nameof(HasChoices));
    }

    [RelayCommand]
    private void Toggle(CollectionChoice? choice)
    {
        if (choice == null) return;
        choice.IsMember = _app.Store.ToggleInCollection(choice.Collection.Id, _reference);
        _notify(Loc.T(choice.IsMember ? "collection_added" : "collection_removed", choice.Name));
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(NewName);
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        var collection = _app.Store.CreateCollection(NewName);
        _app.Store.ToggleInCollection(collection.Id, _reference);
        NewName = "";
        Rebuild();
        _notify(Loc.T("collection_added", collection.Name));
    }
}

public sealed partial class SetupDialog : DialogViewModel
{
    public SetupDialog(AppServices app) { App = app; }
    public AppServices App { get; }
    public override string Title => Loc.T("setup_title");
    public bool LtkReady => App.LtkInstalled;
    public string LtkDetail => App.LtkInstalled ? (App.LtkVersion is { Length: > 0 } v ? "v" + v + " · " : "") + App.LtkPath : Loc.T("setup_ltk_missing");
    public bool GameReady => App.Game != null;
    public string GameDetail => App.Game?.RootDirectory ?? Loc.T("setup_game_missing");
    public Func<Task>? Recheck { get; set; }
    public Action? ChooseLtk { get; set; }
    public Action? DownloadLtk { get; set; }
    public Action? Done { get; set; }
    public void Refresh() { OnPropertyChanged(nameof(LtkReady)); OnPropertyChanged(nameof(LtkDetail)); OnPropertyChanged(nameof(GameReady)); OnPropertyChanged(nameof(GameDetail)); }
    [RelayCommand] private async Task RecheckAsync() { if (Recheck != null) await Recheck(); Refresh(); }
    [RelayCommand] private void ChooseLtkFile() => ChooseLtk?.Invoke();
    [RelayCommand] private void Download() => DownloadLtk?.Invoke();
    [RelayCommand] private void Finish() { Close?.Invoke(); Done?.Invoke(); }
}

public sealed partial class LightboxDialog(string title, string url) : ObservableObject
{
    public string Title => title;
    public string Url => url;
    public Action? Close { get; set; }
    [RelayCommand] private void Dismiss() => Close?.Invoke();
}
