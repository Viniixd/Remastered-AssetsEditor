using System.Linq;
using System.Windows;

namespace Assets_Editor
{
    public partial class LegacyAssetsOptionsDialog : Window
    {
        public int SelectedVersion { get; private set; }
        public bool UseTransparency { get; private set; }

        public LegacyAssetsOptionsDialog()
        {
            InitializeComponent();
            var versions = MainWindow.datStructure.GetAllVersions()
                .OrderByDescending(v => v.Number)
                .ToList();
            VersionCombo.ItemsSource = versions;
            if (versions.Count > 0)
            {
                VersionCombo.SelectedIndex = 0;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (VersionCombo.SelectedItem is VersionInfo versionInfo)
            {
                SelectedVersion = versionInfo.Number;
                UseTransparency = TransparencyCheckBox.IsChecked == true;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please select a client version.", "Assets Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
