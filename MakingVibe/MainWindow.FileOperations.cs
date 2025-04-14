/*
 * MakingVibe/MainWindow.FileOperations.cs
 * Partial class file for standard file operations logic.
 * Handles Copy, Cut, Paste, Delete, Rename actions triggered by toolbar or context menu.
 * (Version with compiler errors fixed)
 */
using MakingVibe.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls; // Required for Grid
using System.Windows.Input;
using Cursors = System.Windows.Input.Cursors; // Alias
using MessageBox = System.Windows.MessageBox; // Alias
using CheckBox = System.Windows.Controls.CheckBox; // Explicit Alias for Rename re-select


namespace MakingVibe
{
    public partial class MainWindow // : Window // Already declared
    {
        // Note: clipboardItems and isCutOperation fields are declared in the main partial class

        // --- Standard File Operations (UI Coordination) ---

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;
            clipboardItems = selectedItems.ToList(); // Store selected items
            isCutOperation = false;
            UpdateStatusBarAndButtonStates($"Copiado(s) {clipboardItems.Count} elemento(s) al portapapeles.");
        }

        private void btnCut_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;
            clipboardItems = selectedItems.ToList();
            isCutOperation = true;
            UpdateStatusBarAndButtonStates($"Cortado(s) {clipboardItems.Count} elemento(s) al portapapeles.");
        }

        private void btnPaste_Click(object sender, RoutedEventArgs e)
        {
            if (clipboardItems.Count == 0)
            {
                MessageBox.Show("El portapapeles está vacío.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Determine Target Directory
            FileSystemItem? targetFsi = (treeViewFiles.SelectedItem as TreeViewItem)?.Tag as FileSystemItem;
            string destinationDir;

            if (targetFsi != null)
            {
                destinationDir = targetFsi.IsDirectory ? targetFsi.Path : (_fileSystemService.GetDirectoryName(targetFsi.Path) ?? rootPath ?? "");
            }
            else if (!string.IsNullOrEmpty(rootPath))
            {
                destinationDir = rootPath; // Paste into root if nothing selected
            }
            else
            {
                 MessageBox.Show("Seleccione un directorio destino en el árbol o asegúrese de que haya una carpeta raíz cargada.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

             if (string.IsNullOrEmpty(destinationDir))
            {
                MessageBox.Show("No se pudo determinar el directorio destino.", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            UpdateStatusBarAndButtonStates("Pegando elementos...");
            this.Cursor = Cursors.Wait;

            bool refreshNeeded = false;
            string? refreshParentPath = null; // Parent where items are pasted
            TreeViewItem? sourceParentNode = null;
            string? sourceRefreshParentPath = null; // Parent path from where items were cut
            int pasteCount = 0;
            var itemsToProcess = clipboardItems.ToList(); // Work on a copy

            try
            {
                foreach (var itemToPaste in itemsToProcess)
                {
                    string sourcePath = itemToPaste.Path;
                    string targetName = _fileSystemService.GetFileName(sourcePath);
                    string fullTargetPath = _fileSystemService.CombinePath(destinationDir, targetName);
                    string? sourceParent = isCutOperation ? _fileSystemService.GetDirectoryName(sourcePath) : null;

                    // --- Basic Checks ---
                    if (string.Equals(sourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase)) continue; // Pasting onto itself
                    if (isCutOperation && string.Equals(sourceParent, destinationDir, StringComparison.OrdinalIgnoreCase)) continue; // Moving to same folder

                    if ((itemToPaste.IsDirectory && !_fileSystemService.DirectoryExists(sourcePath)) ||
                        (!itemToPaste.IsDirectory && !_fileSystemService.FileExists(sourcePath)))
                    {
                        Debug.WriteLine($"Source path not found for paste/move: {sourcePath}");
                        MessageBox.Show($"El elemento de origen '{itemToPaste.Name}' ya no existe.", "Error al Pegar/Mover", MessageBoxButton.OK, MessageBoxImage.Warning);
                        if (isCutOperation) clipboardItems.RemoveAll(ci => ci.Path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase)); // Remove from actual clipboard
                        continue;
                    }

                    if (itemToPaste.IsDirectory && destinationDir.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"No se puede {(isCutOperation ? "mover" : "copiar")} la carpeta '{itemToPaste.Name}' dentro de sí misma.", "Operación Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    // --- Handle Overwrite ---
                    bool targetExists = itemToPaste.IsDirectory
                        ? _fileSystemService.DirectoryExists(fullTargetPath)
                        : _fileSystemService.FileExists(fullTargetPath);

                    if (targetExists)
                    {
                        var overwriteResult = MessageBox.Show($"El elemento '{targetName}' ya existe en el destino. ¿Desea sobrescribirlo?", "Conflicto al Pegar", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                        if (overwriteResult == MessageBoxResult.Cancel) break; // Stop whole paste operation
                        if (overwriteResult == MessageBoxResult.No) continue; // Skip this item

                        // Try to delete existing target before pasting/moving
                        try
                        {
                            // **FIX CS9035:** Provide required Name and Type
                            string targetDeleteName = targetName; // Use the calculated target name
                            string targetDeleteType = itemToPaste.IsDirectory ? "Directorio" : Path.GetExtension(fullTargetPath); // Determine type

                            var targetToDeleteFsi = new FileSystemItem {
                                Path = fullTargetPath,
                                IsDirectory = itemToPaste.IsDirectory,
                                Name = targetDeleteName, // Set required Name
                                Type = targetDeleteType   // Set required Type
                            };
                            _fileSystemService.DeleteItem(targetToDeleteFsi); // Pass the fully initialized item

                            // Clean up map entry for the overwritten item ITSELF
                            if (pathToTreeViewItemMap.ContainsKey(fullTargetPath)) pathToTreeViewItemMap.Remove(fullTargetPath);

                            // Clean up map entries for DESCENDANTS if overwriting a directory
                            if (targetToDeleteFsi.IsDirectory) {
                                var descendantPaths = pathToTreeViewItemMap.Keys
                                    .Where(k => k.StartsWith(fullTargetPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                                foreach(var descendantPath in descendantPaths) {
                                    pathToTreeViewItemMap.Remove(descendantPath);
                                }
                            }
                        }
                        catch (Exception delEx)
                        {
                            MessageBox.Show($"No se pudo sobrescribir '{targetName}'. Error: {delEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                            continue; // Skip this item
                        }
                    }

                    // --- Perform Copy/Move ---
                    try
                    {
                        if (isCutOperation)
                        {
                             // Store source parent node before potential removal from map
                            if (sourceParent != null && sourceParentNode == null) { // Only get it once
                                pathToTreeViewItemMap.TryGetValue(sourceParent, out sourceParentNode);
                            }

                            _fileSystemService.MoveItem(sourcePath, fullTargetPath, itemToPaste.IsDirectory);
                            pathToTreeViewItemMap.Remove(sourcePath); // Remove old path from map

                            // **FIX CS1061:** Replace RemoveAll with Remove for ObservableCollection
                            var itemToRemoveFromSelection = selectedItems.FirstOrDefault(si => si.Path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));
                            if (itemToRemoveFromSelection != null)
                            {
                                selectedItems.Remove(itemToRemoveFromSelection);
                            }
                            // End FIX CS1061

                            if (sourceParent != null) sourceRefreshParentPath = sourceParent; // Mark source parent path for refresh logic
                        }
                        else
                        {
                            _fileSystemService.CopyItem(sourcePath, fullTargetPath, itemToPaste.IsDirectory);
                        }

                        pasteCount++;
                        refreshNeeded = true;
                        refreshParentPath = destinationDir; // Mark destination parent path for refresh logic
                    }
                    catch (Exception opEx)
                    {
                        MessageBox.Show($"Error al {(isCutOperation ? "mover" : "copiar")} '{itemToPaste.Name}':\n{opEx.Message}", "Error al Pegar", MessageBoxButton.OK, MessageBoxImage.Error);
                        break; // Stop on error
                    }
                } // End foreach loop

                // --- Post-Operation Cleanup ---
                if (isCutOperation && pasteCount > 0)
                {
                    // Remove successfully moved items from clipboardItems
                    // Filter itemsToProcess to only include those successfully pasted
                    var successfullyPastedPaths = itemsToProcess.Take(pasteCount).Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    clipboardItems.RemoveAll(ci => successfullyPastedPaths.Contains(ci.Path));

                    if (clipboardItems.Count == 0) // If all were moved or clipboard is now empty
                    {
                        isCutOperation = false; // Clear cut state
                    }
                }

                // --- Refresh UI ---
                 if (refreshNeeded && !string.IsNullOrEmpty(rootPath))
                {
                    bool refreshedNodes = false;
                    // Refresh source first if it's a cut operation and the node was found
                    if (sourceRefreshParentPath != null && sourceParentNode?.Tag is FileSystemItem sourceParentFsi)
                    {
                        RefreshNodeUI(sourceParentNode, sourceParentFsi); // Method in TreeView partial
                        refreshedNodes = true;
                    }

                    // Refresh destination
                    if (refreshParentPath != null && pathToTreeViewItemMap.TryGetValue(refreshParentPath, out var destParentNode))
                    {
                         if (destParentNode.Tag is FileSystemItem destParentFsi)
                         {
                             RefreshNodeUI(destParentNode, destParentFsi); // Method in TreeView partial
                             refreshedNodes = true;
                         }
                    }

                    // If specific nodes couldn't be refreshed (e.g., parent was root, or not found), do a full reload
                    if (!refreshedNodes || sourceRefreshParentPath == rootPath || refreshParentPath == rootPath)
                    {
                        LoadDirectoryTreeUI(rootPath); // Method in TreeView partial
                    }
                 }
                // --- End Refresh UI ---

                 // Update status message
                 if (pasteCount > 0)
                 {
                     string destName = string.IsNullOrEmpty(destinationDir) ? "destino" : _fileSystemService.GetFileName(destinationDir);
                     if (string.IsNullOrEmpty(destName) && destinationDir == rootPath) destName = "carpeta raíz"; // Handle root case
                     bool cutCompleted = isCutOperation && clipboardItems.Count == 0; // Check if clipboard is now empty
                     UpdateStatusBarAndButtonStates($"{pasteCount} elemento(s) {(cutCompleted ? "movido(s)" : "pegado(s)")} en '{destName}'.");
                 }
                 else
                 {
                     UpdateStatusBarAndButtonStates("No se pegaron elementos.");
                 }

            }
            catch (Exception ex) // Catch unexpected errors during the loop or refresh
            {
                MessageBox.Show($"Error inesperado durante la operación de pegado:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBarAndButtonStates("Error al pegar.");
                if (!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath); // Fallback refresh
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
                UpdateStatusBarAndButtonStates(); // Final update
            }
        }

        // Note: This handles clicks from both the Toolbar button and the Context Menu item
        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count == 0) return;

            string message = selectedItems.Count == 1
                ? $"¿Está seguro de que desea eliminar '{selectedItems.First().Name}'?"
                : $"¿Está seguro de que desea eliminar {selectedItems.Count} elementos seleccionados?";

            if (MessageBox.Show(message, "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                UpdateStatusBarAndButtonStates("Eliminando...");
                this.Cursor = Cursors.Wait;
                int deleteCount = 0;
                var itemsToDelete = selectedItems.ToList(); // Copy selection
                var parentPathsToRefresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // --- Clear selection BEFORE deleting files ---
                ClearAllSelectionsUI(); // Method in TreeView partial class

                try
                {
                    foreach (var item in itemsToDelete) // Iterate over the copy
                    {
                        try
                        {
                            string? parentPath = _fileSystemService.GetDirectoryName(item.Path);
                            _fileSystemService.DeleteItem(item); // Service handles file/dir deletion

                            // --- Clean up TreeView and Map ---
                            // Remove item itself
                            if (pathToTreeViewItemMap.TryGetValue(item.Path, out var treeViewItemToDelete))
                            {
                                // Remove the TreeViewItem from its parent in the UI
                                if (treeViewItemToDelete.Parent is TreeViewItem parentTvi) {
                                    parentTvi.Items.Remove(treeViewItemToDelete);
                                } else if (treeViewFiles.Items.Contains(treeViewItemToDelete)) {
                                    treeViewFiles.Items.Remove(treeViewItemToDelete); // Root item case
                                }
                                pathToTreeViewItemMap.Remove(item.Path); // Remove from map
                            }

                            // Remove descendants from map if it was a directory
                            if (item.IsDirectory) {
                                var descendantPaths = pathToTreeViewItemMap.Keys
                                    .Where(k => k.StartsWith(item.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                                foreach(var descendantPath in descendantPaths) {
                                    pathToTreeViewItemMap.Remove(descendantPath);
                                }
                            }
                            // --- End TreeView/Map Cleanup ---

                            if (parentPath != null && parentPath.StartsWith(rootPath ?? "__INVALID__", StringComparison.OrdinalIgnoreCase))
                                parentPathsToRefresh.Add(parentPath);

                            // Adjust filter count if it was a file
                            if (!item.IsDirectory) AdjustFilterCount(item.Type, -1); // Method in FiltersTab partial

                            deleteCount++;
                        }
                        catch (IOException ioEx) { MessageBox.Show($"No se pudo eliminar '{item.Name}'. En uso.\nError: {ioEx.Message}", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Warning); }
                        catch (UnauthorizedAccessException uaEx) { MessageBox.Show($"No se pudo eliminar '{item.Name}'. Permiso denegado.\nError: {uaEx.Message}", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Warning); }
                        catch (Exception itemEx) { MessageBox.Show($"No se pudo eliminar '{item.Name}'.\nError: {itemEx.Message}", "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Error); }
                    } // End foreach loop

                    // --- Refresh Parent Nodes or Full Tree ---
                    if (deleteCount > 0 && !string.IsNullOrEmpty(rootPath))
                    {
                         bool needFullRefresh = false;
                         if (parentPathsToRefresh.Any())
                         {
                             foreach (var parentPath in parentPathsToRefresh)
                             {
                                 if (pathToTreeViewItemMap.TryGetValue(parentPath, out var parentNode))
                                 {
                                     if (parentNode.Tag is FileSystemItem parentFsi)
                                         RefreshNodeUI(parentNode, parentFsi); // Method in TreeView partial
                                     else needFullRefresh = true;
                                 }
                                 else if (parentPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                                     needFullRefresh = true; // Parent was root
                                 else needFullRefresh = true; // Parent not in map

                                 if (needFullRefresh) break;
                             }
                         } else if (itemsToDelete.Any(itd => _fileSystemService.GetDirectoryName(itd.Path) == rootPath)) {
                             needFullRefresh = true;
                         } else if (deleteCount > 0) {
                              needFullRefresh = itemsToDelete.Any(itd => itd.Path.Equals(rootPath, StringComparison.OrdinalIgnoreCase));
                         }


                        if (needFullRefresh) LoadDirectoryTreeUI(rootPath); // Method in TreeView partial
                    }
                    // --- End Refresh Logic ---

                    UpdateStatusBarAndButtonStates($"{deleteCount} elemento(s) eliminado(s).");
                }
                catch (Exception ex) // Catch unexpected error during the process
                {
                    MessageBox.Show($"Error inesperado durante la eliminación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates("Error al eliminar.");
                    if (!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath); // Fallback refresh
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                    UpdateStatusBarAndButtonStates(); // Final update
                }
            }
        }

        // Note: This handles clicks from both the Toolbar button and the Context Menu item
        private void btnRename_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItems.Count != 1) return;

            var itemToRename = selectedItems[0];
            string oldPath = itemToRename.Path;
            string? directory = _fileSystemService.GetDirectoryName(oldPath);

            if (directory == null)
            {
                MessageBox.Show("No se puede determinar el directorio del elemento.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Assuming RenameDialog exists and works as before
            var dialog = new RenameDialog(itemToRename.Name) { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                string newName = dialog.NewName.Trim();
                if (string.IsNullOrEmpty(newName) || newName.Equals(itemToRename.Name, StringComparison.Ordinal)) return; // No change or invalid

                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
                    MessageBox.Show($"El nombre '{newName}' contiene caracteres no válidos.", "Nombre Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string newPath = _fileSystemService.CombinePath(directory, newName);
                string oldExtension = itemToRename.Type;
                string newExtension = itemToRename.IsDirectory ? itemToRename.Type : Path.GetExtension(newPath);

                if ((itemToRename.IsDirectory && _fileSystemService.DirectoryExists(newPath)) ||
                    (!itemToRename.IsDirectory && _fileSystemService.FileExists(newPath)))
                {
                    MessageBox.Show($"Ya existe un elemento llamado '{newName}' en esta ubicación.", "Renombrar Fallido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                UpdateStatusBarAndButtonStates($"Renombrando '{itemToRename.Name}'...");
                this.Cursor = Cursors.Wait;
                string oldNameForStatus = itemToRename.Name;

                // --- Cleanup map and selection before actual rename ---
                selectedItems.Remove(itemToRename);
                if (pathToTreeViewItemMap.ContainsKey(oldPath)) pathToTreeViewItemMap.Remove(oldPath);
                if (itemToRename.IsDirectory) {
                    var descendantPaths = pathToTreeViewItemMap.Keys
                        .Where(k => k.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach(var descendantPath in descendantPaths) {
                        pathToTreeViewItemMap.Remove(descendantPath);
                    }
                }
                // --- End Cleanup ---

                try
                {
                    _fileSystemService.RenameItem(oldPath, newPath, itemToRename.IsDirectory);

                    // --- Refresh parent node ---
                    bool refreshed = false;
                    if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                    {
                        if (parentNode.Tag is FileSystemItem parentFsi)
                        {
                            RefreshNodeUI(parentNode, parentFsi); // Method in TreeView partial
                            refreshed = true;

                            // --- Try to re-select the renamed item (async) ---
                            Dispatcher.InvokeAsync(() => {
                                bool isVisible = true;
                                if (!itemToRename.IsDirectory && _activeFileExtensionsFilter.Any() && !_activeFileExtensionsFilter.Contains(newExtension))
                                {
                                    isVisible = false;
                                }

                                if (isVisible && pathToTreeViewItemMap.TryGetValue(newPath, out var newTvi))
                                {
                                    newTvi.IsSelected = true;
                                    if (newTvi.Header is Grid headerGrid) {
                                        var cb = headerGrid.Children.OfType<CheckBox>().FirstOrDefault();
                                        if (cb != null)
                                        {
                                            cb.IsChecked = true;
                                        }
                                    }
                                    newTvi.BringIntoView();
                                    UpdateStatusBarAndButtonStates($"'{oldNameForStatus}' renombrado a '{newName}'.");
                                } else {
                                    UpdateStatusBarAndButtonStates($"'{oldNameForStatus}' renombrado a '{newName}' (filtrado).");
                                }
                            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                            // --- End Try re-select ---
                        }
                    }

                    if (!refreshed || directory.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                        UpdateStatusBarAndButtonStates($"'{oldNameForStatus}' renombrado a '{newName}'.");
                    }
                    // --- End Refresh ---

                    if (!itemToRename.IsDirectory && !oldExtension.Equals(newExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        AdjustFilterCount(oldExtension, -1); // Method in FiltersTab partial
                        AdjustFilterCount(newExtension, 1); // Method in FiltersTab partial
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al renombrar '{oldNameForStatus}':\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBarAndButtonStates($"Error al renombrar '{oldNameForStatus}'.");
                    if (pathToTreeViewItemMap.TryGetValue(directory, out var parentNode))
                    {
                        if (parentNode.Tag is FileSystemItem parentFsi) RefreshNodeUI(parentNode, parentFsi);
                        else if (!string.IsNullOrEmpty(rootPath)) LoadDirectoryTreeUI(rootPath);
                    } else if (!string.IsNullOrEmpty(rootPath)) {
                        LoadDirectoryTreeUI(rootPath);
                    }
                }
                finally
                {
                    this.Cursor = Cursors.Arrow;
                    // **FIX CS1503:** Use lambda for Dispatcher.InvokeAsync
                    Dispatcher.InvokeAsync(() => UpdateStatusBarAndButtonStates(), System.Windows.Threading.DispatcherPriority.ContextIdle);
                    // End FIX CS1503
                }
            }
        }

    } // End partial class MainWindow
} // End namespace MakingVibe