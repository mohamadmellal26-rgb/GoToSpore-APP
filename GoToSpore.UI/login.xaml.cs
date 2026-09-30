using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoToSpore.UI
{
    public partial class LoginPage : ContentPage
    {
        private bool isRegisterMode = false;

        private static readonly HttpClient httpClient = new HttpClient
        {
            // للأجهزة الحقيقية أو المحاكي (10.0.2.2 لأندرويد محاكي)
            BaseAddress = new Uri("http://10.0.2.2:8080/")
        };

        public LoginPage()
        {
            InitializeComponent();
        }

        private void BtnToggleMode_Click(object sender, EventArgs e)
        {
            isRegisterMode = !isRegisterMode;
            TxtStatus.Text = string.Empty;

            if (isRegisterMode)
            {
                TxtTitle.Text = "إنشاء حساب جديد";
                BtnSubmit.Text = "إنشاء الحساب";
                BtnToggleMode.Text = "لديك حساب بالفعل؟ تسجيل الدخول";
            }
            else
            {
                TxtTitle.Text = "تسجيل الدخول إلى حسابك";
                BtnSubmit.Text = "تسجيل الدخول";
                BtnToggleMode.Text = "ليس لديك حساب؟ إنشاء حساب جديد";
            }
        }

        private async void BtnSubmit_Click(object sender, EventArgs e)
        {
            string username = TxtUsername.Text?.Trim() ?? "";
            string password = TxtPassword.Text ?? "";

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowStatus("يرجى إدخال اسم المستخدم وكلمة المرور.", isError: true);
                return;
            }

            SetLoadingState(isLoading: true);

            try
            {
                var payloadData = new { username = username, password = password };
                string jsonPayload = JsonSerializer.Serialize(payloadData);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string endpoint = isRegisterMode ? "api/register" : "api/login";

                HttpResponseMessage response = await httpClient.PostAsync(endpoint, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (isRegisterMode)
                    {
                        ShowStatus("تم إنشاء الحساب بنجاح! يمكنك الآن تسجيل الدخول.", isError: false);
                        BtnToggleMode_Click(null, null);
                    }
                    else
                    {
                        var result = JsonSerializer.Deserialize<AuthResponse>(responseBody);

                        // حفظ التوكن بشكل دائم في الجهاز
                        if (!string.IsNullOrEmpty(result?.Token))
                        {
                            Preferences.Default.Set("auth_token", result.Token);
                        }

                        // العودة إلى MainPage وإغلاق صفحة التسجيل
                        await Navigation.PopModalAsync();
                    }
                }
                else
                {
                    var errorResult = JsonSerializer.Deserialize<ErrorResponse>(responseBody);
                    ShowStatus(errorResult?.Error ?? "فشلت العملية.", isError: true);
                }
            }
            catch (HttpRequestException)
            {
                ShowStatus("تعذر الاتصال بالسيرفر. تأكد من تشغيل سيرفر Go.", isError: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"حدث خطأ: {ex.Message}", isError: true);
            }
            finally
            {
                SetLoadingState(isLoading: false);
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            BtnSubmit.IsEnabled = !isLoading;
            BtnToggleMode.IsEnabled = !isLoading;
            LoadingBar.IsRunning = isLoading;
            LoadingBar.IsVisible = isLoading;
            TxtStatus.Text = string.Empty;
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
        public string Message { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }
    }

    public class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string Error { get; set; }
    }
}