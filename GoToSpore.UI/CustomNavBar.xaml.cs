using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace GoToSpore.UI
{
    public partial class CustomNavBar : ContentView
    {
        private static bool _isNavigating = false;

        #region Bindable Properties

        public static readonly BindableProperty ActiveTabProperty =
            BindableProperty.Create(
                nameof(ActiveTab),
                typeof(string),
                typeof(CustomNavBar),
                "Home",
                propertyChanged: OnActiveTabChanged);

        public string ActiveTab
        {
            get => (string)GetValue(ActiveTabProperty);
            set => SetValue(ActiveTabProperty, value);
        }

        public static readonly BindableProperty HomeColorProperty =
            BindableProperty.Create(nameof(HomeColor), typeof(Color), typeof(CustomNavBar), Color.FromArgb("#737373"));

        public Color HomeColor
        {
            get => (Color)GetValue(HomeColorProperty);
            set => SetValue(HomeColorProperty, value);
        }

        public static readonly BindableProperty ActivitiesColorProperty =
            BindableProperty.Create(nameof(ActivitiesColor), typeof(Color), typeof(CustomNavBar), Color.FromArgb("#737373"));

        public Color ActivitiesColor
        {
            get => (Color)GetValue(ActivitiesColorProperty);
            set => SetValue(ActivitiesColorProperty, value);
        }

        public static readonly BindableProperty LiveColorProperty =
            BindableProperty.Create(nameof(LiveColor), typeof(Color), typeof(CustomNavBar), Color.FromArgb("#737373"));

        public Color LiveColor
        {
            get => (Color)GetValue(LiveColorProperty);
            set => SetValue(LiveColorProperty, value);
        }

        public static readonly BindableProperty ProfileColorProperty =
            BindableProperty.Create(nameof(ProfileColor), typeof(Color), typeof(CustomNavBar), Color.FromArgb("#737373"));

        public Color ProfileColor
        {
            get => (Color)GetValue(ProfileColorProperty);
            set => SetValue(ProfileColorProperty, value);
        }

        public static readonly BindableProperty SettingsColorProperty =
            BindableProperty.Create(nameof(SettingsColor), typeof(Color), typeof(CustomNavBar), Color.FromArgb("#737373"));

        public Color SettingsColor
        {
            get => (Color)GetValue(SettingsColorProperty);
            set => SetValue(SettingsColorProperty, value);
        }

        #endregion

        public CustomNavBar()
        {
            InitializeComponent();
            // لضمان أن عناصر XAML داخل الشريط تقرأ من المكون نفسه وليس من ViewModel الصفحة
            Content.BindingContext = this;
            UpdateTabColors();
        }

        private static void OnActiveTabChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is CustomNavBar navBar)
            {
                navBar.UpdateTabColors();
            }
        }

        private void UpdateTabColors()
        {
            Color activeColor = Color.FromArgb("#EAB308");
            Color inactiveColor = Color.FromArgb("#737373");
            Color liveActiveColor = Color.FromArgb("#EF4444");

            HomeColor = ActiveTab == "Home" ? activeColor : inactiveColor;
            ActivitiesColor = ActiveTab == "Activities" ? activeColor : inactiveColor;
            LiveColor = ActiveTab == "Live" ? liveActiveColor : inactiveColor;
            ProfileColor = ActiveTab == "Profile" ? activeColor : inactiveColor;
            SettingsColor = ActiveTab == "Settings" ? activeColor : inactiveColor;
        }

        #region Navigation Handling

        private async void OnHomeTabTapped(object sender, EventArgs e)
        {
            if (ActiveTab == "Home") return;
            await NavigateToTabAsync(sender, "//MainPage", () => new MainPage());
        }

        private async void OnActivitiesTabTapped(object sender, EventArgs e)
        {
            if (ActiveTab == "Activities") return;
            await NavigateToTabAsync(sender, "//ActivitiesPage", () => new ActivitiesPage());
        }

        private async void OnLiveTabTapped(object sender, EventArgs e)
        {
            if (ActiveTab == "Live") return;
            await NavigateToTabAsync(sender, "//LivePage", () => new LivePage());
        }

        private async void OnProfileTapped(object sender, EventArgs e)
        {
            if (ActiveTab == "Profile") return;
            await NavigateToTabAsync(sender, "//ProfilePage", () => new ProfilePage());
        }

        private async void OnSettingsTabTapped(object sender, EventArgs e)
        {
            if (ActiveTab == "Settings") return;
            await NavigateToTabAsync(sender, "//SettingsPage", () => new SettingsPage());
        }

        private async Task NavigateToTabAsync(object sender, string shellRoute, Func<Page> pageFactory)
        {
            if (_isNavigating) return;
            _isNavigating = true;

            try
            {
                if (sender is VisualElement element)
                {
                    await element.ScaleTo(0.92, 50, Easing.CubicOut);
                    await element.ScaleTo(1.0, 50, Easing.CubicIn);
                }

                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync(shellRoute, animate: false);
                }
                else if (Navigation != null)
                {
                    var page = pageFactory();
                    await Navigation.PushAsync(page, animated: false);

                    if (Navigation.NavigationStack.Count > 1)
                    {
                        var existingPages = Navigation.NavigationStack;
                        for (int i = existingPages.Count - 2; i >= 0; i--)
                        {
                            Navigation.RemovePage(existingPages[i]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation Error]: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }

        #endregion
    }
}