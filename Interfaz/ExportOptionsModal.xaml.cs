using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RobloxScriptExplorer.Interfaz
{
    public partial class ExportOptionsModal : Window
    {
        public bool IsAllInOneRbxmx => RadioAllInOne.IsChecked == true;
        private bool _isUpdating = false;

        public ExportOptionsModal()
        {
            InitializeComponent();
            SelectOption(true);
        }

        private void SelectOption(bool allInOne)
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                if (RadioAllInOne != null) RadioAllInOne.IsChecked = allInOne;
                if (RadioModular != null) RadioModular.IsChecked = !allInOne;

                if (BorderOpt1 != null && BorderOpt2 != null && TxtTitleOpt1 != null && TxtTitleOpt2 != null)
                {
                    if (allInOne)
                    {
                        BorderOpt1.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#0284C7")!;
                        BorderOpt1.BorderThickness = new Thickness(2);
                        BorderOpt1.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#1E293B")!;

                        BorderOpt2.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#334155")!;
                        BorderOpt2.BorderThickness = new Thickness(1.5);
                        BorderOpt2.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#0F172A")!;

                        TxtTitleOpt1.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#38BDF8")!;
                        TxtTitleOpt2.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!;
                    }
                    else
                    {
                        BorderOpt2.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#0284C7")!;
                        BorderOpt2.BorderThickness = new Thickness(2);
                        BorderOpt2.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#1E293B")!;

                        BorderOpt1.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#334155")!;
                        BorderOpt1.BorderThickness = new Thickness(1.5);
                        BorderOpt1.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#0F172A")!;

                        TxtTitleOpt2.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#38BDF8")!;
                        TxtTitleOpt1.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!;
                    }
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void RadioAllInOne_Checked(object sender, RoutedEventArgs e)
        {
            SelectOption(true);
        }

        private void RadioModular_Checked(object sender, RoutedEventArgs e)
        {
            SelectOption(false);
        }

        private void BorderOpt1_Click(object sender, MouseButtonEventArgs e)
        {
            SelectOption(true);
        }

        private void BorderOpt2_Click(object sender, MouseButtonEventArgs e)
        {
            SelectOption(false);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
