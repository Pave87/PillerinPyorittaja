using MauiBlazorHybrid.Services;
using Microsoft.AspNetCore.Components;

namespace MauiBlazorHybrid.Extensions
{
    public class LocalizeComponent : ComponentBase, IDisposable
    {
        [Inject] protected ILocalizationService LocalizationService { get; set; } = default!;

        protected string L(string key) => LocalizationService.GetString(key);

        protected string L(string key, params object[] args) => LocalizationService.GetString(key, args);

        protected string LF(string key, Dictionary<string, object> parameters) => LocalizationService.Format(key, parameters);

        protected string GetRepetitionText(int repetition, string frequency) => LocalizationService.GetRepetitionText(repetition, frequency);

        protected override void OnInitialized()
        {
            LocalizationService.CultureChanged += OnCultureChanged;
            base.OnInitialized();
        }

        private void OnCultureChanged(object? sender, EventArgs e)
        {
            InvokeAsync(StateHasChanged);
        }

        public void Dispose()
        {
            LocalizationService.CultureChanged -= OnCultureChanged;
        }
    }
}
