using System;
using System.Windows;

namespace BackupMonitor.Views
{
    public partial class PeriodWindow : Window
    {
        public DateTime StartDate { get; private set; }
        public DateTime EndDate { get; private set; }

        public PeriodWindow()
        {
            InitializeComponent();
            DpStartDate.SelectedDate = DateTime.Today;
            DpEndDate.SelectedDate = DateTime.Today;
            
            // Обработка нажатия Enter на DatePicker
            DpStartDate.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) BtnOk_Click(this, new RoutedEventArgs()); };
            DpEndDate.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) BtnOk_Click(this, new RoutedEventArgs()); };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!DpStartDate.SelectedDate.HasValue || !DpEndDate.SelectedDate.HasValue)
            {
                MessageBox.Show("Выберите обе даты", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DpStartDate.SelectedDate.Value > DpEndDate.SelectedDate.Value)
            {
                MessageBox.Show("Дата начала не может быть больше даты окончания", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartDate = DpStartDate.SelectedDate.Value;
            EndDate = DpEndDate.SelectedDate.Value;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

