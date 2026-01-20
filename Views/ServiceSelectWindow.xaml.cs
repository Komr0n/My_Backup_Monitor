using System.Collections.Generic;
using System.Windows;

namespace BackupMonitor.Views
{
    public partial class ServiceSelectWindow : Window
    {
        public const string AllServicesLabel = "Все сервисы";
        public string? SelectedServiceName { get; private set; }

        public ServiceSelectWindow(List<string> serviceNames)
        {
            InitializeComponent();
            var items = new List<string> { AllServicesLabel };
            items.AddRange(serviceNames);

            ServiceCombo.ItemsSource = items;
            if (items.Count > 0)
            {
                ServiceCombo.SelectedIndex = 0;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (ServiceCombo.SelectedItem is string name)
            {
                SelectedServiceName = name;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Выберите сервис", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}

