using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop;

public partial class App : Avalonia.Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _loggingOut;

    public static IServiceProvider? Services { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _desktop = desktop;

        if (SingleInstance.IsSecondary)
        {
            ShowAlreadyRunning();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        try
        {
            Services = AppServices.Build();
            var settings = Services.GetRequiredService<SettingsService>();
            LocalizationService.Instance.SetLanguage(settings.Current.Language);
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
                settings.Current.Language == RixelPOS.Domain.Enums.AppLanguage.English
                    ? new System.Globalization.CultureInfo("en-US")
                    : new System.Globalization.CultureInfo("es-DO");
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
            var themeService = new ThemeService();
            themeService.Apply(settings.Current.Theme);
            themeService.ApplyUiScale(settings.Current.UiScalePercent);

            if (!settings.Current.Configured || Services.GetRequiredService<IPosStore>().GetUsers().Count == 0)
            {
                ShowSetup();
            }
            else
            {
                ShowLogin();
            }
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowSetup()
    {
        var vm = Services!.GetRequiredService<SetupWizardViewModel>();
        var window = new SetupWizardWindow { DataContext = vm };
        vm.Completed += (_, _) => ShowLogin();
        Swap(window);
    }

    private void ShowLogin()
    {
        var vm = Services!.GetRequiredService<LoginViewModel>();
        var window = new LoginWindow { DataContext = vm };
        vm.LoginSucceeded += (_, _) => ShowMain();
        Swap(window);
        _loggingOut = false;
    }

    private void ShowMain()
    {
        var vm = Services!.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = vm };

        vm.LogoutRequested += (_, _) =>
        {
            _loggingOut = true;
            try
            {
                vm.ShutdownScanner();
                Services!.GetRequiredService<AuthService>().SignOut();
            }
            catch
            {
                // best effort
            }

            ShowLogin();
        };

        window.Closed += (_, _) =>
        {
            if (!_loggingOut)
            {
                TryAutoBackup();
            }
        };

        Swap(window);
        _ = vm.InitializeAsync();
        ShowOnboardingIfNeeded();
    }

    private void ShowOnboardingIfNeeded()
    {
        try
        {
            var settings = Services!.GetRequiredService<SettingsService>();
            if (settings.Current.OnboardingCompleted)
            {
                return;
            }

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var window = new OnboardingWindow();
                    if (window.Owner is null && _desktop?.MainWindow is not null)
                    {
                        await window.ShowDialog(_desktop.MainWindow);
                    }
                    else
                    {
                        window.Show();
                    }

                    var s = settings.Current.Clone();
                    s.OnboardingCompleted = true;
                    settings.Save(s);
                }
                catch
                {
                    // onboarding must never block the app
                }
            }, DispatcherPriority.Background);
        }
        catch
        {
            // ignore
        }
    }

    private void TryAutoBackup()
    {
        try
        {
            var settings = Services?.GetService<SettingsService>();
            var backup = Services?.GetService<IBackupService>();
            if (settings?.Current.AutoBackupOnClose == true && backup is not null)
            {
                backup.CreateBackup("cierre");
                backup.Prune(settings.Current.BackupKeepCount);
            }
        }
        catch
        {
            // never prevent shutdown because of a backup
        }
    }

    private void Swap(Window next)
    {
        var previous = _desktop?.MainWindow;
        _desktop!.MainWindow = next;
        next.Show();
        if (previous is not null && !ReferenceEquals(previous, next))
        {
            try
            {
                previous.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void ShowAlreadyRunning()
    {
        try
        {
            var panel = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = "RixelPOS",
                FontSize = 22,
                FontWeight = Avalonia.Media.FontWeight.Bold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "RixelPOS ya está abierto.\nRixelPOS is already running.",
                FontSize = 16,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Carpeta de datos / Data folder:\n{AppPaths.DataDirectory}",
                FontSize = 12,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            var button = new Button
            {
                Content = "Cerrar / Close",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };
            button.Click += (_, _) => _desktop?.Shutdown();
            panel.Children.Add(button);

            var window = new Window
            {
                Title = "RixelPOS",
                Width = 520,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = panel
            };
            WindowSizing.ClampOnOpened(window);
            _desktop!.MainWindow = window;
            window.Show();
        }
        catch
        {
            // Never leave a headless second process running.
            _desktop?.Shutdown();
        }
    }

    private void ShowStartupError(Exception exception)
    {
        try
        {
            var panel = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = "RixelPOS no pudo iniciar",
                FontSize = 24,
                FontWeight = Avalonia.Media.FontWeight.Bold
            });
            panel.Children.Add(new TextBlock
            {
                Text = exception.Message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 640
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Carpeta de datos: {AppPaths.DataDirectory}\nRevisa los archivos de registro en {AppPaths.LogsDirectory}",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 640
            });
            var button = new Button { Content = "Cerrar", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            button.Click += (_, _) => _desktop?.Shutdown();
            panel.Children.Add(button);

            var window = new Window
            {
                Title = "RixelPOS",
                Width = 720,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = panel
            };
            _desktop!.MainWindow = window;
            window.Show();
        }
        catch
        {
            // no UI left
        }
    }
}
