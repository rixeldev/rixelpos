using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Desktop.Localization;
using RixelPOS.Domain.Entities;
using RixelPOS.Infrastructure;
using RixelPOS.Infrastructure.Hardware;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class ReceiptPreviewViewModel : DialogViewModelBase
{
    private readonly ReceiptService _receipt;
    private readonly LocalizationService _loc;
    private readonly Sale _sale;

    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _isPrinting;
    [ObservableProperty] private string _autoPrintText = "";

    private readonly DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft;

    public ReceiptPreviewViewModel(ReceiptService receipt, LocalizationService loc, Sale sale)
    {
        _receipt = receipt;
        _loc = loc;
        _sale = sale;
        PreviewText = ReceiptFormatter.FormatPreview(receipt.BuildData(sale));
        _autoTimer.Tick += (_, _) => OnAutoTick();
    }

    /// <summary>Starts the automatic print countdown shown when the preview opens.</summary>
    public void StartAutoPrint(int seconds = 3)
    {
        _secondsLeft = Math.Max(1, seconds);
        AutoPrintText = _loc.Format("receipt.autoPrintIn", _secondsLeft);
        _autoTimer.Start();
    }

    /// <summary>Cancels the timer when the window is closed by any route.</summary>
    public void CancelAutoPrint() => _autoTimer.Stop();

    private void OnAutoTick()
    {
        _secondsLeft--;
        if (_secondsLeft > 0)
        {
            AutoPrintText = _loc.Format("receipt.autoPrintIn", _secondsLeft);
            return;
        }

        _autoTimer.Stop();
        AutoPrintText = _loc.Get("receipt.printing");
        _ = PrintAsync();
    }

    public PrintResult? PrintResult { get; private set; }

    [RelayCommand]
    private async Task PrintAsync()
    {
        _autoTimer.Stop();
        if (IsPrinting)
        {
            return;
        }

        IsPrinting = true;
        AutoPrintText = _loc.Get("receipt.printing");
        try
        {
            var result = await _receipt.PrintAsync(_sale);
            PrintResult = result;
            StatusMessage = result.Message;
            HasStatus = true;
            StatusIsError = !result.Success;
        }
        catch (Exception ex)
        {
            PrintResult = RixelPOS.Application.Printing.PrintResult.Fail(ex.Message);
            StatusMessage = ex.Message;
            HasStatus = true;
            StatusIsError = true;
        }
        finally
        {
            IsPrinting = false;
            // The receipt window must never trap the cashier: close even when the
            // printer is not configured, and let the sale summary show the error.
            Close(true);
        }
    }

    [RelayCommand]
    private void ExportPdf()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DocumentsDirectory);
            var path = _receipt.DefaultPdfPath(_sale.Invoice, AppPaths.DocumentsDirectory);
            _receipt.ExportPdf(_sale, path);
            StatusMessage = _loc.Format("reports.exported", path);
            HasStatus = true;
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            HasStatus = true;
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private void ClosePreview()
    {
        _autoTimer.Stop();
        Close(false);
    }
}
