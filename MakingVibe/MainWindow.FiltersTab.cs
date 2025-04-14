/*
 * MakingVibe/MainWindow.FiltersTab.cs
 * Partial class file for Filters Tab logic.
 * Handles populating the filters list based on scanned files,
 * managing filter selection (Select/Deselect All), and applying filters.
 */
using MakingVibe.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Cursors = System.Windows.Input.Cursors; // Alias
using MessageBox = System.Windows.MessageBox; // Alias


namespace MakingVibe
{
    public partial class MainWindow // : Window // Already declared
    {
        // Note: FileFilters, _activeFileExtensionsFilter fields are in the main partial class

        // --- Filter List Population (Called by TreeView Load) ---

        /// <summary>
        /// Updates the FileFilters ObservableCollection based on found extensions, preserving states.
        /// </summary>
        private void UpdateFileFiltersUI(Dictionary<string, int> foundExtensions)
        {
            var currentStates = FileFilters.ToDictionary(f => f.Extension, f => f.IsEnabled);
            FileFilters.Clear();

            foreach (var kvp in foundExtensions.OrderBy(kv => kv.Key))
            {
                bool isEnabled = currentStates.TryGetValue(kvp.Key, out bool previousState) ? previousState : true; // Default to enabled, restore state if found

                FileFilters.Add(new FileFilterItem
                {
                    Extension = kvp.Key,
                    Count = kvp.Value,
                    IsEnabled = isEnabled
                });
            }
             // After updating the list, re-evaluate button states
             UpdateStatusBarAndButtonStates();
        }


        // --- Filter Adjustment Helper (Called by Rename/Delete) ---

        /// <summary>
        /// Adjusts the count for a specific filter extension. Adds/removes if necessary.
        /// </summary>
        private void AdjustFilterCount(string extension, int change)
        {
             if (string.IsNullOrEmpty(extension)) extension = "[Sin extensión]"; // Normalize blank extension

             var filterItem = FileFilters.FirstOrDefault(f => f.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));
             if (filterItem != null)
             {
                 filterItem.Count += change;
                 if (filterItem.Count <= 0)
                 {
                     // Remove filter if count reaches zero
                     FileFilters.Remove(filterItem);
                 }
             }
             else if (change > 0) // Add new filter if it didn't exist and count is positive
             {
                 var newFilter = new FileFilterItem { Extension = extension, Count = change, IsEnabled = true }; // Assume enabled
                 FileFilters.Add(newFilter);
                 // Re-sort the list
                 var sortedFilters = FileFilters.OrderBy(f => f.Extension).ToList();
                 FileFilters.Clear();
                 foreach (var f in sortedFilters) FileFilters.Add(f);
             }
              // Update buttons after filter list changes
             UpdateStatusBarAndButtonStates();
        }

        // --- Filter Tab Button Handlers ---

        private void SelectAllFilters_Click(object sender, RoutedEventArgs e)
        {
            foreach (var filter in FileFilters) { filter.IsEnabled = true; }
            // Optionally apply immediately: ApplyFiltersAndReloadTree();
        }

        private void DeselectAllFilters_Click(object sender, RoutedEventArgs e)
        {
            foreach (var filter in FileFilters) { filter.IsEnabled = false; }
             // Optionally apply immediately: ApplyFiltersAndReloadTree();
        }

        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            ApplyFiltersAndReloadTree();
        }

        /// <summary>
        /// Updates the active filter set from UI choices and reloads the TreeView.
        /// </summary>
        private void ApplyFiltersAndReloadTree()
        {
             if (string.IsNullOrEmpty(rootPath)) {
                 MessageBox.Show("Seleccione una carpeta raíz primero.", "Aplicar Filtros", MessageBoxButton.OK, MessageBoxImage.Information);
                 return;
             }

             UpdateStatusBarAndButtonStates("Aplicando filtros y recargando árbol...");
             this.Cursor = Cursors.Wait;

             // Update the active filter HashSet
             _activeFileExtensionsFilter = FileFilters
                 .Where(f => f.IsEnabled)
                 .Select(f => f.Extension)
                 .ToHashSet(StringComparer.OrdinalIgnoreCase);

             // Reload the tree (Method in TreeView partial)
             // LoadDirectoryTreeUI uses _activeFileExtensionsFilter automatically
             LoadDirectoryTreeUI(rootPath);

             this.Cursor = Cursors.Arrow;
             UpdateStatusBarAndButtonStates("Filtros aplicados y árbol recargado.");
        }

    } // End partial class MainWindow
} // End namespace MakingVibe