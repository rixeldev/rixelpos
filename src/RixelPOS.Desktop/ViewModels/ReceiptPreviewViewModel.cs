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

    public ReceiptPreviewViewModel(ReceiptService receipt, LocalizationService loc, Sale sale)
    {
        _receipt = receipt;
        _loc = loc;
        _sale = sale;
        PreviewText = ReceiptFormatter.FormatPreview(receipt.BuildData(sale));
    }

    public PrintResult? PrintResult { get; private set; }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (IsPrinting)
        {
            return;
        }

        IsPrinting = true;
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
    private void ClosePreview() => Close(false);
}
