namespace ImageGenerator.Localization;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Localization;

#pragma warning disable CA1812
internal sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly JsonStringLocalizer localizer;

    public JsonStringLocalizerFactory(IHostEnvironment env)
    {
        var resourcesPath = Path.Combine(env.ContentRootPath, "Resources");
        localizer = new JsonStringLocalizer(resourcesPath);
    }

    public IStringLocalizer Create(Type resourceSource) => localizer;

    public IStringLocalizer Create(string baseName, string location) => localizer;
}

internal sealed class JsonStringLocalizer : IStringLocalizer
{
    private readonly string resourcesPath;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> cache = new(StringComparer.OrdinalIgnoreCase);

    public JsonStringLocalizer(string resourcesPath)
    {
        this.resourcesPath = resourcesPath;
    }

    public LocalizedString this[string name]
    {
        get
        {
            var value = GetString(name);
            return new LocalizedString(name, value ?? name, resourceNotFound: value is null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = GetString(name);
#pragma warning disable CA1305
            var formatted = value is not null
                ? String.Format(CultureInfo.CurrentUICulture, value, arguments)
                : name;
#pragma warning restore CA1305
            return new LocalizedString(name, formatted, resourceNotFound: value is null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var culture = CultureInfo.CurrentUICulture;
        var dict = GetDictionary(culture.IsNeutralCulture ? culture.Name : culture.Parent.Name);
        return dict.Select(kv => new LocalizedString(kv.Key, kv.Value));
    }

    private string? GetString(string name)
    {
        var culture = CultureInfo.CurrentUICulture;

        if (!culture.IsNeutralCulture)
        {
            var dict = GetDictionary(culture.Name);
            if (dict.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        {
            var neutralName = culture.IsNeutralCulture ? culture.Name : culture.Parent.Name;
            var dict = GetDictionary(neutralName);
            if (dict.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        {
            var dict = GetDictionary("en");
            return dict.TryGetValue(name, out var value) ? value : null;
        }
    }

    private IReadOnlyDictionary<string, string> GetDictionary(string cultureName)
        => cache.GetOrAdd(cultureName, LoadDictionary);

    private IReadOnlyDictionary<string, string> LoadDictionary(string cultureName)
    {
        var path = Path.Combine(resourcesPath, $"{cultureName}.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (IOException)
        {
            return new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
#pragma warning restore CA1812
