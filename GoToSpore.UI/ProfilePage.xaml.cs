using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GoToSpore.UI
{
    public partial class ProfilePage : ContentPage
    {
        private static readonly HttpClient httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://gotospore-server.onrender.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public ProfilePage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadUserProfileDataAsync();
        }

        private async Task LoadUserProfileDataAsync()
        {
            try
            {
                // 1. القراءة أولاً من البيانات المحفوظة محلياً
                string savedUsername = Preferences.Default.Get("user_name", string.Empty);
                string savedFullName = Preferences.Default.Get("full_name", string.Empty);
                string avatarPath = Preferences.Default.Get("user_avatar_path", string.Empty);
                string avatarUrl = Preferences.Default.Get("user_avatar_url", string.Empty);
                double localDistanceMeters = Preferences.Default.Get("saved_distance_meters", 0.0);
                double localCalories = Preferences.Default.Get("saved_calories_burned", 0.0);

                if (!string.IsNullOrEmpty(avatarPath) && File.Exists(avatarPath))
                {
                    ImgAvatar.Source = ImageSource.FromFile(avatarPath);
                }
                else if (!string.IsNullOrEmpty(avatarUrl))
                {
                    ImgAvatar.Source = avatarUrl;
                }

                if (!string.IsNullOrEmpty(savedUsername))
                {
                    LblUsername.Text = $"@{savedUsername}";
                    LblFullName.Text = !string.IsNullOrEmpty(savedFullName) ? savedFullName : savedUsername;
                }

                double localKm = localDistanceMeters / 1000.0;
                LblTotalDistance.Text = $"{localKm:F2} km";
                LblDistanceMeters.Text = $"({localDistanceMeters:N0} m)";
                LblCalories.Text = $"{localCalories:N0} kcal";

                // 2. التحقق من وجود التوكن
                string token = Preferences.Default.Get("auth_token", string.Empty);

                if (string.IsNullOrEmpty(token))
                {
                    await Navigation.PushModalAsync(new LoginPage());
                    return;
                }

                // 3. جلب البيانات الحديثة من السيرفر
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/user/profile");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var profile = JsonSerializer.Deserialize<UserProfileDto>(json, jsonOptions);

                    if (profile != null)
                    {
                        Preferences.Default.Set("user_name", profile.Username);
                        if (!string.IsNullOrEmpty(profile.FullName))
                        {
                            Preferences.Default.Set("full_name", profile.FullName);
                        }

                        LblFullName.Text = !string.IsNullOrEmpty(profile.FullName) ? profile.FullName : profile.Username;
                        LblUsername.Text = $"@{profile.Username}";

                        // عرض صورة الملف الشخصي المرجعة من السيرفر إذا وجدت
                        if (!string.IsNullOrEmpty(profile.ImageUrl))
                        {
                            Preferences.Default.Set("user_avatar_url", profile.ImageUrl);
                            ImgAvatar.Source = profile.ImageUrl;
                        }

                        // الدمج واختيار القيمة الأكبر بين السيرفر والمحلي
                        double finalDistanceMeters = Math.Max(profile.TotalDistanceMeters, localDistanceMeters);
                        double finalCalories = Math.Max(profile.TotalCaloriesBurned, localCalories);
                        double totalKm = finalDistanceMeters / 1000.0;
                        
                        // تحديث القيم المحفوظة محلياً لضمان عدم ضياعها
                        Preferences.Default.Set("saved_distance_meters", finalDistanceMeters);
                        Preferences.Default.Set("saved_calories_burned", finalCalories);

                        LblTotalDistance.Text = $"{totalKm:F2} km";
                        LblDistanceMeters.Text = $"({finalDistanceMeters:N0} m)";

                        LblMaxSpeed.Text = $"{profile.MaxSpeedKmh:F1} km/h";
                        LblTotalActivities.Text = profile.TotalActivities.ToString();
                        LblCalories.Text = $"{finalCalories:N0} kcal";
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    ClearUserData();
                    await Navigation.PushModalAsync(new LoginPage());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PROFILE LOAD ERROR]: {ex.Message}");
            }
        }

        private async void BtnLogout_Clicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Confirmation", "Are you sure you want to log out?", "Yes", "Cancel");
            if (confirm)
            {
                // 1. إرسال أحدث قيمة مسافة وسعرات للسيرفر قبل مسح البيانات محلياً
                await SyncStatsBeforeLogoutAsync();

                // 2. مسح البيانات المحفوظة محلياً
                ClearUserData();

                ImgAvatar.Source = "appiconfg.png";
                LblFullName.Text = string.Empty;
                LblUsername.Text = string.Empty;
                LblTotalDistance.Text = "0.0 km";
                LblDistanceMeters.Text = "(0 m)";
                LblMaxSpeed.Text = "0.0 km/h";
                LblTotalActivities.Text = "0";
                LblCalories.Text = "0 kcal";

                await Navigation.PushModalAsync(new LoginPage());
            }
        }

        private async Task SyncStatsBeforeLogoutAsync()
        {
            try
            {
                string token = Preferences.Default.Get("auth_token", string.Empty);
                double localDistanceMeters = Preferences.Default.Get("saved_distance_meters", 0.0);
                double localCalories = Preferences.Default.Get("saved_calories_burned", 0.0);

                if (string.IsNullOrEmpty(token) || localDistanceMeters <= 0) return;

                var payload = new
                {
                    distance_meters = localDistanceMeters,
                    calories = localCalories,
                    speed_kmh = 0.0
                };

                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "api/user/stats")
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                await httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOGOUT SYNC ERROR]: {ex.Message}");
            }
        }

        private void ClearUserData()
        {
            Preferences.Default.Remove("auth_token");
            Preferences.Default.Remove("user_name");
            Preferences.Default.Remove("full_name");
            Preferences.Default.Remove("user_avatar_path");
            Preferences.Default.Remove("user_avatar_url");
            Preferences.Default.Remove("saved_steps_count");
            Preferences.Default.Remove("saved_distance_meters");
            Preferences.Default.Remove("saved_calories_burned");
            Preferences.Default.Remove("saved_active_seconds");
        }
    }

    public class UserProfileDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; } = string.Empty;

        [JsonPropertyName("total_distance_meters")]
        public double TotalDistanceMeters { get; set; }

        [JsonPropertyName("max_speed_kmh")]
        public double MaxSpeedKmh { get; set; }

        [JsonPropertyName("total_activities")]
        public int TotalActivities { get; set; }

        [JsonPropertyName("total_calories")]
        public double TotalCaloriesBurned { get; set; }
    }
}