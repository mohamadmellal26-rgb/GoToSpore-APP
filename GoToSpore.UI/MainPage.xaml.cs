using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;

namespace GoToSpore.UI
{
    public partial class MainPage : ContentPage
    {
        private int _steps = 0;
        private double _distanceInMeters = 0;
        private int _activeSeconds = 0;

        private const double StepDistanceInMeters = 0.78;
        private const double ShakeThreshold = 2.5;
        private DateTime _lastShakeTime = DateTime.MinValue;

        private const string KeySteps = "saved_steps_count";
        private const string KeyDistance = "saved_distance_meters";
        private const string KeyActiveSeconds = "saved_active_seconds";

        private const double TargetRunningKm = 5.0;
        private const int TargetWalkingMinutes = 30;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // 1. التحقق من التوكن وقراءة بيانات المستخدم المحفوظة
            string authToken = Preferences.Default.Get("auth_token", string.Empty);

            if (string.IsNullOrEmpty(authToken))
            {
                await Navigation.PushModalAsync(new LoginPage());
                return;
            }

            // 2. تحميل البيانات المحفوظة سابقاً
            LoadSavedData();

            // 3. تشغيل الحساسات
            ToggleAccelerometer(true);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            ToggleAccelerometer(false);
            SaveCurrentData();
        }

        #region Data Persistence (Preferences)

        private void LoadSavedData()
        {
            _steps = Preferences.Default.Get(KeySteps, 0);
            _distanceInMeters = Preferences.Default.Get(KeyDistance, 0.0);
            _activeSeconds = Preferences.Default.Get(KeyActiveSeconds, 0);

            UpdateUIAndGoals();
        }

        private void SaveCurrentData()
        {
            Preferences.Default.Set(KeySteps, _steps);
            Preferences.Default.Set(KeyDistance, _distanceInMeters);
            Preferences.Default.Set(KeyActiveSeconds, _activeSeconds);
        }

        #endregion

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

            if (gForce > ShakeThreshold)
            {
                var now = DateTime.Now;
                var timeDiff = (now - _lastShakeTime).TotalMilliseconds;

                if (timeDiff > 300)
                {
                    if (_lastShakeTime != DateTime.MinValue && timeDiff < 2000)
                    {
                        _activeSeconds += (int)(timeDiff / 1000);
                    }
                    else
                    {
                        _activeSeconds += 1;
                    }

                    _lastShakeTime = now;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        AddStepAndDistance(1);
                    });
                }
            }
        }

        private void AddStepAndDistance(int stepIncrement)
        {
            _steps += stepIncrement;
            _distanceInMeters += stepIncrement * StepDistanceInMeters;

            SaveCurrentData();
            UpdateUIAndGoals();
        }

        private void UpdateUIAndGoals()
        {
            double distanceInKm = _distanceInMeters / 1000.0;
            double activeMinutes = _activeSeconds / 60.0;

            if (StepsLabel != null) StepsLabel.Text = _steps.ToString("N0");
            if (MetersLabel != null) MetersLabel.Text = $"{_distanceInMeters:F0} m";
            if (KmLabel != null) KmLabel.Text = distanceInKm.ToString("F2");

            if (RunGoalLabel != null) RunGoalLabel.Text = $"{distanceInKm:F2} / {TargetRunningKm} km";
            if (RunProgressBar != null) RunProgressBar.Progress = Math.Min(distanceInKm / TargetRunningKm, 1.0);

            if (WalkGoalLabel != null) WalkGoalLabel.Text = $"{(int)activeMinutes} / {TargetWalkingMinutes} min";
            if (WalkProgressBar != null) WalkProgressBar.Progress = Math.Min(activeMinutes / TargetWalkingMinutes, 1.0);
        }

        private void ResetTracker()
        {
            _steps = 0;
            _distanceInMeters = 0;
            _activeSeconds = 0;

            Preferences.Default.Remove(KeySteps);
            Preferences.Default.Remove(KeyDistance);
            Preferences.Default.Remove(KeyActiveSeconds);

            UpdateUIAndGoals();
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

            bool reset = await DisplayAlert("إعادة ضبط", "هل تريد إعادة تعيين العداد وإلغاء جميع البيانات المحفوظة والبدء من جديد؟", "نعم", "إلغاء");
            if (reset)
            {
                ResetTracker();
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            Preferences.Default.Remove("auth_token");
            Preferences.Default.Remove("user_name");
            Preferences.Default.Remove("full_name");

            await Navigation.PushModalAsync(new LoginPage());
        }

        #endregion
    }
}