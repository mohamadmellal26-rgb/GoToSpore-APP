using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GoToSpore.UI
{
    public partial class LoginPage : ContentPage
    {
        private bool isRegisterMode = false;
        private string? selectedAvatarPath = null;

        private static readonly HttpClient httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://gotospore-server.onrender.com/"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public LoginPage()
        {
            InitializeComponent();

            Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("RemoveEntryBorders", (handler, view) =>
            {
#if ANDROID
                handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
                handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
            });
        }

        private async void OnPickAvatarTapped(object? sender, EventArgs e)
        {
            try
            {
                var result = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Choose Profile Picture"
                });

                if (result != null)
                {
                    selectedAvatarPath = result.FullPath;
                    ImgAvatarPreview.Source = ImageSource.FromFile(selectedAvatarPath);
                    Preferences.Default.Set("user_avatar_path", selectedAvatarPath);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to pick image: {ex.Message}", isError: true);
            }
        }

        private void BtnToggleMode_Click(object? sender, EventArgs? e)
        {
            isRegisterMode = !isRegisterMode;
            TxtStatus.Text = string.Empty;

            LayoutAvatarSelection.IsVisible = isRegisterMode;
            LayoutFullName.IsVisible = isRegisterMode;

            if (isRegisterMode)
            {
                TxtTitle.Text = "Create a new account";
                BtnSubmit.Text = "Sign Up";
                BtnToggleMode.Text = "Already have an account? Log In";
            }
            else
            {
                TxtTitle.Text = "Log in to your account";
                BtnSubmit.Text = "Log In";
                BtnToggleMode.Text = "Don't have an account? Sign Up";
            }
        }

        private async void BtnSubmit_Click(object sender, EventArgs e)
        {
            string username = TxtUsername.Text?.Trim() ?? string.Empty;
            string password = TxtPassword.Text ?? string.Empty;
            string fullName = TxtFullName.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowStatus("Please enter username and password.", isError: true);
                return;
            }

            SetLoadingState(isLoading: true);

            try
            {
                var payloadData = isRegisterMode 
                    ? new { username = username, password = password, full_name = fullName }
                    : (object)new { username = username, password = password };

                string jsonPayload = JsonSerializer.Serialize(payloadData);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string endpoint = isRegisterMode ? "api/register" : "api/login";

                HttpResponseMessage response = await httpClient.PostAsync(endpoint, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (isRegisterMode)
                    {
                        if (!string.IsNullOrEmpty(fullName))
                        {
                            Preferences.Default.Set("full_name", fullName);
                        }
                        if (!string.IsNullOrEmpty(selectedAvatarPath))
                        {
                            Preferences.Default.Set("user_avatar_path", selectedAvatarPath);
                        }

                        ShowStatus("Account created successfully! You can now log in.", isError: false);
                        BtnToggleMode_Click(this, EventArgs.Empty);
                    }
                    else
                    {
                        var result = JsonSerializer.Deserialize<AuthResponse>(responseBody, jsonOptions);

                        string token = result?.Token ?? result?.AccessToken ?? string.Empty;

                        if (!string.IsNullOrEmpty(token))
                        {
                            Preferences.Default.Set("auth_token", token);

                            string savedUsername = !string.IsNullOrEmpty(result?.Username) ? result.Username : username;
                            Preferences.Default.Set("user_name", savedUsername);

                            if (!string.IsNullOrEmpty(result?.FullName))
                            {
                                Preferences.Default.Set("full_name", result.FullName);
                            }
                        }

                        await NavigateAfterLoginAsync();
                    }
                }
                else
                {
                    string errorMessage = "Operation failed.";
                    try
                    {
                        var errorResult = JsonSerializer.Deserialize<ErrorResponse>(responseBody, jsonOptions);
                        if (!string.IsNullOrEmpty(errorResult?.Error))
                            errorMessage = errorResult.Error;
                        else if (!string.IsNullOrEmpty(errorResult?.Message))
                            errorMessage = errorResult.Message;
                    }
                    catch
                    {
                        errorMessage = $"Server error ({ (int)response.StatusCode })";
                    }

                    ShowStatus(errorMessage, isError: true);
                }
            }
            catch (TaskCanceledException)
            {
                ShowStatus("Server request timed out (waking up Render instance). Please try again.", isError: true);
            }
            catch (HttpRequestException ex)
            {
                ShowStatus($"Cannot connect to server: {ex.Message}", isError: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"An unexpected error occurred: {ex.Message}", isError: true);
            }
            finally
            {
                SetLoadingState(isLoading: false);
            }
        }

        private async Task NavigateAfterLoginAsync()
        {
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
            }
            else if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync();
            }
            else
            {
                Application.Current!.MainPage = new ProfilePage();
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            BtnSubmit.IsEnabled = !isLoading;
            BtnToggleMode.IsEnabled = !isLoading;
            LoadingBar.IsRunning = isLoading;
            LoadingBar.IsVisible = isLoading;
            if (isLoading) TxtStatus.Text = "Connecting to server...";
        }

        private void ShowStatus(string message, bool isError)
        {
            TxtStatus.TextColor = isError ? Colors.Red : Colors.Green;
            TxtStatus.Text = message;
        }
    }

    public class AuthResponse
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }
    }

    public class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}