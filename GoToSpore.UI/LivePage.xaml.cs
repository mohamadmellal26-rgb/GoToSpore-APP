using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;

namespace GoToSpore.UI
{
    // نموذج الرسائل المتبادلة مع سيرفر Go
    public class LiveMessage
    {
        public string Type { get; set; } = string.Empty;     // "chat", "heart", "stats"
        public string Username { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Color { get; set; } = "#3B82F6";
        public int Calories { get; set; }
        public int Duration { get; set; }
    }

    public partial class LivePage : ContentPage
    {
        private readonly ICameraProvider? _cameraProvider;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;

        private int _calories = 320;
        private int _secondsElapsed = 865;
        private bool _isLiveActive = true;

        // عنوان سيرفر Go (استبدل IP بالخاص بك إذا كنت تستخدم جهازاً حقيقياً أو 10.0.2.2 للمحاكي)
        private const string WebSocketUrl = "ws://10.0.2.2:8080/ws/live";

        public LivePage(ICameraProvider cameraProvider)
        {
            InitializeComponent();
            _cameraProvider = cameraProvider;
            StartWorkoutTimers();
        }

        public LivePage()
        {
            InitializeComponent();
            _cameraProvider = Handler?.MauiContext?.Services.GetService<ICameraProvider>();
            StartWorkoutTimers();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await Task.Delay(300);
            await StartCameraAsync();
            
            // بدء الاتصال بسيرفر Go المباشر
            await ConnectToLiveHubAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isLiveActive = false;
            StopCamera();
            _ = DisconnectWebSocketAsync();
        }

        #region WebSocket Connection & Management

        private async Task ConnectToLiveHubAsync()
        {
            try
            {
                _webSocket = new ClientWebSocket();
                _cts = new CancellationTokenSource();

                await _webSocket.ConnectAsync(new Uri(WebSocketUrl), _cts.Token);
                System.Diagnostics.Debug.WriteLine("[WS]: تم الاتصال بنجاح بسيرفر Go LiveHub");

                // بدء استماع الرسائل القادمة من السيرفر في خلفية غير حاجزة
                _ = ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WS Connection Error]: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[1024 * 4];

            while (_webSocket != null && _webSocket.State == WebSocketState.Open && !_cts!.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    var jsonString = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var msg = JsonSerializer.Deserialize<LiveMessage>(jsonString, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (msg != null)
                    {
                        // التحديث على الـ UI Thread
                        MainThread.BeginInvokeOnMainThread(() => HandleIncomingMessage(msg));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WS Receive Error]: {ex.Message}");
                    break;
                }
            }
        }

        private void HandleIncomingMessage(LiveMessage msg)
        {
            switch (msg.Type)
            {
                case "chat":
                    AddComment(msg.Username, msg.Content, msg.Color);
                    break;
                case "heart":
                    ShowFloatingHeart();
                    break;
                case "stats":
                    // تحديث مؤشرات التمرين إذا كان المشترك مستخدماً وليس مدرباً
                    TimerLabel.Text = $"⏱️ {TimeSpan.FromSeconds(msg.Duration):mm\\:ss}";
                    CaloriesLabel.Text = $"🔥 {msg.Calories} kcal";
                    break;
            }
        }

        private async Task SendMessageAsync(LiveMessage msg)
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var json = JsonSerializer.Serialize(msg);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WS Send Error]: {ex.Message}");
                }
            }
        }

        private async Task DisconnectWebSocketAsync()
        {
            if (_cts != null) _cts.Cancel();

            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Page Disappearing", CancellationToken.None);
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        #endregion

        #region Camera Controls

        private async Task StartCameraAsync()
        {
            try
            {
                var cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                var micStatus = await Permissions.RequestAsync<Permissions.Microphone>();

                if (cameraStatus != PermissionStatus.Granted)
                {
                    await DisplayAlert("خطأ", "يلزم منح صلاحية الكاميرا لتشغيل البث.", "حسناً");
                    return;
                }

                var provider = _cameraProvider ?? Handler?.MauiContext?.Services.GetService<ICameraProvider>();

                if (provider != null)
                {
                    await provider.RefreshAvailableCameras(CancellationToken.None);
                    var cameras = provider.AvailableCameras;

                    var selectedCamera = cameras.FirstOrDefault(c => c.Position.ToString().Equals("Front", StringComparison.OrdinalIgnoreCase))
                                         ?? cameras.FirstOrDefault(c => c.Position.ToString().Equals("Back", StringComparison.OrdinalIgnoreCase))
                                         ?? cameras.FirstOrDefault();

                    if (selectedCamera != null)
                    {
                        LiveCameraView.SelectedCamera = selectedCamera;
                    }
                }

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await LiveCameraView.StartCameraPreview(CancellationToken.None);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Camera Error]: {ex.Message}");
            }
        }

        private void StopCamera()
        {
            try
            {
                LiveCameraView.StopCameraPreview();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Stop Error]: {ex.Message}");
            }
        }

        #endregion

        #region Timers & Interactions

        private void StartWorkoutTimers()
        {
            Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                if (!_isLiveActive) return false;

                _secondsElapsed++;
                TimeSpan time = TimeSpan.FromSeconds(_secondsElapsed);
                TimerLabel.Text = $"⏱️ {time:mm\\:ss}";

                if (_secondsElapsed % 5 == 0)
                {
                    _calories += 2;
                    CaloriesLabel.Text = $"🔥 {_calories} kcal";
                }

                // بث البيانات (Stats) إلى السيرفر في كل ثانية ليصل المشاهدين التوقيت الحقيقي
                _ = SendMessageAsync(new LiveMessage
                {
                    Type = "stats",
                    Calories = _calories,
                    Duration = _secondsElapsed
                });

                return true;
            });
        }

        private void AddComment(string userName, string message, string colorHex)
        {
            var commentBorder = new Border
            {
                BackgroundColor = Color.FromArgb("#1E293B"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                Padding = new Thickness(10, 6),
                HorizontalOptions = LayoutOptions.Start
            };

            var formattedString = new FormattedString();
            formattedString.Spans.Add(new Span { Text = $"{userName}: ", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(string.IsNullOrEmpty(colorHex) ? "#FFFFFF" : colorHex) });
            formattedString.Spans.Add(new Span { Text = message, TextColor = Colors.White });

            commentBorder.Content = new Label { FormattedText = formattedString, FontSize = 13 };

            CommentsStack.Children.Add(commentBorder);
        }

        private async void OnSendCommentClicked(object? sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(CommentEntry.Text))
            {
                var userText = CommentEntry.Text;
                CommentEntry.Text = string.Empty;

                // إرسال التعليق إلى سيرفر Go ليبثه للجميع
                await SendMessageAsync(new LiveMessage
                {
                    Type = "chat",
                    Username = "المدرب",
                    Content = userText,
                    Color = "#F59E0B"
                });
            }
        }

        private async void OnHeartButtonClicked(object? sender, EventArgs e)
        {
            // إرسال حدث الضغطة للجميع
            await SendMessageAsync(new LiveMessage
            {
                Type = "heart"
            });
        }

        private async void ShowFloatingHeart()
        {
            var heartLabel = new Label
            {
                Text = "❤️",
                FontSize = 28,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.End,
                TranslationX = -20
            };

            FloatingHeartsContainer.Children.Add(heartLabel);

            await Task.WhenAll(
                heartLabel.TranslateTo(-20, -300, 1200, Easing.CubicOut),
                heartLabel.FadeTo(0, 1200)
            );

            FloatingHeartsContainer.Children.Remove(heartLabel);
        }

        private async void OnCloseLiveClicked(object? sender, EventArgs e)
        {
            bool answer = await DisplayAlert("إنهاء البث", "هل تريد إنهاء جلسة البث المباشر الآن؟", "نعم", "إلغاء");
            if (answer)
            {
                _isLiveActive = false;
                StopCamera();
                await DisconnectWebSocketAsync();

                if (Shell.Current != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await Shell.Current.GoToAsync("//MainPage", animate: false);
                    });
                }
                else if (Navigation != null)
                {
                    await Navigation.PopToRootAsync(animated: false);
                }
            }
        }

        #endregion
    }
}