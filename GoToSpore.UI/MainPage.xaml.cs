using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using System;
using System.Diagnostics;

namespace GoToSpore.UI
{
    public partial class MainPage : ContentPage
    {
        private int _steps = 0;
        private double _distanceInMeters = 0;
        private const double StepDistanceInMeters = 0.78;
        private const double ShakeThreshold = 2.5;
        private DateTime _lastShakeTime = DateTime.MinValue;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // 1. التحقق من وجود التوكن
            string authToken = Preferences.Default.Get("auth_token", string.Empty);

            if (string.IsNullOrEmpty(authToken))
            {
                // إذا لم يكن مسجلاً، توجيهه لصفحة التسجيل/الدخول
                // في MAUI يُفضل الانتقال لنفذة Navigation أو Modal
                await Navigation.PushModalAsync(new LoginPage());
                return;
            }

            // 2. تشغيل الحساسات في حال كان مسجلاً
            ToggleAccelerometer(true);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            ToggleAccelerometer(false);
        }

        // دالة لتسجيل الخروج (يمكن استدعاؤها من زر الخروج في الواجهة)
        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            Preferences.Default.Remove("auth_token");
            await Navigation.PushModalAsync(new LoginPage());
        }

        private void ToggleAccelerometer(bool enable)
        {
            try
            {
                if (!Accelerometer.Default.IsSupported)
                {
                    UpdateSensorUIStatus("Not Supported", "#A3A3A3");
                    return;
                }

                if (enable)
                {
                    if (!Accelerometer.Default.IsMonitoring)
                    {
                        Accelerometer.Default.ReadingChanged -= OnAccelerometerReadingChanged;
                        Accelerometer.Default.ReadingChanged += OnAccelerometerReadingChanged;
                        Accelerometer.Default.Start(SensorSpeed.UI);
                        UpdateSensorUIStatus("Sensor Active", "#EAB308");
                    }
                }
                else
                {
                    if (Accelerometer.Default.IsMonitoring)
                    {
                        Accelerometer.Default.ReadingChanged -= OnAccelerometerReadingChanged;
                        Accelerometer.Default.Stop();
                        UpdateSensorUIStatus("Sensor Stopped", "#EF4444");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ACCELEROMETER ERROR]: {ex.Message}");
                UpdateSensorUIStatus("Sensor Error", "#EF4444");
            }
        }

        private void UpdateSensorUIStatus(string statusText, string hexColor)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (SensorStatusLabel != null)
                    {
                        SensorStatusLabel.Text = statusText;
                        SensorStatusLabel.TextColor = Color.FromArgb(hexColor);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UI UPDATE ERROR]: {ex.Message}");
                }
            });
        }

        private void OnAccelerometerReadingChanged(object? sender, AccelerometerChangedEventArgs e)
        {
            var data = e.Reading;
            double gForce = Math.Sqrt(data.Acceleration.X * data.Acceleration.X +
                                      data.Acceleration.Y * data.Acceleration.Y +
                                      data.Acceleration.Z * data.Acceleration.Z);

            if (gForce > ShakeThreshold && (DateTime.Now - _lastShakeTime).TotalMilliseconds > 300)
            {
                _lastShakeTime = DateTime.Now;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AddStepAndDistance(1);
                });
            }
        }

        private void AddStepAndDistance(int stepIncrement)
        {
            _steps += stepIncrement;
            _distanceInMeters += stepIncrement * StepDistanceInMeters;

            double distanceInKm = _distanceInMeters / 1000.0;

            if (StepsLabel != null) StepsLabel.Text = _steps.ToString("N0");
            if (MetersLabel != null) MetersLabel.Text = $"{_distanceInMeters:F0} m";
            if (KmLabel != null) KmLabel.Text = distanceInKm.ToString("F2");

            try
            {
                SemanticScreenReader.Announce($"المسافة الآن {_distanceInMeters:F0} متر، أي ما يعادل {distanceInKm:F2} كيلومتر");
            }
            catch { }
        }

        private void ResetTracker()
        {
            _steps = 0;
            _distanceInMeters = 0;

            if (StepsLabel != null) StepsLabel.Text = "0";
            if (MetersLabel != null) MetersLabel.Text = "0 m";
            if (KmLabel != null) KmLabel.Text = "0.00";
        }

        #region XAML Event Handlers

        private async void OnSensorStatusTapped(object? sender, EventArgs e)
        {
            try
            {
                bool isSupported = Accelerometer.Default.IsSupported;
                bool isMonitoring = isSupported && Accelerometer.Default.IsMonitoring;

                string statusMessage = isSupported 
                    ? (isMonitoring ? "الحسّاس نشط ويعمل حالياً لمتابعة الخطوات." : "الحسّاس متوقف حالياً.") 
                    : "جهازك لا يدعم حساس التسارع (Accelerometer).";

                await DisplayAlert("حالة الحسّاس", statusMessage, "حسناً");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STATUS TAP ERROR]: {ex.Message}");
            }
        }

        private async void OnCounterTapped(object? sender, EventArgs e)
        {
            if (sender is VisualElement element)
            {
                await element.ScaleTo(0.95, 100);
                await element.ScaleTo(1.0, 100);
            }

            bool reset = await DisplayAlert("إعادة ضبط", "هل تريد إعادة تعيين العداد وإعادة حساب المسافة من جديد؟", "نعم", "إلغاء");
            if (reset)
            {
                ResetTracker();
            }
        }

        #endregion
    }
}