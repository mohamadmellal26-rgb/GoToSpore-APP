namespace GoToSpore.UI;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("ActivitiesPage", typeof(ActivitiesPage));
        Routing.RegisterRoute("ProfilePage", typeof(ProfilePage));
        Routing.RegisterRoute("SettingsPage", typeof(SettingsPage));
    }
}