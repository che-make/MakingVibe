/*
 * MakingVibe/MainWindow.PromptTab.cs
 * Partial class file for Prompt Tab logic.
 * Handles Main Prompt, Text Fragments (Add, Delete, Edit, Load, Save),
 * and the "Copiar Texto (AI)" action.
 */
using MakingVibe.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CheckBox = System.Windows.Controls.CheckBox; // Alias
using Clipboard = System.Windows.Clipboard; // Alias
using Cursors = System.Windows.Input.Cursors; // Alias
using KeyEventArgs = System.Windows.Input.KeyEventArgs; // Alias
using ListViewItem = System.Windows.Controls.ListViewItem; // Alias
using MessageBox = System.Windows.MessageBox; // Alias

namespace MakingVibe
{
    public partial class MainWindow // : Window // Already declared
    {
        // Note: Fragments, _fragmentsFilePath, _aiCopyService, selectedItems fields are in the main partial class

        // --- AI Text Copy Action (UI Coordination) ---

        private async void btnCopyText_Click(object sender, RoutedEventArgs e)
        {
            // 1. Get main prompt and selected fragments text
            string mainPrompt = txtMainPrompt.Text.Trim();
            var selectedFragmentsText = new StringBuilder();
            foreach (var item in listViewFragments.Items)
            {
                if (item is TextFragment fragment)
                {
                    var container = listViewFragments.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                    if (container != null)
                    {
                        var checkBox = FindVisualChild<CheckBox>(container); // Helper in main partial
                        if (checkBox != null && checkBox.IsChecked == true)
                        {
                            selectedFragmentsText.AppendLine(fragment.Text);
                            selectedFragmentsText.AppendLine(); // Blank line separator
                        }
                    }
                }
            }
            string fragmentsString = selectedFragmentsText.ToString().TrimEnd();

            // 2. Handle case: NO files selected (copy only prompt/fragments)
            if (selectedItems.Count == 0)
            {
                var promptOnlyOutputBuilder = new StringBuilder();
                if (!string.IsNullOrEmpty(mainPrompt)) { promptOnlyOutputBuilder.AppendLine(mainPrompt).AppendLine(); }
                if (!string.IsNullOrEmpty(fragmentsString)) { promptOnlyOutputBuilder.AppendLine(fragmentsString); } // No extra newline at the very end
                string finalPromptOnlyText = promptOnlyOutputBuilder.ToString().Trim();

                if (string.IsNullOrEmpty(finalPromptOnlyText))
                {
                    UpdateStatusBarAndButtonStates("Nada seleccionado para copiar.");
                    MessageBox.Show("No hay archivos seleccionados, ni prompt principal, ni fragmentos seleccionados.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    Clipboard.SetText(finalPromptOnlyText);
                    UpdateStatusBarAndButtonStates("Prompt y/o fragmentos copiados.");
                    MessageBox.Show("El prompt principal y/o los fragmentos seleccionados han sido copiados.", "Texto Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception clipEx)
                {
                     Debug.WriteLine($"Clipboard Error (Prompt/Fragment Only): {clipEx.Message}");
                     MessageBox.Show($"No se pudo copiar al portapapeles.\nError: {clipEx.Message}", "Error al Copiar", MessageBoxButton.OK, MessageBoxImage.Warning);
                     UpdateStatusBarAndButtonStates("Error al copiar texto.");
                }
                return;
            }

            // --- Case: Files ARE selected ---
            if (string.IsNullOrEmpty(rootPath))
            {
                 MessageBox.Show("No se ha establecido una carpeta raíz (necesaria para el mapa).", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            var itemsToProcess = selectedItems.ToList(); // Use local copy

            this.Cursor = Cursors.Wait;
            statusBarText.Text = "Recopilando archivos de texto..."; // Direct status update
            btnCopyText.IsEnabled = false; // Disable button during operation

            try
            {
                // Use service call in background thread
                var result = await Task.Run(() => _aiCopyService.GenerateAiClipboardContentAsync(itemsToProcess, rootPath));

                var finalOutputBuilder = new StringBuilder();
                 // Add Main Prompt (if any)
                if (!string.IsNullOrEmpty(mainPrompt)) { finalOutputBuilder.AppendLine(mainPrompt).AppendLine(); }
                 // Add Selected Fragments (if any)
                if (!string.IsNullOrEmpty(fragmentsString)) { finalOutputBuilder.AppendLine(fragmentsString).AppendLine(); }

                if (result == null || result.Value.TextFileCount == 0)
                {
                    // No text files found, but prompt/fragments might exist
                    string finalPromptOnlyText = finalOutputBuilder.ToString().Trim();
                    if (string.IsNullOrEmpty(finalPromptOnlyText))
                    {
                         MessageBox.Show("No se encontraron archivos de texto válidos y el prompt/fragmentos están vacíos.", "Copiar Texto (AI)", MessageBoxButton.OK, MessageBoxImage.Information);
                         UpdateStatusBarAndButtonStates("No se encontraron archivos de texto.");
                    }
                    else {
                         try {
                            Clipboard.SetText(finalPromptOnlyText);
                            UpdateStatusBarAndButtonStates("Prompt y/o fragmentos copiados (sin archivos).");
                            MessageBox.Show("No se encontraron archivos de texto. Se copió solo el prompt y/o fragmentos.", "Texto Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
                         } catch (Exception clipEx) {
                             Debug.WriteLine($"Clipboard Error (Prompt/Fragment Only after file check): {clipEx.Message}");
                             MessageBox.Show($"No se pudo copiar al portapapeles.\nError: {clipEx.Message}", "Error al Copiar", MessageBoxButton.OK, MessageBoxImage.Warning);
                             UpdateStatusBarAndButtonStates("Error al copiar texto.");
                         }
                    }
                }
                else
                {
                    // Append file map and contents from service result
                    finalOutputBuilder.Append(result.Value.Output);
                    string finalClipboardText = finalOutputBuilder.ToString();

                    try {
                        Clipboard.SetText(finalClipboardText);
                        UpdateStatusBarAndButtonStates($"Contenido de {result.Value.TextFileCount} archivo(s) copiado.");
                        MessageBox.Show($"Se ha copiado el prompt, fragmentos seleccionados, mapa y contenido de {result.Value.TextFileCount} archivo(s) de texto.", "Texto Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
                    } catch (Exception clipEx) {
                        Debug.WriteLine($"Clipboard Error: {clipEx.Message}");
                        string sizeInfo = finalClipboardText.Length > 1024*1024 ? $" (Tamaño: {finalClipboardText.Length / 1024.0 / 1024.0:F1} MB)" : "";
                        MessageBox.Show($"No se pudo copiar al portapapeles.{sizeInfo}\nError: {clipEx.Message}", "Error al Copiar", MessageBoxButton.OK, MessageBoxImage.Warning);
                        UpdateStatusBarAndButtonStates("Error al copiar texto.");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatusBarAndButtonStates("Error al copiar texto.");
                MessageBox.Show($"Error al preparar el texto para la IA:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Error btnCopyText_Click: {ex}");
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
                UpdateStatusBarAndButtonStates(); // Refresh button states
            }
        }


        // --- Fragment Management ---

        private void AddFragment_Click(object sender, RoutedEventArgs e)
        {
            AddCurrentFragment();
        }

        private void TxtNewFragmentText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                AddCurrentFragment();
                e.Handled = true; // Prevent Enter from adding newline/dinging
            }
        }

        private void AddCurrentFragment()
        {
             string newTitle = txtNewFragmentTitle.Text.Trim();
             string newText = txtNewFragmentText.Text.Trim();

            if (string.IsNullOrWhiteSpace(newTitle)) {
                 MessageBox.Show("El título no puede estar vacío.", "Añadir Fragmento", MessageBoxButton.OK, MessageBoxImage.Warning);
                 txtNewFragmentTitle.Focus(); return;
            }
             if (string.IsNullOrWhiteSpace(newText)) {
                 MessageBox.Show("El texto no puede estar vacío.", "Añadir Fragmento", MessageBoxButton.OK, MessageBoxImage.Warning);
                 txtNewFragmentText.Focus(); return;
            }

            Fragments.Add(new TextFragment { Title = newTitle, Text = newText });
            txtNewFragmentTitle.Clear();
            txtNewFragmentText.Clear();
            SaveFragments(); // Persist change
            txtNewFragmentTitle.Focus();
             UpdateStatusBarAndButtonStates(); // Update AI Copy button state potentially
        }

        private void DeleteFragment_Click(object sender, RoutedEventArgs e)
        {
            var itemsToRemove = listViewFragments.SelectedItems.Cast<TextFragment>().ToList();
            if (itemsToRemove.Count == 0) {
                MessageBox.Show("Seleccione fragmentos para eliminar.", "Eliminar Fragmento", MessageBoxButton.OK, MessageBoxImage.Information); return;
            }

            if (MessageBox.Show($"¿Eliminar {itemsToRemove.Count} fragmento(s) seleccionado(s)?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var item in itemsToRemove) { Fragments.Remove(item); }
                SaveFragments(); // Persist change
                UpdateStatusBarAndButtonStates(); // Update AI Copy button state potentially
            }
        }

        // --- Fragment Persistence (Called from Main Window Load/Close) ---

        private void LoadFragments()
        {
            if (!File.Exists(_fragmentsFilePath)) return;
            try
            {
                string json = File.ReadAllText(_fragmentsFilePath);
                var loadedFragments = JsonSerializer.Deserialize<List<TextFragment>>(json);
                if (loadedFragments != null)
                {
                    Fragments.Clear();
                    foreach (var fragment in loadedFragments.Where(f => !string.IsNullOrEmpty(f.Title) && f.Text != null))
                    {
                        Fragments.Add(fragment);
                    }
                }
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error loading fragments: {ex.Message}");
                 MessageBox.Show($"No se pudieron cargar los fragmentos guardados.\nError: {ex.Message}", "Error Carga Fragmentos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveFragments()
        {
             try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Fragments, options);
                File.WriteAllText(_fragmentsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving fragments: {ex.Message}");
                 MessageBox.Show($"No se pudieron guardar los fragmentos.\nError: {ex.Message}", "Error Guardar Fragmentos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // --- Fragment Editing (Double Click) ---
        private void listViewFragments_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Find the ListViewItem that was clicked
            DependencyObject? originalSource = e.OriginalSource as DependencyObject;
            ListViewItem? listViewItem = null;
            while (originalSource != null && originalSource != listViewFragments) {
                if (originalSource is ListViewItem item) { listViewItem = item; break; }
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            if (listViewItem?.DataContext is TextFragment fragmentToEdit)
            {
                // Assuming EditFragmentDialog exists and works as before
                var dialog = new EditFragmentDialog(fragmentToEdit) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    var editedData = dialog.EditedFragment;
                    // Update the fragment directly (assuming TextFragment implements INotifyPropertyChanged if needed)
                    fragmentToEdit.Title = editedData.Title;
                    fragmentToEdit.Text = editedData.Text;
                    SaveFragments(); // Persist changes
                    UpdateStatusBarAndButtonStates($"Fragmento '{fragmentToEdit.Title}' actualizado.");
                }
            }
        }

    } // End partial class MainWindow
} // End namespace MakingVibe