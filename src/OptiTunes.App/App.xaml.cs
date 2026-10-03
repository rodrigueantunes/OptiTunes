using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Threading;

namespace OptiTunes.App;

public partial class App : Application
{
    public App()
    {
        // Formats français dans tous les bindings (kg « 9 394 », décimales « 1,35 »),
        // y compris les Run des TextBlock (FrameworkContentElement).
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        var language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
        FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(TextElement), new FrameworkPropertyMetadata(language));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "OptiTunes – erreur inattendue", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
