using System.Text.Json;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.Localization;

public sealed class LocalizationService
{
    private Dictionary<string, string> _spanish = new();
    private Dictionary<string, string> _english = new();
    private Dictionary<string, string> _current = new();

    public static LocalizationService Instance { get; } = new();

    private LocalizationService()
    {
        _spanish = Load("es");
        _english = Load("en");
        _current = _spanish;
    }

    public AppLanguage Language { get; private set; } = AppLanguage.Spanish;

    public void SetLanguage(AppLanguage language)
    {
        Language = language;
        _current = language == AppLanguage.English ? _english : _spanish;
    }

    public string Get(string key)
    {
        if (_current.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_spanish.TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }

    public string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(Get(key), args);
        }
        catch
        {
            return Get(key);
        }
    }

    private static Dictionary<string, string> Load(string language)
    {
        try
        {
            var assembly = typeof(LocalizationService).Assembly;
            using var stream = assembly.GetManifestResourceStream($"RixelPOS.Desktop.Localization.{language}.json");
            if (stream is null)
            {
                return new Dictionary<string, string>();
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizationService.Instance.Get(Key);
}
