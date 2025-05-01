// MauiBlazorHybrid/Services/AdService.cs
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;

namespace MauiBlazorHybrid.Services
{
    public interface IAdService
    {
        bool IsAdBannerReady { get; }
        Task InitializeAdsAsync();
        Task LoadBannerAdAsync(ElementReference adContainer);
    }

    public class AdService : IAdService
    {
        private bool _initialized = false;
        public bool IsAdBannerReady { get; private set; } = false;

        public async Task InitializeAdsAsync()
        {
            if (_initialized)
                return;

#if ANDROID
            try
            {
                // Initialize Google Mobile Ads SDK
                Android.Gms.Ads.MobileAds.Initialize(Android.App.Application.Context);
                _initialized = true;

                // Wait a moment to ensure initialization completes
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize ads: {ex.Message}");
                Console.WriteLine($"Exception stack trace: {ex.StackTrace}");
            }
#else
                // For non-Android platforms, just mark as initialized
                _initialized = true;
#endif
        }

        public async Task LoadBannerAdAsync(ElementReference adContainer)
        {
#if ANDROID
            try
            {
                if (!_initialized)
                {
                    await InitializeAdsAsync();
                }

                // Get the current activity
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity == null)
                {
                    return;
                }

                // Run on UI thread
                activity.RunOnUiThread(() =>
                {
                    try
                    {
                        // Create ad view
                        var adView = new Android.Gms.Ads.AdView(activity)
                        {
                            AdUnitId = "ca-app-pub-3940256099942544/6300978111", // Google's test ad unit ID
                            AdSize = Android.Gms.Ads.AdSize.Banner
                        };

                        // Create ad request
                        var adRequest = new Android.Gms.Ads.AdRequest.Builder().Build();

                        // Register event handler for ad loading
                        adView.AdListener = new AdListener(this);

                        // Load ad
                        adView.LoadAd(adRequest);

                        // Find parent view to add our ad container to
                        Android.Widget.FrameLayout rootView = activity.FindViewById<Android.Widget.FrameLayout>(Android.Resource.Id.Content);
                        if (rootView != null)
                        {
                            // Create layout parameters for positioning at bottom
                            var layoutParams = new Android.Widget.FrameLayout.LayoutParams(
                                Android.Widget.FrameLayout.LayoutParams.MatchParent,
                                Android.Widget.FrameLayout.LayoutParams.WrapContent);
                            layoutParams.Gravity = Android.Views.GravityFlags.Bottom | Android.Views.GravityFlags.CenterHorizontal;

                            // See if we need to remove any existing ad view first
                            for (int i = 0; i < rootView.ChildCount; i++)
                            {
                                var childView = rootView.GetChildAt(i);
                                if (childView is Android.Widget.LinearLayout linearLayout &&
                                    linearLayout.Tag?.ToString() == "AdBannerContainer")
                                {
                                    rootView.RemoveView(childView);
                                    break;
                                }
                            }

                            // Create a container for the ad
                            var containerLayout = new Android.Widget.LinearLayout(activity)
                            {
                                Orientation = Android.Widget.Orientation.Vertical,
                                Tag = "AdBannerContainer"
                            };
                            containerLayout.SetGravity(Android.Views.GravityFlags.Center);

                            // Add the ad view to our container
                            containerLayout.AddView(adView);

                            // Add our container to the root view
                            rootView.AddView(containerLayout, layoutParams);
                        }
                        else
                        {
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading banner ad: {ex.Message}");
                        Console.WriteLine($"Exception stack trace: {ex.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load banner ad: {ex.Message}");
                Console.WriteLine($"Exception stack trace: {ex.StackTrace}");
            }
#else
                // For other platforms, just mark it as ready
                IsAdBannerReady = true;
#endif
        }

#if ANDROID
        // Custom AdListener to track ad loading events
        private class AdListener : Android.Gms.Ads.AdListener
        {
            private readonly AdService _adService;

            public AdListener(AdService adService)
            {
                _adService = adService;
            }

            public override void OnAdLoaded()
            {
                base.OnAdLoaded();
                _adService.IsAdBannerReady = true;
            }

            public override void OnAdFailedToLoad(Android.Gms.Ads.LoadAdError error)
            {
                base.OnAdFailedToLoad(error);
                _adService.IsAdBannerReady = false;
            }
        }
#endif
    }
}
