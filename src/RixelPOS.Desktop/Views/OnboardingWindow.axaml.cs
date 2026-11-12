using Avalonia.Controls;
using Avalonia.Media;
using RixelPOS.Desktop.Localization;

namespace RixelPOS.Desktop.Views;

public partial class OnboardingWindow : Window
{
    private int _step;

    public OnboardingWindow()
    {
        InitializeComponent();
        RixelPOS.Desktop.Services.WindowSizing.ClampOnOpened(this);
        Render();
    }

    private void NextClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_step >= 2)
        {
            Close(true);
            return;
        }

        _step++;
        Render();
    }

    private void SkipClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);

    private void Render()
    {
        var loc = LocalizationService.Instance;
        var (icon, titleKey, bodyKey) = _step switch
        {
            0 => ("▣", "onboarding.slide1Title", "onboarding.slide1Body"),
            1 => ("◈", "onboarding.slide2Title", "onboarding.slide2Body"),
            _ => ("✓", "onboarding.slide3Title", "onboarding.slide3Body")
        };

        IconText.Text = icon;
        TitleText.Text = loc.Get(titleKey);
        BodyText.Text = loc.Get(bodyKey);
        StepText.Text = $"{_step + 1} / 3";
        NextButton.Content = loc.Get(_step >= 2 ? "onboarding.startSelling" : "common.next");
    }
}
