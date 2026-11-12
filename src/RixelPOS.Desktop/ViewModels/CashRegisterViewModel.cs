using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Services;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Reporting;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class CashRegisterViewModel : ViewModelBase, IActivatable
{
    private readonly CashRegisterService _cash;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _loc;

    [ObservableProperty] private bool _hasOpenSession;
    [ObservableProperty] private string _openedAtText = "";
    [ObservableProperty] private string _openedByText = "";
    [ObservableProperty] private string _openingText = "";
    [ObservableProperty] private string _expectedText = "";
    [ObservableProperty] private string _salesCountText = "";
    [ObservableProperty] private string _salesTotalText = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _hasMovements;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private SessionHistoryItem? _selectedHistory;

    public CashRegisterViewModel(
        CashRegisterService cash,
        SettingsService settings,
        DialogService dialogs,
        LocalizationService loc)
    {
        _cash = cash;
        _settings = settings;
        _dialogs = dialogs;
        _loc = loc;
    }

    public ObservableCollection<PaymentTotalItem> Payments { get; } = new();
    public ObservableCollection<MovementItem> Movements { get; } = new();
    public ObservableCollection<SessionHistoryItem> History { get; } = new();


    public void Activate() => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var session = _cash.Current;
            HasOpenSession = session is not null;
            Payments.Clear();
            Movements.Clear();

            if (session is not null)
            {
                var report = _cash.BuildZReport(session);
                OpenedAtText = session.OpenedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                OpenedByText = session.OpenedByName;
                OpeningText = _settings.FormatMoney(session.OpeningCents);
                ExpectedText = _settings.FormatMoney(report.ExpectedCashCents);
                SalesCountText = report.SalesCount.ToString();
                SalesTotalText = _settings.FormatMoney(report.GrossSalesCents);

                foreach (var payment in report.Payments)
                {
                    Payments.Add(new PaymentTotalItem(PaymentName(payment.Method),
                        _settings.FormatMoney(payment.TotalCents), payment.Count));
                }

                foreach (var movement in report.Movements.OrderByDescending(m => m.CreatedAt))
                {
                    Movements.Add(new MovementItem(movement, _settings.FormatMoney(movement.AmountCents)));
                }
            }

            HasMovements = Movements.Count > 0;
            LoadHistory();
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [RelayCommand]
    private async Task OpenRegisterAsync()
    {
        if (await _dialogs.OpenRegisterAsync())
        {
            Refresh();
            StatusMessage = _loc.Get("cash.registerOpen");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task CloseRegisterAsync()
    {
        if (!HasOpenSession)
        {
            return;
        }

        if (await _dialogs.CloseRegisterAsync())
        {
            Refresh();
            StatusMessage = _loc.Get("cash.sessionClosed");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task CashInAsync()
    {
        if (await _dialogs.CashMovementAsync(CashMovementType.In))
        {
            Refresh();
            StatusMessage = _loc.Get("cash.cashMovementSaved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task CashOutAsync()
    {
        if (await _dialogs.CashMovementAsync(CashMovementType.Out))
        {
            Refresh();
            StatusMessage = _loc.Get("cash.cashMovementSaved");
            StatusIsError = false;
        }
    }

    [RelayCommand]
    private async Task ViewSessionReportAsync(SessionHistoryItem? item)
    {
        item ??= SelectedHistory;
        if (item is null)
        {
            return;
        }

        var report = _cash.BuildZReport(item.Session);
        await _dialogs.ShowZReportAsync(report);
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var session in _cash.GetHistory())
        {
            History.Add(new SessionHistoryItem(session, _settings.FormatMoney(session.OpeningCents),
                session.ClosingCents is null ? "—" : _settings.FormatMoney(session.ClosingCents.Value),
                session.DifferenceCents is null ? "—" : _settings.FormatMoney(session.DifferenceCents.Value)));
        }

        HasHistory = History.Count > 0;
    }

    private string PaymentName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => _loc.Get("checkout.cash"),
        PaymentMethod.Card => _loc.Get("checkout.card"),
        PaymentMethod.Transfer => _loc.Get("checkout.transfer"),
        PaymentMethod.StoreCredit => _loc.Get("checkout.storeCredit"),
        PaymentMethod.Credit => _loc.Get("checkout.credit"),
        _ => method.ToString()
    };
}
