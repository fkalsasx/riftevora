namespace SkinForge.Core.Enums;

public enum ModCategory
{
    FullOverhaul = 0,
    ModelOnly = 1,
    TextureChroma = 2,
    VfxOnly = 3,
    AudioVoice = 4,
    MapEnvironment = 5,
    UiHud = 6
}

public enum ModStatus
{
    Disabled = 0,
    Active = 1,
    Conflicted = 2,
    Error = 3
}

public enum ConflictSeverity
{
    Warning = 0,
    Blocker = 1
}

public enum ViewMode
{
    Grid = 0,
    CompactGrid = 1,
    List = 2
}

public enum LoadOrderLayer
{
    BaseSkin = 1,
    Model = 2,
    Texture = 3,
    Vfx = 4,
    Sfx = 5,
    Ui = 6
}
