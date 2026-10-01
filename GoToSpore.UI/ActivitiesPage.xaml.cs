using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;

namespace GoToSpore.UI
{
    public partial class ActivitiesPage : ContentPage
    {
        // مفاتيح حفظ حالات التحديات والبيانات في Preferences
        private const string KeyRunChallengeActive = "challenge_run_active";
        private const string KeyCalorieChallengeActive = "challenge_calorie_active";
        private const string KeyWalkChallengeActive = "challenge_walk_active";

        private const string KeyRunProgressMeters = "challenge_run_progress_meters";
        private const string KeyCalorieProgressKcal = "challenge_calorie_progress_kcal";
        private const string KeyWalkProgressSteps = "challenge_walk_progress_steps";

        // أهداف التحديات
        private const double TargetRunMeters = 20000.0; // 20 KM
        private const double TargetCalorieKcal = 3000.0; // 3000 Kcal
        private const int TargetWalkSteps = 50000;      // 50,000 Steps

        // معايير الحركة وحساب السعرات/الخطوات
        private const double StepDistanceMeters = 0.78;
        private const double CaloriesPerStep = 0.04; // متوسط السعرات الحرارية لكل خطوة
        private const double ShakeThreshold = 2.5;
        private DateTime _lastShakeTime = DateTime.MinValue;

        // حالات التنشيط
        private bool _isRunChallengeActive = false;
        private bool _isCalorieChallengeActive = false;
        private bool _isWalkChallengeActive = false;

        // القيم المقطوعة الحالية
        private double _runProgressMeters = 0;
        private double _calorieProgressKcal = 0;
        private int _walkProgressSteps = 0;

        public ActivitiesPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            LoadSavedData();
            EvaluateSensorState();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            ToggleAccelerometer(false);
            SaveCurrentData();
        }

        #region Data Persistence

        private void LoadSavedData()
        {
            _isRunChallengeActive = Preferences.Default.Get(KeyRunChallengeActive, false);
            _isCalorieChallengeActive = Preferences.Default.Get(KeyCalorieChallengeActive, false);
            _isWalkChallengeActive = Preferences.Default.Get(KeyWalkChallengeActive, false);

            _runProgressMeters = Preferences.Default.Get(KeyRunProgressMeters, 0.0);
            _calorieProgressKcal = Preferences.Default.Get(KeyCalorieProgressKcal, 0.0);
            _walkProgressSteps = Preferences.Default.Get(KeyWalkProgressSteps, 0);

            UpdateUI();
        }

        private void SaveCurrentData()
        {
            Preferences.Default.Set(KeyRunChallengeActive, _isRunChallengeActive);
            Preferences.Default.Set(KeyCalorieChallengeActive, _isCalorieChallengeActive);
            Preferences.Default.Set(KeyWalkChallengeActive, _isWalkChallengeActive);

            Preferences.Default.Set(KeyRunProgressMeters, _runProgressMeters);
            Preferences.Default.Set(KeyCalorieProgressKcal, _calorieProgressKcal);
            Preferences.Default.Set(KeyWalkProgressSteps, _walkProgressSteps);
        }

        #endregion

        #region Sensor Operations

        private void EvaluateSensorState()
        {
            // إذا كان أي تحدٍّ نشطاً، يتم تشغيل الحساس
            bool shouldRunSensor = _isRunChallengeActive || _isCalorieChallengeActive || _isWalkChallengeActive;
            ToggleAccelerometer(shouldRunSensor);
        }

        private void ToggleAccelerometer(bool enable)
        {
            try
            {
                if (!Accelerometer.Default.IsSupported)
                    return;

                if (enable)
                {
                    if (!Accelerometer.Default.IsMonitoring)
                    {
                        Accelerometer.Default.ReadingChanged -= OnAccelerometerReadingChanged;
                        Accelerometer.Default.ReadingChanged += OnAccelerometerReadingChanged;
                        Accelerometer.Default.Start(SensorSpeed.UI);
                    }
                }
                else
                {
                    if (Accelerometer.Default.IsMonitoring)
                    {
                        Accelerometer.Default.ReadingChanged -= OnAccelerometerReadingChanged;
                        Accelerometer.Default.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ACTIVITIES ACCELEROMETER ERROR]: {ex.Message}");
            }
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
                    _lastShakeTime = now;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ProcessStep();
                    });
                }
            }
        }

        private void ProcessStep()
        {
            // تحديث التحديات النشطة فقط
            if (_isRunChallengeActive)
            {
                _runProgressMeters += StepDistanceMeters;
            }

            if (_isCalorieChallengeActive)
            {
                _calorieProgressKcal += CaloriesPerStep;
            }

            if (_isWalkChallengeActive)
            {
                _walkProgressSteps += 1;
            }

            SaveCurrentData();
            UpdateUI();
        }

        #endregion

        #region UI Updates

        private void UpdateUI()
        {
            // 1. Run Challenge UI
            double runKm = _runProgressMeters / 1000.0;
            double runTargetKm = TargetRunMeters / 1000.0;
            double runRatio = Math.Min(_runProgressMeters / TargetRunMeters, 1.0);

            LblRunProgressText.Text = $"Progress: {runKm:F2} km / {runTargetKm:F0} km";
            LblRunPercentage.Text = $"{runRatio * 100:F0}%";
            ProgressRun.Progress = runRatio;

            if (_isRunChallengeActive)
            {
                BtnToggleRunChallenge.Text = "Stop Challenge";
                BtnToggleRunChallenge.BackgroundColor = Color.FromArgb("#333333");
                BtnToggleRunChallenge.TextColor = Color.FromArgb("#FFFFFF");
            }
            else
            {
                BtnToggleRunChallenge.Text = "Start Challenge";
                BtnToggleRunChallenge.BackgroundColor = Color.FromArgb("#F97316");
                BtnToggleRunChallenge.TextColor = Color.FromArgb("#FFFFFF");
            }

            // 2. Calorie Challenge UI
            double calorieRatio = Math.Min(_calorieProgressKcal / TargetCalorieKcal, 1.0);

            LblCalorieProgressText.Text = $"Progress: {_calorieProgressKcal:F0} / {TargetCalorieKcal:N0} kcal";
            LblCaloriePercentage.Text = $"{calorieRatio * 100:F0}%";
            ProgressCalorie.Progress = calorieRatio;

            if (_isCalorieChallengeActive)
            {
                BtnToggleCalorieChallenge.Text = "Stop Challenge";
                BtnToggleCalorieChallenge.BackgroundColor = Color.FromArgb("#333333");
                BtnToggleCalorieChallenge.TextColor = Color.FromArgb("#FFFFFF");
            }
            else
            {
                BtnToggleCalorieChallenge.Text = "Start Challenge";
                BtnToggleCalorieChallenge.BackgroundColor = Color.FromArgb("#FFFFFF");
                BtnToggleCalorieChallenge.TextColor = Color.FromArgb("#333333");
            }

            // 3. Walk Challenge UI
            double walkRatio = Math.Min((double)_walkProgressSteps / TargetWalkSteps, 1.0);

            LblWalkProgressText.Text = $"Progress: {_walkProgressSteps:N0} / {TargetWalkSteps:N0} Steps";
            LblWalkPercentage.Text = $"{walkRatio * 100:F0}%";
            ProgressWalk.Progress = walkRatio;

            if (_isWalkChallengeActive)
            {
                BtnToggleWalkChallenge.Text = "Stop Challenge";
                BtnToggleWalkChallenge.BackgroundColor = Color.FromArgb("#333333");
                BtnToggleWalkChallenge.TextColor = Color.FromArgb("#FFFFFF");
            }
            else
            {
                BtnToggleWalkChallenge.Text = "Start Challenge";
                BtnToggleWalkChallenge.BackgroundColor = Color.FromArgb("#FFFFFF");
                BtnToggleWalkChallenge.TextColor = Color.FromArgb("#333333");
            }
        }

        #endregion

        #region Event Handlers

        private async void OnToggleRunChallengeClicked(object sender, EventArgs e)
        {
            _isRunChallengeActive = !_isRunChallengeActive;
            EvaluateSensorState();
            SaveCurrentData();
            UpdateUI();

            if (_isRunChallengeActive)
                await DisplayAlert("Started", "You have started the Weekly 20KM Running Challenge!", "OK");
            else
                await DisplayAlert("Stopped", "Weekly Running Challenge paused.", "OK");
        }

        private async void OnToggleCalorieChallengeClicked(object sender, EventArgs e)
        {
            _isCalorieChallengeActive = !_isCalorieChallengeActive;
            EvaluateSensorState();
            SaveCurrentData();
            UpdateUI();

            if (_isCalorieChallengeActive)
                await DisplayAlert("Started", "You have started the Calorie Burner Challenge!", "OK");
            else
                await DisplayAlert("Stopped", "Calorie Burner Challenge paused.", "OK");
        }

        private async void OnToggleWalkChallengeClicked(object sender, EventArgs e)
        {
            _isWalkChallengeActive = !_isWalkChallengeActive;
            EvaluateSensorState();
            SaveCurrentData();
            UpdateUI();

            if (_isWalkChallengeActive)
                await DisplayAlert("Started", "You have started the 50K Steps Challenge!", "OK");
            else
                await DisplayAlert("Stopped", "50K Steps Challenge paused.", "OK");
        }

        #endregion
    }
}