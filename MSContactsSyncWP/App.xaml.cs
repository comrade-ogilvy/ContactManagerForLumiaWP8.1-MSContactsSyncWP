// App.xaml.cs — WP8.1 Silverlight
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;

namespace MSContactsSyncWP
{
    public partial class App : Application
    {
        public static PhoneApplicationFrame RootFrame { get; private set; }

        public App()
        {
            UnhandledException += Application_UnhandledException;
            InitializeComponent();
            InitializePhoneApplication();
            PhoneApplicationService.Current.UserIdleDetectionMode =
                IdleDetectionMode.Disabled;
        }

        private void Application_Launching(object sender,
            LaunchingEventArgs e) { }

        private void Application_Activated(object sender,
            ActivatedEventArgs e) { }

        private void Application_Deactivated(object sender,
            DeactivatedEventArgs e) { }

        private void Application_Closing(object sender,
            ClosingEventArgs e) { }

        private void RootFrame_NavigationFailed(object sender,
            NavigationFailedEventArgs e) { }

        private void Application_UnhandledException(object sender,
            ApplicationUnhandledExceptionEventArgs e) { }

        // ----------------------------------------------------------------
        bool _phoneApplicationInitialized = false;

        private void InitializePhoneApplication()
        {
            if (_phoneApplicationInitialized) return;
            RootFrame = new PhoneApplicationFrame();
            RootFrame.Navigated += CompleteInitializePhoneApplication;
            RootFrame.NavigationFailed += RootFrame_NavigationFailed;
            _phoneApplicationInitialized = true;
        }

        private void CompleteInitializePhoneApplication(object sender,
            NavigationEventArgs e)
        {
            if (RootVisual != RootFrame)
                RootVisual = RootFrame;
            RootFrame.Navigated -= CompleteInitializePhoneApplication;
        }
    }
}
