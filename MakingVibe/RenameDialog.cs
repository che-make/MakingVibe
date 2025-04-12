using System.Windows;

namespace MakingVibe
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; }

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            txtNewName.Text = currentName;
            txtNewName.SelectAll();
            txtNewName.Focus();
        }

        private void btnAccept_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNewName.Text))
            {
                MessageBox.Show("El nombre no puede estar vacío.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            NewName = txtNewName.Text;
            DialogResult = true;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}