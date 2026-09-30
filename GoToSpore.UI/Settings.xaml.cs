using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace GoToSpore.UI
{
    public partial class SettingsPage : ContentPage
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            InitializeComponent();

            ViewModel = new SettingsViewModel();
            BindingContext = ViewModel;
        }

        #region User Actions & Events

        private async void OnClearCacheTapped(object sender, EventArgs e)
        {
            try
            {
                bool confirm = await DisplayAlert(
                    "مسح التخزين المؤقت", 
                    "هل أنت تأكد من رغبتك في مسح البيانات المؤقتة المحفوظة؟", 
                    "نعم", 
                    "إلغاء");

                if (confirm)
                {
                    await ViewModel.ClearCacheAsync();
                    await DisplayAlert("تمت العملية", "تم مسح الـ Cache بنجاح.", "موافق");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClearCache Error]: {ex.Message}");
            }
        }

        #endregion
    }

    public class SettingsViewModel : BindableObject
    {
        private bool _isAutoStartSensor = true;
        private bool _isHighPrecisionLocation = false;
        private bool _isActivitySummaryAlerts = true;
        private bool _isPersistentNotification = true;
        private string _cacheSize = "12.4 MB";

        public bool IsAutoStartSensor
        {
            get => _isAutoStartSensor;
            set
            {
                if (_isAutoStartSensor != value)
                {
                    _isAutoStartSensor = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsHighPrecisionLocation
        {
            get => _isHighPrecisionLocation;
            set
            {
                if (_isHighPrecisionLocation != value)
                {
                    _isHighPrecisionLocation = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsActivitySummaryAlerts
        {
            get => _isActivitySummaryAlerts;
            set
            {
                if (_isActivitySummaryAlerts != value)
                {
                    _isActivitySummaryAlerts = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsPersistentNotification
        {
            get => _isPersistentNotification;
            set
            {
                if (_isPersistentNotification != value)
                {
                    _isPersistentNotification = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CacheSize
        {
            get => _cacheSize;
            set
            {
                if (_cacheSize != value)
                {
                    _cacheSize = value;
                    OnPropertyChanged();
                }
            }
        }

        public async Task ClearCacheAsync()
        {
            await Task.Delay(400);
            CacheSize = "0.0 MB";
        }
    }
}