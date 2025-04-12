using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace MakingVibe;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private string rootPath;
    private readonly ObservableCollection<FileSystemItem> selectedItems = new();
    private readonly Dictionary<string, FileSystemItem> pathToItemMap = new();
    private string clipboardPath;
    private bool isCutOperation;

    public MainWindow()
    {
        InitializeComponent();
        listViewSelected.ItemsSource = selectedItems;
    }

    private void btnSelectRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        dialog.Description = "Seleccione la carpeta raíz";
        dialog.ShowNewFolderButton = false;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            rootPath = dialog.SelectedPath;
            txtCurrentPath.Text = $"Ruta actual: {rootPath}";
            LoadDirectoryTree(rootPath);
        }
    }

    private void LoadDirectoryTree(string path)
    {
        try
        {
            treeViewFiles.Items.Clear();
            pathToItemMap.Clear();
            selectedItems.Clear();

            var rootDirectory = new DirectoryInfo(path);
            var rootItem = CreateTreeViewItemForDirectory(rootDirectory);
            rootItem.IsExpanded = true;
            treeViewFiles.Items.Add(rootItem);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar el directorio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private TreeViewItem CreateTreeViewItemForDirectory(DirectoryInfo directoryInfo)
    {
        var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
        
        var checkbox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Tag = directoryInfo.FullName
        };
        checkbox.Checked += Checkbox_Checked;
        checkbox.Unchecked += Checkbox_Unchecked;
        
        var textBlock = new TextBlock
        {
            Text = directoryInfo.Name,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        stackPanel.Children.Add(checkbox);
        stackPanel.Children.Add(textBlock);

        var treeViewItem = new TreeViewItem
        {
            Header = stackPanel,
            Tag = directoryInfo.FullName
        };

        // Crear un objeto FileSystemItem para este directorio
        var fileSystemItem = new FileSystemItem
        {
            Name = directoryInfo.Name,
            Path = directoryInfo.FullName,
            Type = "Directorio",
            IsDirectory = true
        };
        pathToItemMap[directoryInfo.FullName] = fileSystemItem;

        // Agregar un elemento dummy para permitir la expansión
        treeViewItem.Items.Add("Cargando...");

        treeViewItem.Expanded += TreeViewItem_Expanded;
        treeViewItem.Selected += TreeViewItem_Selected;

        return treeViewItem;
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        var treeViewItem = sender as TreeViewItem;
        if (treeViewItem?.Tag == null || treeViewItem.Items.Count != 1 || treeViewItem.Items[0] is not string loadingText || loadingText != "Cargando...")
            return;

        treeViewItem.Items.Clear();
        var directoryPath = treeViewItem.Tag.ToString();

        try
        {
            // Cargar directorios
            foreach (var directoryInfo in new DirectoryInfo(directoryPath).GetDirectories().OrderBy(d => d.Name))
            {
                treeViewItem.Items.Add(CreateTreeViewItemForDirectory(directoryInfo));
            }

            // Cargar archivos
            foreach (var fileInfo in new DirectoryInfo(directoryPath).GetFiles().OrderBy(f => f.Name))
            {
                treeViewItem.Items.Add(CreateTreeViewItemForFile(fileInfo));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al expandir el directorio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private TreeViewItem CreateTreeViewItemForFile(FileInfo fileInfo)
    {
        var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
        
        var checkbox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Tag = fileInfo.FullName
        };
        checkbox.Checked += Checkbox_Checked;
        checkbox.Unchecked += Checkbox_Unchecked;
        
        var textBlock = new TextBlock
        {
            Text = fileInfo.Name,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        stackPanel.Children.Add(checkbox);
        stackPanel.Children.Add(textBlock);

        var treeViewItem = new TreeViewItem
        {
            Header = stackPanel,
            Tag = fileInfo.FullName
        };

        // Crear un objeto FileSystemItem para este archivo
        var fileSystemItem = new FileSystemItem
        {
            Name = fileInfo.Name,
            Path = fileInfo.FullName,
            Type = fileInfo.Extension,
            IsDirectory = false
        };
        pathToItemMap[fileInfo.FullName] = fileSystemItem;

        treeViewItem.Selected += TreeViewItem_Selected;

        return treeViewItem;
    }

    private void Checkbox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox && checkbox.Tag is string path && pathToItemMap.TryGetValue(path, out var item))
        {
            if (!selectedItems.Contains(item))
            {
                selectedItems.Add(item);
            }
            UpdateButtonStates();
        }
    }

    private void Checkbox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox && checkbox.Tag is string path && pathToItemMap.TryGetValue(path, out var item))
        {
            selectedItems.Remove(item);
            UpdateButtonStates();
        }
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem treeViewItem && treeViewItem.Tag is string path)
        {
            try
            {
                if (File.Exists(path) && IsTextFile(path))
                {
                    string content = File.ReadAllText(path);
                    txtFileContent.Text = content;
                    tabControlMain.SelectedIndex = 0; // Switch to preview tab
                }
                else
                {
                    txtFileContent.Text = $"No se puede previsualizar este tipo de archivo: {path}";
                }
            }
            catch (Exception ex)
            {
                txtFileContent.Text = $"Error al leer el archivo: {ex.Message}";
            }
        }
    }

    private bool IsTextFile(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            ".txt" or ".log" or ".xml" or ".json" or ".html" or ".htm" or ".css" or ".js" or 
            ".cs" or ".xaml" or ".config" or ".ini" or ".bat" or ".cmd" or ".ps1" or 
            ".sh" or ".md" or ".csv" or ".tsv" => true,
            _ => false
        };
    }

    private void UpdateButtonStates()
    {
        bool hasSelection = selectedItems.Count > 0;
        btnCopy.IsEnabled = hasSelection;
        btnCut.IsEnabled = hasSelection;
        btnDelete.IsEnabled = hasSelection;
        btnRename.IsEnabled = selectedItems.Count == 1;
        btnPaste.IsEnabled = !string.IsNullOrEmpty(clipboardPath);
    }

    private void btnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count == 0) return;
        
        // Si hay múltiples elementos, usamos el directorio del primero como referencia
        clipboardPath = selectedItems.First().Path;
        isCutOperation = false;
        btnPaste.IsEnabled = true;
        
        MessageBox.Show($"Se {(selectedItems.Count == 1 ? "ha copiado 1 elemento" : $"han copiado {selectedItems.Count} elementos")} al portapapeles", 
            "Copiar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void btnCut_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count == 0) return;
        
        clipboardPath = selectedItems.First().Path;
        isCutOperation = true;
        btnPaste.IsEnabled = true;
        
        MessageBox.Show($"Se {(selectedItems.Count == 1 ? "ha cortado 1 elemento" : $"han cortado {selectedItems.Count} elementos")} al portapapeles", 
            "Cortar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void btnPaste_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(clipboardPath) || treeViewFiles.SelectedItem is not TreeViewItem selectedItem || 
            selectedItem.Tag is not string targetPath)
        {
            MessageBox.Show("Seleccione un directorio destino para pegar", "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string destinationPath = targetPath;
        
        // Verificar si el destino seleccionado es un archivo
        if (File.Exists(destinationPath))
        {
            // Si es un archivo, usamos su directorio padre
            destinationPath = Path.GetDirectoryName(destinationPath);
        }

        try
        {
            foreach (var item in selectedItems)
            {
                string sourcePath = item.Path;
                string fileName = Path.GetFileName(sourcePath);
                string destFile = Path.Combine(destinationPath, fileName);

                if (item.IsDirectory)
                {
                    // Es un directorio
                    if (Directory.Exists(sourcePath))
                    {
                        if (!Directory.Exists(destFile))
                            CopyDirectory(sourcePath, destFile);
                        
                        if (isCutOperation)
                            Directory.Delete(sourcePath, true);
                    }
                }
                else
                {
                    // Es un archivo
                    if (File.Exists(sourcePath))
                    {
                        File.Copy(sourcePath, destFile, true);
                        
                        if (isCutOperation)
                            File.Delete(sourcePath);
                    }
                }
            }

            if (isCutOperation)
            {
                selectedItems.Clear();
                clipboardPath = null;
                btnPaste.IsEnabled = false;
            }

            // Recargar el árbol
            LoadDirectoryTree(rootPath);
            
            MessageBox.Show("Operación completada con éxito", "Pegar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al pegar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        // Crear el directorio destino si no existe
        Directory.CreateDirectory(destDir);

        // Copiar todos los archivos
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        // Copiar subdirectorios recursivamente
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    private void btnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count == 0) return;

        string message = selectedItems.Count == 1
            ? $"¿Está seguro de que desea eliminar '{selectedItems[0].Name}'?"
            : $"¿Está seguro de que desea eliminar {selectedItems.Count} elementos?";

        if (MessageBox.Show(message, "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                foreach (var item in selectedItems.ToList())
                {
                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(item.Path))
                            Directory.Delete(item.Path, true);
                    }
                    else
                    {
                        if (File.Exists(item.Path))
                            File.Delete(item.Path);
                    }
                }

                selectedItems.Clear();
                LoadDirectoryTree(rootPath);
                MessageBox.Show("Elementos eliminados con éxito", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void btnRename_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItems.Count != 1) return;

        var item = selectedItems[0];
        
        var dialog = new RenameDialog(Path.GetFileName(item.Path));
        if (dialog.ShowDialog() == true)
        {
            try
            {
                string directory = Path.GetDirectoryName(item.Path);
                string newPath = Path.Combine(directory, dialog.NewName);

                if (item.IsDirectory)
                {
                    Directory.Move(item.Path, newPath);
                }
                else
                {
                    File.Move(item.Path, newPath);
                }

                selectedItems.Clear();
                LoadDirectoryTree(rootPath);
                MessageBox.Show("Elemento renombrado con éxito", "Renombrar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al renombrar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(rootPath))
        {
            LoadDirectoryTree(rootPath);
        }
    }
}

// Clase para representar un elemento del sistema de archivos (archivo o directorio)
public class FileSystemItem
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Type { get; set; }
    public bool IsDirectory { get; set; }
}