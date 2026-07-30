using System.Runtime.Versioning;

// WPF-сборка (net8.0-windows) по определению работает только под Windows.
// GenerateTargetFrameworkAttribute=false отключает авто-генерацию этого атрибута,
// поэтому объявляем его явно, чтобы CA1416 не срабатывал на каждом использовании
// Windows-специфичных API (ServiceController, MessageBox и т.п.).
[assembly: SupportedOSPlatform("windows")]
