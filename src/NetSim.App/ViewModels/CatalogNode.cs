using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NetSim.App.Localization;
using NetSim.App.Rendering;
using NetSim.Core.Catalog;

namespace NetSim.App.ViewModels;

/// <summary>Узел дерева браузера оборудования: группа, устройство или ПО.</summary>
public sealed partial class CatalogNode : ObservableObject
{
    public CatalogCategory? Category { get; init; }
    public CatalogDevice? Device { get; init; }
    public CatalogSoftware? Software { get; init; }

    public ObservableCollection<CatalogNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isSelected;

    public bool IsItem => Device != null || Software != null;

    public string Title => Category?.Name(Loc.I.Lang) ?? Device?.Name(Loc.I.Lang) ?? Software?.Name(Loc.I.Lang) ?? "";

    public string Tooltip
    {
        get
        {
            var desc = Device?.Description(Loc.I.Lang) ?? Software?.Description(Loc.I.Lang) ?? "";
            if (Device != null) return $"{Title}\n{Device.Vendor}{(string.IsNullOrEmpty(desc) ? "" : "\n" + desc)}";
            if (Software != null) return $"{Title}\n{Loc.T("Catalog_DragSoftware")}{(string.IsNullOrEmpty(desc) ? "" : "\n" + desc)}";
            return Title;
        }
    }

    public ImageSource Icon => IconLibrary.Get(Device?.Icon ?? (Software != null ? "software" : Category?.Icon ?? "folder"));

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Tooltip));
        foreach (var c in Children) c.RefreshTexts();
    }

    /// <summary>Фильтр поиска: показывает совпавшие элементы и раскрывает их группы.</summary>
    public bool ApplyFilter(string text)
    {
        if (IsItem)
        {
            IsVisible = string.IsNullOrWhiteSpace(text)
                        || Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || (Device?.NameEn.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (Software?.NameEn.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (Device?.Vendor.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
            return IsVisible;
        }
        bool any = false;
        foreach (var c in Children) any |= c.ApplyFilter(text);
        bool selfMatch = !string.IsNullOrWhiteSpace(text) && Title.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (selfMatch) foreach (var c in Children) c.ShowAll();
        IsVisible = string.IsNullOrWhiteSpace(text) || any || selfMatch;
        if (!string.IsNullOrWhiteSpace(text)) IsExpanded = any || selfMatch;
        return IsVisible;
    }

    private void ShowAll()
    {
        IsVisible = true;
        foreach (var c in Children) c.ShowAll();
    }

    public static ObservableCollection<CatalogNode> Build(DeviceCatalog catalog)
    {
        var roots = new ObservableCollection<CatalogNode>();
        foreach (var cat in catalog.Categories) roots.Add(BuildCategory(catalog, cat));
        return roots;
    }

    private static CatalogNode BuildCategory(DeviceCatalog catalog, CatalogCategory cat)
    {
        var node = new CatalogNode { Category = cat };
        foreach (var child in cat.Children) node.Children.Add(BuildCategory(catalog, child));
        foreach (var id in cat.Items)
        {
            if (cat.IsSoftware)
            {
                var sw = catalog.SoftwareById(id);
                if (sw != null) node.Children.Add(new CatalogNode { Software = sw });
            }
            else
            {
                var d = catalog.Device(id);
                if (d != null) node.Children.Add(new CatalogNode { Device = d });
            }
        }
        return node;
    }
}
