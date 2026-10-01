using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
                // 1. Read directly from local preferences
                string savedUsername = Preferences.Default.Get("user_name", string.Empty);
                string savedFullName = Preferences.Default.Get("full_name", string.Empty);
                string avatarPath = Preferences.Default.Get("user_avatar_path", string.Empty);
                double localDistanceMeters = Preferences.Default.Get("saved_distance_meters", 0.0);

                if (!string.IsNullOrEmpty(avatarPath) && File.Exists(avatarPath))
                {
                    ImgAvatar.Source = ImageSource.FromFile(avatarPath);
                }

                if (!string.IsNullOrEmpty(savedUsername))
                {
                    LblUsername.Text = $"@{savedUsername}";
                    LblFullName.Text = !string.IsNullOrEmpty(savedFullName) ? savedFullName : savedUsername;
                }

                double localKm = localDistanceMeters / 1000.0;
                LblTotalDistance.Text = $"{localKm:F2} km";
                LblDistanceMeters.Text = $"({localDistanceMeters:N0} m)";

                // 2. Validate token
                string token = Preferences.Default.Get("auth_token", string.Empty);

                if (string.IsNullOrEmpty(token))
                {
                    await Navigation.PushModalAsync(new LoginPage());
                    return;
                }

                // 3. Fetch server data
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

                        double finalDistanceMeters = Math.Max(profile.TotalDistanceMeters, localDistanceMeters);
                        double totalKm = finalDistanceMeters / 1000.0;
                        
                        LblTotalDistance.Text = $"{totalKm:F2} km";
                        LblDistanceMeters.Text = $"({finalDistanceMeters:N0} m)";

                        LblMaxSpeed.Text = $"{profile.MaxSpeedKmh:F1} km/h";
                        LblTotalActivities.Text = profile.TotalActivities.ToString();
                        LblCalories.Text = $"{profile.TotalCaloriesBurned:N0} kcal";
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    ClearUserData();
                    await Navigation.PushModalAsync(new LoginPage());
                }
            }
            catch (Exception)
            {
                // Fallback to local cached data on connection error
            }
        }

        private async void BtnLogout_Clicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Confirmation", "Are you sure you want to log out?", "Yes", "Cancel");
            if (confirm)
            {
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

        private void ClearUserData()
        {
            Preferences.Default.Remove("auth_token");
            Preferences.Default.Remove("user_name");
            Preferences.Default.Remove("full_name");
            Preferences.Default.Remove("user_avatar_path");
            Preferences.Default.Remove("saved_steps_count");
            Preferences.Default.Remove("saved_distance_meters");
            Preferences.Default.Remove("saved_active_seconds");
        }
    }

    public class UserProfileDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

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