using SkinForge.Application.Services;
using Xunit;

namespace SkinForge.Tests;

public class LocalizationTests
{
    [Fact]
    public void SupportedLanguages_ContainsAll14Languages()
    {
        var service = new LocalizationService(string.Empty);
        var expected = new[] { "en", "tr", "de", "fr", "es", "pt", "ja", "ko", "zh", "zh-tw", "ru", "pl", "it", "vi" };

        Assert.Equal(14, service.SupportedLanguages.Count);
        foreach (var lang in expected)
        {
            Assert.Contains(lang, service.SupportedLanguages);
        }
    }

    [Theory]
    [InlineData("en", "SearchPlaceholder")]
    [InlineData("tr", "SearchPlaceholder")]
    [InlineData("de", "SearchPlaceholder")]
    [InlineData("fr", "SearchPlaceholder")]
    [InlineData("es", "SearchPlaceholder")]
    [InlineData("pt", "SearchPlaceholder")]
    [InlineData("ja", "SearchPlaceholder")]
    [InlineData("ko", "SearchPlaceholder")]
    [InlineData("zh", "SearchPlaceholder")]
    [InlineData("zh-tw", "SearchPlaceholder")]
    [InlineData("ru", "SearchPlaceholder")]
    [InlineData("pl", "SearchPlaceholder")]
    [InlineData("it", "SearchPlaceholder")]
    [InlineData("vi", "SearchPlaceholder")]
    public void GetString_ReturnsValidStringForEverySupportedLanguage(string lang, string key)
    {
        var service = new LocalizationService(string.Empty);
        service.SetLanguage(lang);

        var val = service.GetString(key);
        Assert.False(string.IsNullOrWhiteSpace(val));
        Assert.NotEqual(key, val); // Must be translated, not raw key
    }

    [Fact]
    public void SetLanguage_TriggersLanguageChangedEvent()
    {
        var service = new LocalizationService(string.Empty);
        var triggered = false;
        service.LanguageChanged += (_, _) => triggered = true;

        service.SetLanguage("tr");

        Assert.True(triggered);
        Assert.Equal("tr", service.CurrentLanguage);
    }

    [Fact]
    public void FallbackToEnglish_WhenKeyNotFoundInTargetLanguage()
    {
        var service = new LocalizationService(string.Empty);
        service.SetLanguage("de");

        // Request a key that might only exist in English
        var val = service.GetString("AppTitle");
        Assert.Equal("SkinForge", val);
    }
}
