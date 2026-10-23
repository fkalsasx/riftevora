using System.Windows;
using System.Windows.Controls;
using SkinForge.UI.ViewModels;

namespace SkinForge.UI.Controls;

/// <summary>Picks the tile layout by what the card represents.</summary>
public sealed class CardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Portrait { get; set; }
    public DataTemplate? Mod { get; set; }
    public DataTemplate? Collection { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is CardItem card
        ? card.Kind switch { CardKind.Mod => Mod, CardKind.Collection => Collection, _ => Portrait }
        : base.SelectTemplate(item, container);
}
