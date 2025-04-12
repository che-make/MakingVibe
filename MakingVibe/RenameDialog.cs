using System.IO; // For Path.GetInvalidFileNameChars
using System.Windows;
using System.Windows.Input; // For KeyEventArgs

namespace MakingVibe
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; }
        private readonly string _originalName;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            _originalName = currentName;
            NewName = currentName; // Initialize NewName
            txtNewName.Text = currentName;
            txtNewName.SelectAll();
            // txtNewName.Focus(); // Focus is set in XAML now
        }

        private void btnAccept_Click(object sender, RoutedEventArgs e)
        {
            TryAccept();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // Close dialog without changes
        }

        // Handle Enter key press in TextBox
        private void TxtNewName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryAccept();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false; // Close on Escape
            }
        }

        private void TryAccept()
        {
             string proposedName = txtNewName.Text.Trim(); // Trim whitespace

            if (string.IsNullOrWhiteSpace(proposedName))
            {
                System.Windows.MessageBox.Show("El nombre no puede estar vacío.", "Nombre Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check for invalid characters
            if (proposedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                System.Windows.MessageBox.Show("El nombre contiene caracteres no válidos.", "Nombre Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                 return;
            }

            NewName = proposedName;
            DialogResult = true; // Close dialog successfully
        }
    }
}