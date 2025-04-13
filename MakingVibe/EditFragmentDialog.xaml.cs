using MakingVibe.Models; // Use the model namespace
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MakingVibe
{
    /// <summary>
    /// Interaction logic for EditFragmentDialog.xaml
    /// </summary>
    public partial class EditFragmentDialog : Window
    {
        // Property to hold the edited fragment data (passed back to MainWindow)
        // We edit a temporary copy within the dialog itself.
        public TextFragment EditedFragment { get; private set; }

        // Constructor takes the original fragment to populate the fields
        public EditFragmentDialog(TextFragment fragmentToEdit)
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow; // Set owner for centering

            // Initialize EditedFragment with data from the fragment passed in
            // We work on this copy; if saved, MainWindow updates the original.
            EditedFragment = new TextFragment
            {
                Title = fragmentToEdit.Title,
                Text = fragmentToEdit.Text
            };

            // Populate UI controls from the copy
            txtFragmentTitle.Text = EditedFragment.Title;
            txtFragmentText.Text = EditedFragment.Text;

            txtFragmentTitle.Focus(); // Focus on title initially
            txtFragmentTitle.SelectAll(); // Select text for easy replacement
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // Validate input before saving
            string newTitle = txtFragmentTitle.Text.Trim();
            string newText = txtFragmentText.Text; // Keep original formatting, maybe trim at end if needed

            if (string.IsNullOrWhiteSpace(newTitle))
            {
                MessageBox.Show("El título del fragmento no puede estar vacío.", "Guardar Fragmento", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtFragmentTitle.Focus();
                return;
            }
            // Allow empty text, but maybe warn? For now, allow it.
            // if (string.IsNullOrWhiteSpace(newText))
            // {
            //     MessageBox.Show("El texto del fragmento no puede estar vacío.", "Guardar Fragmento", MessageBoxButton.OK, MessageBoxImage.Warning);
            //     txtFragmentText.Focus();
            //     return;
            // }

            // Update the EditedFragment property with the values from the UI
            EditedFragment.Title = newTitle;
            EditedFragment.Text = newText;

            DialogResult = true; // Set DialogResult to true to indicate successful save
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // DialogResult is automatically false if IsCancel=True, but explicit doesn't hurt
        }
    }
}