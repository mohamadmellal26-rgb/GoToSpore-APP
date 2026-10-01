using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoToSpore.UI
{
    public partial class MainPage : ContentPage
    {
        private int _steps = 0;
        private double _distanceInMeters = 0;
        private double _distanceInKm = 0;
        private int _activeSeconds = 0;
        private double _caloriesBurned = 0;
        private double _estimatedSpeedKmh = 0;

        private const double StepDistanceInMeters = 0.78;
        private const double CaloriesPerStep = 0.04;
        private const double ShakeThreshold = 2.5;
        private DateTime _lastShakeTime = DateTime.MinValue;

        private const string KeySteps = "saved_steps_count";
        private const string KeyDistanceMeters = "saved_distance_meters";
        private const string KeyDistanceKm = "saved_distance_km";
        private const string KeyActiveSeconds = "saved_active_seconds";
        private const string KeyCalories = "saved_calories_burned";

        private const double TargetRunningKm = 5.0;
        private const int TargetWalkingMinutes = 30;

        private const string ApiBaseUrl = "http://10.0.2.2:8080/api";
        private readonly HttpClient _httpClient = new HttpClient();

        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            string authToken = Preferences.Default.Get("auth_token", string.Empty);

            if (string.IsNullOrEmpty(authToken))
            {
                await Navigation.PushModalAsync(new LoginPage());
                return;
            }

            // 1. تحميل البيانات من السيرفر والذاكرة المحلية
            await SyncDataFromApiAsync(authToken);

            // 2. تشغيل الحساسات
            ToggleAccelerometer(true);
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            ToggleAccelerometer(false);

            SaveCurrentDataLocally();
            await SyncDataToApiAsync();
        }

        #region Data Persistence & API Sync

        private async Task SyncDataFromApiAsync(string token)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/user/profile");

                if (response.IsSuccessStatusCode)
                {
                    // تصحيح: استخدام response.Content.ReadAsStringAsync()
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("total_distance_meters", out var dist))
                        _distanceInMeters = dist.GetDouble();

                    if (root.TryGetProperty("total_calories", out var cal))
                        _caloriesBurned = cal.GetDouble();

                    _distanceInKm = _distanceInMeters / 1000.0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API FETCH ERROR]: {ex.Message}");
                // في حال انقطاع النت، الاعتماد على البيانات المحلية
                LoadLocalSavedData();
            }

            UpdateUIAndGoals();
        }

        private async Task SyncDataToApiAsync()
        {
            try
            {
                string token = Preferences.Default.Get("auth_token", string.Empty);
                if (string.IsNullOrEmpty(token)) return;

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var payload = new
                {
                    distance_meters = _distanceInMeters,
                    calories = _caloriesBurned,
                    speed_kmh = _estimatedSpeedKmh
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await _httpClient.PostAsync($"{ApiBaseUrl}/user/stats", content);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API SAVE ERROR]: {ex.Message}");
            }
        }

        private void LoadLocalSavedData()
        {
            _steps = Preferences.Default.Get(KeySteps, 0);
            _distanceInMeters = Preferences.Default.Get(KeyDistanceMeters, 0.0);
            _distanceInKm = Preferences.Default.Get(KeyDistanceKm, _distanceInMeters / 1000.0);
            _activeSeconds = Preferences.Default.Get(KeyActiveSeconds, 0);
            _caloriesBurned = Preferences.Default.Get(KeyCalories, _steps * CaloriesPerStep);
        }

        private void SaveCurrentDataLocally()
        {
            Preferences.Default.Set(KeySteps, _steps);
            Preferences.Default.Set(KeyDistanceMeters, _distanceInMeters);
            Preferences.Default.Set(KeyDistanceKm, _distanceInKm);
            Preferences.Default.Set(KeyActiveSeconds, _activeSeconds);
            Preferences.Default.Set(KeyCalories, _caloriesBurned);
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
                    var sensorLabel = this.FindByName<Label>("SensorStatusLabel");
                    if (sensorLabel != null)
                    {
                        sensorLabel.Text = statusText;
                        sensorLabel.TextColor = Color.FromArgb(hexColor);
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
            _distanceInKm = _distanceInMeters / 1000.0;
            _caloriesBurned += stepIncrement * CaloriesPerStep;

            if (_activeSeconds > 0)
            {
                double activeHours = _activeSeconds / 3600.0;
                _estimatedSpeedKmh = _distanceInKm / activeHours;
            }

            SaveCurrentDataLocally();
            UpdateUIAndGoals();

            // حفظ دوري للسيرفر كل 10 خطوات للحفاظ على المزامنة دون التعطيل
            if (_steps % 10 == 0)
            {
                _ = SyncDataToApiAsync();
            }
        }

        private void UpdateUIAndGoals()
        {
            double activeMinutes = _activeSeconds / 60.0;

            var stepsLabel = this.FindByName<Label>("StepsLabel");
            if (stepsLabel != null) stepsLabel.Text = _steps.ToString("N0");

            var metersLabel = this.FindByName<Label>("MetersLabel");
            if (metersLabel != null) metersLabel.Text = $"{_distanceInMeters:F0} m";

            var kmLabel = this.FindByName<Label>("KmLabel");
            if (kmLabel != null) kmLabel.Text = $"{_distanceInKm:F2} km";

            var caloriesLabel = this.FindByName<Label>("CaloriesLabel");
            if (caloriesLabel != null) caloriesLabel.Text = $"{_caloriesBurned:F1} kcal";

            var speedLabel = this.FindByName<Label>("SpeedLabel");
            if (speedLabel != null) speedLabel.Text = $"{_estimatedSpeedKmh:F1} km/h";

            var runGoalLabel = this.FindByName<Label>("RunGoalLabel");
            if (runGoalLabel != null) runGoalLabel.Text = $"{_distanceInKm:F2} / {TargetRunningKm} km";

            var runProgressBar = this.FindByName<ProgressBar>("RunProgressBar");
            if (runProgressBar != null) runProgressBar.Progress = Math.Min(_distanceInKm / TargetRunningKm, 1.0);

            var walkGoalLabel = this.FindByName<Label>("WalkGoalLabel");
            if (walkGoalLabel != null) walkGoalLabel.Text = $"{(int)activeMinutes} / {TargetWalkingMinutes} min";

            var walkProgressBar = this.FindByName<ProgressBar>("WalkProgressBar");
            if (walkProgressBar != null) walkProgressBar.Progress = Math.Min(activeMinutes / TargetWalkingMinutes, 1.0);
        }

        private void ResetTracker()
        {
            _steps = 0;
            _distanceInMeters = 0;
            _distanceInKm = 0;
            _activeSeconds = 0;
            _caloriesBurned = 0;
            _estimatedSpeedKmh = 0;

            Preferences.Default.Remove(KeySteps);
            Preferences.Default.Remove(KeyDistanceMeters);
            Preferences.Default.Remove(KeyDistanceKm);
            Preferences.Default.Remove(KeyActiveSeconds);
            Preferences.Default.Remove(KeyCalories);

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
                    ? (isMonitoring ? "الحسّاس نشط ويعمل حالياً لمتابعة الخطوات والمسافات." : "الحسّاس متوقف حالياً.") 
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

        private async void OnLogoutClicked(object? sender, EventArgs e)
        {
            Preferences.Default.Remove("auth_token");
            Preferences.Default.Remove("user_name");
            Preferences.Default.Remove("full_name");

            await Navigation.PushModalAsync(new LoginPage());
        }

        #endregion
    }
}