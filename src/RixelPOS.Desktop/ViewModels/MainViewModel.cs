using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Security;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;

namespace RixelPOS.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasErrorMessage));

    protected LocalizationService Loc => LocalizationService.Instance;

    protected void ReportError(Exception ex)
    {
        ErrorMessage = ex is Domain.Common.BusinessRuleException rule ? rule.Message : ex.Message;
    }

}

public abstract class DialogViewModelBase : ViewModelBase
{
    public event EventHandler<bool>? CloseRequested;

    protected void Close(bool result) => CloseRequested?.Invoke(this, result);
}

public interface IActivatable
{
    void Activate();
}

public interface IScanTarget
{
    bool HandleScan(string code);
}

public sealed class NavItem
{
    public NavItem(string key, string label, string iconKey, bool visible)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
        IsVisible = visible;
        Icon = ResolveIcon(iconKey);
    }

    public string Key { get; }
    public string Label { get; }
    public string IconKey { get; }
    public bool IsVisible { get; }
    public Geometry? Icon { get; }

    private static Geometry? ResolveIcon(string key)
    {
        try
        {
            return Avalonia.Application.Current?.Resources[key] as Geometry;
        }
        catch
        {
            return null;
        }
    }
}

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly SettingsService _settings;
    private readonly AuthService _auth;
    private readonly CashRegisterService _cash;
    private readonly ThemeService _theme;
    private readonly LocalizationService _loc;
    private readonly IClock _clock;
    private readonly CheckoutViewModel _checkout;
    private readonly DialogService _dialogs;
    private readonly ScanRouter _scannerRouter;
    private readonly IScannerListener _scannerListener;
    private readonly IAppLogger _logger;
    private readonly Dictionary<string, object> _pages = new();
    private readonly List<NavItem> _allNav = new();
    private bool _suppressNavigation;

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private string _pageTitle = "";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _roleLabel = "";
    [ObservableProperty] private string _initials = "";
    [ObservableProperty] private string _businessName = "";
    [ObservableProperty] private string _cashStatus = "";
    [ObservableProperty] private bool _isCashOpen;
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private bool _isSidebarExpanded = true;

    public MainViewModel(
        AppSession session,
        SettingsService settings,
        AuthService auth,
        CashRegisterService cash,
        ThemeService theme,
        LocalizationService loc,
        IClock clock,
        ScanRouter scannerRouter,
        IScannerListener scannerListener,
        IAppLogger logger,
        DialogService dialogs,
        DashboardViewModel dashboard,
        CheckoutViewModel checkout,
        InventoryViewModel inventory,
        CustomersViewModel customers,
        SuppliersViewModel suppliers,
        CashRegisterViewModel cashRegister,
        ReportsViewModel reports,
        SettingsViewModel settingsPage)
    {
        _session = session;
        _settings = settings;
        _auth = auth;
        _cash = cash;
        _theme = theme;
        _loc = loc;
        _clock = clock;
        _scannerRouter = scannerRouter;
        _scannerListener = scannerListener;
        _logger = logger;

        _checkout = checkout;
        _dialogs = dialogs;
        _pages["dashboard"] = dashboard;
        _pages["checkout"] = checkout;
        _pages["inventory"] = inventory;
        _pages["customers"] = customers;
        _pages["suppliers"] = suppliers;
        _pages["cash"] = cashRegister;
        _pages["reports"] = reports;
        _pages["settings"] = settingsPage;

        dashboard.NavigationRequested += (_, key) => Navigate(key);

        _session.Changed += (_, _) => RefreshChrome();
        _scannerRouter.Scanned += OnScanned;

        BuildNavigation();
        RefreshChrome();
        Navigate("dashboard");
    }

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public event EventHandler? LogoutRequested;

    public bool IsAdmin => _session.CurrentUser?.Role == UserRole.Admin;
    public bool IsManagerOrAdmin => _session.CurrentUser?.Role is UserRole.Manager or UserRole.Admin;

    public async Task InitializeAsync()
    {
        RefreshChrome();
        var connection = _settings.Current.ScannerConnection;
        if (!string.IsNullOrWhiteSpace(connection))
        {
            try
            {
                await _scannerListener.StartAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Serial scanner could not start: {ex.Message}");
            }
        }
    }

    public void Navigate(string key)
    {
        _dialogs.CloseSaleCompleted();
        string? tab = null;
        if (key.Contains(':'))
        {
            var parts = key.Split(':', 2);
            key = parts[0];
            tab = parts[1];
        }

        if (!_pages.TryGetValue(key, out var page))
        {
            return;
        }

        if (page is InventoryViewModel inventory && tab is not null)
        {
            inventory.ShowTab(tab);
        }

        var nav = _allNav.FirstOrDefault(n => n.Key == key);
        if (nav is not null && !nav.IsVisible)
        {
            return;
        }

        if (page is IActivatable activatable)
        {
            activatable.Activate();
        }

        CurrentPage = page;
        PageTitle = nav?.Label ?? "";
        if (nav is not null && !ReferenceEquals(SelectedNavItem, nav))
        {
            _suppressNavigation = true;
            SelectedNavItem = nav;
            _suppressNavigation = false;
        }
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (!_suppressNavigation && value is not null)
        {
            Navigate(value.Key);
        }
    }

    public void FocusCheckoutSearch()
    {
        Navigate("checkout");
        _checkout.RequestFocus();
    }

    public async Task ChargeAsync()
    {
        Navigate("checkout");
        await _checkout.ChargeCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        try
        {
            var s = _settings.Current.Clone();
            s.Theme = _theme.IsDark ? AppTheme.Dark : AppTheme.Light;
            _settings.Save(s);
        }
        catch
        {
            // Theme still applies even if persistence fails.
        }
    }

    [RelayCommand]
    private void Logout() => LogoutRequested?.Invoke(this, EventArgs.Empty);

    public void ShutdownScanner()
    {
        try
        {
            _scannerListener.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // ignore
        }
    }

    private void OnScanned(object? sender, string code)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (CurrentPage is IScanTarget target)
            {
                target.HandleScan(code);
            }
        });
    }

    private void RefreshChrome()
    {
        var user = _session.CurrentUser;
        UserName = user?.FullName ?? "";
        Initials = user?.Initials ?? "?";
        RoleLabel = user is null ? "" : _loc.Get($"users.{user.Role.ToString().ToLowerInvariant()}");
        BusinessName = string.IsNullOrWhiteSpace(_settings.Current.BusinessName) ? "RixelPOS" : _settings.Current.BusinessName;
        var open = _cash.Current;
        IsCashOpen = open is not null;
        CashStatus = open is null ? _loc.Get("cash.registerClosed") : _loc.Get("cash.registerOpen");
        DateText = _clock.LocalNow.ToString("ddd d MMM · HH:mm");
        BuildNavigation();
    }

    private void BuildNavigation()
    {
        var role = _session.CurrentUser?.Role ?? UserRole.Cashier;
        var nav = new List<NavItem>
        {
            new("dashboard", _loc.Get("nav.dashboard"), "IconDashboard", true),
            new("checkout", _loc.Get("nav.checkout"), "IconCart", RolePermissions.Can(role, Permission.Sell)),
            new("inventory", _loc.Get("nav.inventory"), "IconBox", RolePermissions.Can(role, Permission.ViewInventory)),
            new("customers", _loc.Get("nav.customers"), "IconUsers", RolePermissions.Can(role, Permission.ViewCustomers)),
            new("suppliers", _loc.Get("nav.suppliers"), "IconTruck", RolePermissions.Can(role, Permission.ViewSuppliers)),
            new("cash", _loc.Get("nav.cash"), "IconCash", RolePermissions.Can(role, Permission.ManageCash)),
            new("reports", _loc.Get("nav.reports"), "IconChart", RolePermissions.Can(role, Permission.ViewReports)),
            new("settings", _loc.Get("nav.settings"), "IconGear", RolePermissions.Can(role, Permission.ManageSettings))
        };

        _allNav.Clear();
        _allNav.AddRange(nav);
        NavItems.Clear();
        foreach (var item in nav.Where(n => n.IsVisible))
        {
            NavItems.Add(item);
        }

        if (SelectedNavItem is not null)
        {
            var still = NavItems.FirstOrDefault(n => n.Key == SelectedNavItem.Key);
            if (still is null)
            {
                Navigate("dashboard");
            }
        }
    }
}
