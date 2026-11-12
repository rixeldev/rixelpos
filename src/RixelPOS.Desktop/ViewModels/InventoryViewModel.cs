using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Inventory;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Entities;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class InventoryViewModel : ViewModelBase, IActivatable, IScanTarget
{
    [ObservableProperty] private int _selectedTabIndex;

    public InventoryViewModel(
        ProductsTabViewModel products,
        ReceiveTabViewModel receive,
        StockCountTabViewModel count)
    {
        Products = products;
        Receive = receive;
        Count = count;
    }

    public ProductsTabViewModel Products { get; }
    public ReceiveTabViewModel Receive { get; }
    public StockCountTabViewModel Count { get; }


    public void Activate()
    {
        ActivateCurrentTab();
    }

    public void ShowTab(string tab)
    {
        SelectedTabIndex = tab switch
        {
            "receive" => 1,
            "count" => 2,
            _ => 0
        };
        ActivateCurrentTab();
    }

    partial void OnSelectedTabIndexChanged(int value) => ActivateCurrentTab();

    public bool HandleScan(string code)
    {
        return SelectedTabIndex switch
        {
            1 => Receive.HandleScan(code),
            2 => Count.HandleScan(code),
            _ => false
        };
    }

    private void ActivateCurrentTab()
    {
        switch (SelectedTabIndex)
        {
            case 1:
                Receive.Activate();
                break;
            case 2:
                Count.Activate();
                break;
            default:
                Products.Activate();
                break;
        }
    }
}

public sealed partial class ProductsTabViewModel : ViewModelBase, IActivatable
{
    private readonly InventoryService _inventory;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly ProductCsvService _csv;
    private readonly FileDialogService _files;
    private readonly ILabelPrinter _labelPrinter;
    private readonly LocalizationService _loc;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _lowStockOnly;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private ProductListItem? _selectedProduct;

    public ProductsTabViewModel(
        InventoryService inventory,
        SettingsService settings,
        DialogService dialogs,
        ProductCsvService csv,
        FileDialogService files,
        ILabelPrinter labelPrinter,
        LocalizationService loc)
    {
        _inventory = inventory;
        _settings = settings;
        _dialogs = dialogs;
        _csv = csv;
        _files = files;
        _labelPrinter = labelPrinter;
        _loc = loc;
    }

    public ObservableCollection<ProductListItem> Items { get; } = new();

    public void Activate() => Refresh();

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnLowStockOnlyChanged(bool value) => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var term = SearchText.Trim();
            var products = string.IsNullOrWhiteSpace(term)
                ? _inventory.Search(null)
                : _inventory.Search(term);
            Items.Clear();
            foreach (var product in products)
            {
                if (LowStockOnly && !product.IsLowStock)
                {
                    continue;
                }

                Items.Add(new ProductListItem(product,
                    _settings.FormatMoney(product.PriceCents),
                    _settings.FormatMoney(product.CostCents),
                    _settings.FormatQuantity(product.Stock),
                    _settings.FormatQuantity(product.MinStock)));
            }

            CountText = $"{Items.Count} {_loc.Get("inventory.productCount")}";
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [RelayCommand]
    private async Task NewProductAsync()
    {
        if (await _dialogs.EditProductAsync(null))
        {
            Refresh();
            StatusMessage = _loc.Get("inventory.productSaved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task EditProductAsync(ProductListItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (await _dialogs.EditProductAsync(item.Product))
        {
            Refresh();
            StatusMessage = _loc.Get("inventory.productSaved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task ArchiveProductAsync(ProductListItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(_loc.Get("inventory.archiveProduct"),
                $"{item.Name}. {_loc.Get("dialogs.deleteConfirm")}", _loc.Get("inventory.archiveProduct"), danger: true))
        {
            return;
        }

        try
        {
            _inventory.ArchiveProduct(item.Product.Id);
            Refresh();
            StatusMessage = _loc.Get("inventory.productArchived");
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [RelayCommand]
    private async Task AdjustStockAsync(ProductListItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (await _dialogs.AdjustStockAsync(item.Product))
        {
            Refresh();
            StatusMessage = _loc.Get("inventory.stockAdjusted");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task PrintLabelAsync(ProductListItem? item)
    {
        if (item is null)
        {
            return;
        }

        var result = await _labelPrinter.PrintLabelAsync(new LabelData(
            item.Product.Name,
            item.Product.Sku,
            item.Product.PrimaryBarcode,
            _settings.FormatMoney(item.Product.PriceCents),
            _settings.Current.BusinessName,
            _settings.Current.CurrencySymbol));
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var path = await _files.PickSaveAsync(_loc.Get("inventory.exportCsv"),
            $"productos-{DateTime.Now:yyyyMMdd}.csv", "CSV", "csv");
        if (path is null)
        {
            return;
        }

        try
        {
            _csv.Export(path);
            StatusMessage = _loc.Format("reports.exported", path);
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var path = await _files.PickOpenAsync(_loc.Get("inventory.importCsv"), "CSV", "csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var result = _csv.Import(path, updateExisting: true);
            Refresh();
            StatusMessage = $"{_loc.Get("inventory.csvImported")}: {result.Created} + {result.Updated} ({result.Skipped} omitidos)";
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task DownloadTemplateAsync()
    {
        var path = await _files.PickSaveAsync(_loc.Get("inventory.template"), "plantilla-productos.csv", "CSV", "csv");
        if (path is null)
        {
            return;
        }

        try
        {
            ProductCsvService.Template(path);
            StatusMessage = _loc.Format("reports.exported", path);
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }
}

public sealed partial class ReceiveTabViewModel : ViewModelBase, IActivatable
{
    private readonly InventoryService _inventory;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly SoundService _sound;
    private readonly LocalizationService _loc;
    private string? _lastCode;
    private DateTime _lastScanAt;

    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _lastScanName = "";
    [ObservableProperty] private string _lastScanDetail = "";
    [ObservableProperty] private bool _hasItems;

    public ReceiveTabViewModel(
        InventoryService inventory,
        SettingsService settings,
        DialogService dialogs,
        SoundService sound,
        LocalizationService loc)
    {
        _inventory = inventory;
        _settings = settings;
        _dialogs = dialogs;
        _sound = sound;
        _loc = loc;
    }

    public ObservableCollection<ReceivingLineItem> Items { get; } = new();

    public void Activate()
    {
        HasItems = Items.Count > 0;
    }

    public bool HandleScan(string code)
    {
        _ = CommitScanAsync(code);
        return true;
    }

    [RelayCommand]
    private Task CommitScanAsync() => CommitScanAsync(ScanText);

    [RelayCommand]
    private void Increment(ReceivingLineItem? item)
    {
        if (item is not null)
        {
            item.Quantity += 1;
        }
    }

    [RelayCommand]
    private void Decrement(ReceivingLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Quantity <= 1)
        {
            Items.Remove(item);
            HasItems = Items.Count > 0;
        }
        else
        {
            item.Quantity -= 1;
        }
    }

    [RelayCommand]
    private void Remove(ReceivingLineItem? item)
    {
        if (item is not null)
        {
            Items.Remove(item);
            HasItems = Items.Count > 0;
        }
    }

    [RelayCommand]
    private void ClearBatch()
    {
        Items.Clear();
        HasItems = false;
        LastScanName = "";
        LastScanDetail = "";
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Items.Count == 0)
        {
            StatusMessage = _loc.Get("inventory.receivingEmpty");
            StatusIsError = true;
            return;
        }

        try
        {
            var requests = Items.Select(item => item.Product is not null
                    ? new ReceivingRequest(item.Product.Id, item.Quantity)
                    : new ReceivingRequest(null, item.Quantity, item.NewProduct))
                .ToList();

            var result = _inventory.ReceiveBatch(requests);
            _sound.Play(ScanSound.Success);
            await _dialogs.ShowMessageAsync(_loc.Get("inventory.receivedSummary"),
                $"{_loc.Get("inventory.createdProducts")}: {result.CreatedProducts}\n" +
                $"{_loc.Get("inventory.updatedProducts")}: {result.UpdatedProducts}\n" +
                $"{_loc.Get("inventory.totalUnits")}: {result.TotalUnits:0.###}");
            Items.Clear();
            HasItems = false;
            LastScanName = "";
            LastScanDetail = "";
            StatusMessage = _loc.Get("inventory.batchSaved");
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            _sound.Play(ScanSound.Error);
        }
    }

    private async Task CommitScanAsync(string? rawCode)
    {
        var code = rawCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        ScanText = "";
        var now = DateTime.UtcNow;
        var debounce = _settings.Current.ScannerDebounceMs;
        if (_lastCode == code && (now - _lastScanAt).TotalMilliseconds < debounce)
        {
            StatusMessage = _loc.Get("inventory.scanDuplicated");
            StatusIsError = false;
            return;
        }

        _lastCode = code;
        _lastScanAt = now;

        try
        {
            var product = _inventory.FindByCode(code);
            if (product is not null)
            {
                var existing = Items.FirstOrDefault(i => i.Product?.Id == product.Id);
                if (existing is not null)
                {
                    existing.Quantity += 1;
                }
                else
                {
                    Items.Add(new ReceivingLineItem(product, null, code, product.Name,
                        $"{_settings.FormatQuantity(product.Stock)} {product.Unit}"));
                    HasItems = true;
                }

                LastScanName = product.Name;
                LastScanDetail = $"{_loc.Get("inventory.scanFound")} · {_settings.FormatMoney(product.PriceCents)}";
                StatusMessage = $"+1 {product.Name}";
                StatusIsError = false;
                _sound.Play(ScanSound.Found);
                return;
            }

            _sound.Play(ScanSound.NewProduct);
            var created = await _dialogs.CreateProductForReceivingAsync(code);
            if (created is null)
            {
                StatusMessage = _loc.Get("checkout.scanNotFound");
                StatusIsError = true;
                return;
            }

            Items.Add(new ReceivingLineItem(null, created, code, created.Name, "—"));
            HasItems = true;
            LastScanName = created.Name;
            LastScanDetail = _loc.Get("inventory.scanNew");
            StatusMessage = $"{_loc.Get("inventory.scanNew")}: {created.Name}";
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            _sound.Play(ScanSound.Error);
        }
    }
}

public sealed partial class StockCountTabViewModel : ViewModelBase, IActivatable
{
    private readonly InventoryService _inventory;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly SoundService _sound;
    private readonly FileDialogService _files;
    private readonly LocalizationService _loc;

    [ObservableProperty] private bool _hasOpenCount;
    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _applyAdjustments = true;
    [ObservableProperty] private string _countedText = "";
    [ObservableProperty] private string _differenceText = "";

    public StockCountTabViewModel(
        InventoryService inventory,
        SettingsService settings,
        DialogService dialogs,
        SoundService sound,
        FileDialogService files,
        LocalizationService loc)
    {
        _inventory = inventory;
        _settings = settings;
        _dialogs = dialogs;
        _sound = sound;
        _files = files;
        _loc = loc;
    }

    public ObservableCollection<StockCountLineItem> Items { get; } = new();

    private StockCount? CurrentCount { get; set; }

    public void Activate() => Load();

    public bool HandleScan(string code)
    {
        if (!HasOpenCount)
        {
            StatusMessage = _loc.Get("inventory.noOpenCount");
            StatusIsError = true;
            return false;
        }

        return Scan(code);
    }

    [RelayCommand]
    private void StartCount()
    {
        try
        {
            _inventory.StartStockCount(null);
            Load();
            StatusMessage = _loc.Get("inventory.openCount");
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private Task CommitScanAsync() => Task.Run(() => Scan(ScanText));

    [RelayCommand]
    private void Refresh() => Load();

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (CurrentCount is null)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(_loc.Get("inventory.finishCount"),
                $"{_loc.Get("inventory.counted")}: {CurrentCount.Lines.Count}\n" +
                $"{_loc.Get("inventory.differenceItems")}: {CurrentCount.Lines.Count(l => l.Difference != 0)}",
                _loc.Get("inventory.finishCount")))
        {
            return;
        }

        try
        {
            _inventory.CloseStockCount(CurrentCount.Id, ApplyAdjustments);
            _sound.Play(ScanSound.Success);
            StatusMessage = _loc.Get("inventory.countClosed");
            StatusIsError = false;
            Load();
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (CurrentCount is null)
        {
            return;
        }

        var path = await _files.PickSaveAsync(_loc.Get("inventory.countSummary"),
            $"conteo-{CurrentCount.Number}.csv", "CSV", "csv");
        if (path is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", new[]
        {
            _loc.Get("inventory.product"),
            _loc.Get("common.sku"),
            _loc.Get("inventory.system"),
            _loc.Get("inventory.counted"),
            _loc.Get("inventory.difference")
        }));
        foreach (var line in CurrentCount.Lines)
        {
            sb.AppendLine($"\"{line.ProductName}\",\"{line.Sku}\",{line.SystemQuantity:0.###},{line.CountedQuantity:0.###},{line.Difference:0.###}");
        }

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        StatusMessage = _loc.Format("reports.exported", path);
        StatusIsError = false;
    }

    private void Load()
    {
        CurrentCount = _inventory.GetOpenStockCount();
        HasOpenCount = CurrentCount is not null;
        Items.Clear();
        if (CurrentCount is not null)
        {
            foreach (var line in CurrentCount.Lines.OrderBy(l => l.ProductName))
            {
                Items.Add(new StockCountLineItem(line));
            }
        }

        CountedText = Items.Count.ToString();
        DifferenceText = Items.Count(i => i.HasDifference).ToString();
    }

    private bool Scan(string? rawCode)
    {
        var code = rawCode?.Trim();
        if (string.IsNullOrWhiteSpace(code) || CurrentCount is null)
        {
            return false;
        }

        try
        {
            var result = _inventory.ScanStockCount(CurrentCount.Id, code);
            if (result is null)
            {
                _sound.Play(ScanSound.Error);
                StatusMessage = _loc.Get("checkout.scanNotFound");
                StatusIsError = true;
                return false;
            }

            ScanText = "";
            Load();
            _sound.Play(ScanSound.Found);
            StatusMessage = $"+1 {result.Line.ProductName}";
            StatusIsError = false;
            return true;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            StatusIsError = true;
            _sound.Play(ScanSound.Error);
            return false;
        }
    }
}
