/*
 * MakingVibe/MainWindow.PreviewTab.cs
 * Partial class file for Preview Tab logic.
 * Handles displaying file content or directory information.
 */
using MakingVibe.Models;
using MakingVibe.Services; // For FileHelper
using System.Threading.Tasks;
using System.Windows; // Required for partial class Window

namespace MakingVibe
{
    public partial class MainWindow // : Window // Already declared
    {
        // Note: _fileSystemService is in the main partial class

        // --- File Preview (UI Related) ---

        /// <summary>
        /// Shows a preview of the selected file or directory information in the TextBox.
        /// Called by treeViewFiles_SelectedItemChanged.
        /// </summary>
        private async Task ShowPreviewAsync(FileSystemItem? fsi)
        {
            if (fsi == null)
            {
                txtFileContent.Text = string.Empty;
                return;
            }

            if (fsi.IsDirectory)
            {
                txtFileContent.Text = $"Directorio: {fsi.Name}\nRuta: {fsi.Path}";
            }
            else if (FileHelper.IsTextFile(fsi.Path))
            {
                var (content, success) = await _fileSystemService.ReadTextFileContentAsync(fsi.Path);
                txtFileContent.Text = content; // Shows content or error message from service
            }
            else if (FileHelper.IsImageFile(fsi.Path))
            {
                txtFileContent.Text = $"--- Vista previa de imagen no disponible ---\nArchivo: {fsi.Name}";
            }
            else
            {
                txtFileContent.Text = $"--- No se puede previsualizar este tipo de archivo ---\nTipo: {fsi.Type}\nArchivo: {fsi.Name}";
            }
            // Optional: Switch to preview tab? Decided against automatically switching.
            // if (tabControlMain.SelectedIndex != 0) tabControlMain.SelectedIndex = 0;
        }

    } // End partial class MainWindow
} // End namespace MakingVibe