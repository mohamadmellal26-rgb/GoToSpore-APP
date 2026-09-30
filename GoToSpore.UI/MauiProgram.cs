using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;

namespace GoToSpore.UI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit() // يشمل التسهيلات الأساسية تلقائياً
            .UseMauiCommunityToolkitCamera() // تهيئة الكاميرا
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // تسجيل الصفحات لحل مشكلة Dependency Injection ومنع Crash الـ TargetInvocationException
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<ActivitiesPage>();
        builder.Services.AddTransient<ProfilePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}