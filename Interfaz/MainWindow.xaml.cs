using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RobloxScriptExplorer.Logica;
using Path = System.IO.Path;

namespace RobloxScriptExplorer.Interfaz
{
    public partial class MainWindow : Window
    {
        private readonly RbxlPlaceManager _manager = new();
        private RobloxInstance? _selectedInstance;
        private readonly HashSet<int> _selectedIds = new();
        private readonly Dictionary<int, Border> _itemRowBorders = new();
        private bool _isUpdatingEditorText = false;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    MemoryOptimizer.TrimMemory();
                }));
            };

            StateChanged += (s, e) =>
            {
                MemoryOptimizer.TrimMemory();
            };
        }

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar archivo de Roblox Studio",
                Filter = "Roblox Place Binary (*.rbxl)|*.rbxl|Todos los archivos (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                await LoadPlaceAsync(dlg.FileName);
            }
        }

        private async Task LoadPlaceAsync(string filePath)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LblOverlayTitle.Text = "Cargando Archivo de Roblox Studio...";
            ProgressLoading.Value = 5;
            LblLoadingStatus.Text = "Iniciando lectura de archivo...";

            try
            {
                await Task.Run(async () =>
                {
                    await _manager.LoadAsync(filePath, (status, progress) =>
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                        {
                            LblLoadingStatus.Text = status;
                            ProgressLoading.Value = progress * 100;
                        }));
                    });
                });

                LblCurrentFile.Text = Path.GetFileName(filePath);
                BtnSave.IsEnabled = true;
                BtnExportAll.IsEnabled = true;
                BtnExportSelected.IsEnabled = true;
                BtnExportModel.IsEnabled = true;
                BtnAdd.IsEnabled = true;
                BtnDelete.IsEnabled = true;
                _selectedIds.Clear();
                _itemRowBorders.Clear();
                LblMultiSelectCount.Text = "";

                PopulateTreeLazy();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el archivo:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void PopulateTreeLazy()
        {
            TreeHierarchy.Items.Clear();
            _itemRowBorders.Clear();

            string[] priorityServices = {
                "Workspace", "ServerScriptService", "ReplicatedStorage",
                "StarterGui", "StarterPlayer", "ServerStorage",
                "ReplicatedFirst", "Lighting", "SoundService", "Chat"
            };

            var roots = _manager.Instances.Values
                .Where(inst => inst.IsService)
                .ToList();

            var sortedRoots = roots
                .OrderByDescending(r => Array.IndexOf(priorityServices, r.Name) >= 0)
                .ThenBy(r => {
                    int idx = Array.IndexOf(priorityServices, r.Name);
                    return idx >= 0 ? idx : 999;
                })
                .ThenByDescending(r => r.ChildrenIds.Count > 0)
                .ThenBy(r => r.Name)
                .ToList();

            foreach (var rootInst in sortedRoots)
            {
                var item = CreateLazyTreeItem(rootInst);
                TreeHierarchy.Items.Add(item);

                if (rootInst.Name is "ServerScriptService" or "StarterGui")
                {
                    item.IsExpanded = true;
                }
            }

            UpdateMultiSelectVisuals();
        }

        private TreeViewItem CreateLazyTreeItem(RobloxInstance inst)
        {
            var (icon, iconColorHex, badgeBgHex) = inst.GetVisuals();

            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 8, 2),
                Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent
            };
            _itemRowBorders[inst.Id] = rowBorder;

            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 8, 0),
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString(badgeBgHex)!,
                BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(iconColorHex)!,
                BorderThickness = new Thickness(1)
            };

            var txtIcon = new TextBlock { Text = icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            badge.Child = txtIcon;
            panel.Children.Add(badge);

            var txtName = new TextBlock
            {
                Text = inst.Name,
                FontWeight = FontWeights.Bold,
                FontSize = 13.5,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            if (inst.ClassName == "LocalScript") txtName.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#38BDF8")!;
            else if (inst.ClassName == "Script") txtName.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#4ADE80")!;
            else if (inst.ClassName == "ModuleScript") txtName.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#FB923C")!;
            else if (inst.ClassName == "Folder") txtName.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#FACC15")!;
            else if (inst.ClassName == "ScreenGui") txtName.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#60A5FA")!;
            else if (inst.IsRemote) txtName.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#F43F5E")!;

            panel.Children.Add(txtName);

            var txtClass = new TextBlock
            {
                Text = $"({inst.ClassName})",
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(txtClass);

            rowBorder.Child = panel;

            var item = new TreeViewItem
            {
                Header = rowBorder,
                Tag = inst.Id
            };

            if (inst.ChildrenIds.Count > 0)
            {
                item.Items.Add(new TreeViewItem { Header = "Cargando..." });
                item.Expanded += Item_Expanded;
            }

            return item;
        }

        private void Item_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Tag is int instId && _manager.Instances.TryGetValue(instId, out var inst))
            {
                if (item.Items.Count == 1 && item.Items[0] is TreeViewItem dummy && dummy.Header?.ToString() == "Cargando...")
                {
                    item.Items.Clear();

                    int maxShow = 150;
                    int count = 0;

                    foreach (int childId in inst.ChildrenIds)
                    {
                        if (_manager.Instances.TryGetValue(childId, out var childInst))
                        {
                            item.Items.Add(CreateLazyTreeItem(childInst));
                            count++;

                            if (count >= maxShow && inst.ChildrenIds.Count > maxShow)
                            {
                                int remaining = inst.ChildrenIds.Count - maxShow;
                                var moreItem = new TreeViewItem
                                {
                                    Header = new TextBlock
                                    {
                                        Text = $"📦 ... y {remaining:N0} elementos más en {inst.Name} (ver en Propiedades o Exportar)",
                                        Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!,
                                        FontStyle = FontStyles.Italic,
                                        FontSize = 12
                                    }
                                };
                                item.Items.Add(moreItem);
                                break;
                            }
                        }
                    }

                    UpdateMultiSelectVisuals();
                }
            }
        }

        private void TreeHierarchy_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var item = VisualUpwardSearch<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item?.Tag is int id && _manager.Instances.TryGetValue(id, out var inst))
            {
                bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                if (isCtrl)
                {
                    bool isFolderLike = inst.IsService || inst.ClassName is "Folder" or "ScreenGui" or "Model";
                    bool shouldAdd = !_selectedIds.Contains(id);

                    if (isFolderLike)
                    {
                        SelectInstanceAndDescendants(id, shouldAdd);
                    }
                    else
                    {
                        if (shouldAdd) _selectedIds.Add(id);
                        else _selectedIds.Remove(id);
                    }
                }
                else
                {
                    _selectedIds.Clear();
                    _selectedIds.Add(id);
                }

                UpdateMultiSelectVisuals();
            }
        }

        private void SelectInstanceAndDescendants(int id, bool add)
        {
            var toProcess = new Stack<int>();
            toProcess.Push(id);

            while (toProcess.Count > 0)
            {
                int currId = toProcess.Pop();
                if (add)
                {
                    _selectedIds.Add(currId);
                }
                else
                {
                    _selectedIds.Remove(currId);
                }

                if (_manager.Instances.TryGetValue(currId, out var inst))
                {
                    foreach (int childId in inst.ChildrenIds)
                    {
                        toProcess.Push(childId);
                    }
                }
            }
        }

        private void UpdateMultiSelectVisuals()
        {
            foreach (var kv in _itemRowBorders)
            {
                int id = kv.Key;
                var border = kv.Value;

                if (_selectedIds.Contains(id))
                {
                    border.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#0284C7")!;
                    border.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#38BDF8")!;
                    border.BorderThickness = new Thickness(1);
                }
                else
                {
                    border.Background = Brushes.Transparent;
                    border.BorderBrush = Brushes.Transparent;
                    border.BorderThickness = new Thickness(0);
                }
            }

            LblMultiSelectCount.Text = _selectedIds.Count > 1 ? $"🎯 {_selectedIds.Count:N0} elementos seleccionados" : "";
        }

        private static T? VisualUpwardSearch<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null && source is not T)
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as T;
        }

        private void TreeHierarchy_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (TreeHierarchy.SelectedItem is TreeViewItem item && item.Tag is int instId && _manager.Instances.TryGetValue(instId, out var inst))
            {
                _selectedInstance = inst;
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    _selectedIds.Clear();
                    _selectedIds.Add(instId);
                }

                UpdateMultiSelectVisuals();
                DisplayInstance(inst);
            }
        }

        private void DisplayInstance(RobloxInstance inst)
        {
            string parentName = inst.ParentId.HasValue && _manager.Instances.TryGetValue(inst.ParentId.Value, out var parent) ? parent.Name : "Ninguno";

            // 1. Update Properties Tab
            LblPropertiesTitle.Text = $"⚙️ Propiedades de [{inst.ClassName}]  {inst.Name}";
            GridProperties.ItemsSource = inst.GetPropertiesList(parentName);

            // 2. Update Code Editor Tab
            _isUpdatingEditorText = true;
            try
            {
                if (inst.Properties.TryGetValue("Source", out string? src) && inst.IsScript)
                {
                    LblScriptTitle.Text = $"[{inst.ClassName}]  {inst.Name}";
                    TxtEditor.IsEnabled = true;
                    TxtEditor.Text = src ?? string.Empty;
                    UpdateMetrics(src ?? string.Empty);
                }
                else
                {
                    LblScriptTitle.Text = $"[{inst.ClassName}]  {inst.Name}  (Información Técnica y Diagnóstico)";
                    TxtEditor.Text = inst.GetDiagnosticExplanation(parentName);
                    TxtEditor.IsEnabled = false;
                    LblScriptMetrics.Text = inst.IsService ? "Servicio Raíz" : (inst.IsRemote ? "Objeto de Red" : (inst.IsGui ? "Elemento UI" : "Contenedor"));
                }
            }
            finally
            {
                _isUpdatingEditorText = false;
            }

            // 3. Update GUI Preview Tab
            RenderGuiPreview(inst);
        }

        private void RenderGuiPreview(RobloxInstance inst)
        {
            GuiCanvas.Children.Clear();
            LblGuiTitle.Text = $"🖥️ Vista Previa Visual: {inst.Name} ({inst.ClassName})";

            RobloxInstance targetGui = inst;
            if (!inst.ClassName.Equals("ScreenGui", StringComparison.OrdinalIgnoreCase))
            {
                int? curr = inst.ParentId;
                while (curr.HasValue && _manager.Instances.TryGetValue(curr.Value, out var p))
                {
                    if (p.ClassName.Equals("ScreenGui", StringComparison.OrdinalIgnoreCase))
                    {
                        targetGui = p;
                        break;
                    }
                    curr = p.ParentId;
                }
            }

            var screenFrame = new Border
            {
                Width = 720,
                Height = 440,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#020617")),
                BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#1E293B")!,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6)
            };
            Canvas.SetLeft(screenFrame, 30);
            Canvas.SetTop(screenFrame, 20);
            GuiCanvas.Children.Add(screenFrame);

            var screenGrid = new Grid();
            screenFrame.Child = screenGrid;

            var topBar = new Border
            {
                Height = 36,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(12, 6, 12, 6)
            };
            var topStack = new StackPanel { Orientation = Orientation.Horizontal };
            topStack.Children.Add(new TextBlock { Text = "Roblox Player Viewport", FontWeight = FontWeights.Bold, Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#38BDF8")!, FontSize = 12 });
            topStack.Children.Add(new TextBlock { Text = $" - GUI: {targetGui.Name}", Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!, FontSize = 12 });
            topBar.Child = topStack;
            screenGrid.Children.Add(topBar);

            var guiContainer = new Canvas { Margin = new Thickness(0, 36, 0, 0) };
            screenGrid.Children.Add(guiContainer);

            DrawGuiChildren(targetGui, guiContainer, 0, 0, 720, 400);
        }

        private void DrawGuiChildren(RobloxInstance parentInst, Canvas canvas, double offsetX, double offsetY, double parentW, double parentH)
        {
            double posX = offsetX + 20;
            double posY = offsetY + 20;

            foreach (int childId in parentInst.ChildrenIds)
            {
                if (_manager.Instances.TryGetValue(childId, out var child))
                {
                    if (child.ClassName is "Frame" or "ImageLabel" or "TextLabel" or "TextButton" or "ScreenGui")
                    {
                        var elem = new Border
                        {
                            Width = Math.Min(320, parentW - 40),
                            Height = 70,
                            Background = (SolidColorBrush)new BrushConverter().ConvertFromString(child.ClassName == "TextButton" ? "#0369A1" : "#1E293B")!,
                            BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#38BDF8")!,
                            BorderThickness = new Thickness(1.5),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(10, 6, 10, 6),
                            Margin = new Thickness(0, 0, 0, 10)
                        };

                        var stack = new StackPanel();
                        stack.Children.Add(new TextBlock
                        {
                            Text = $"{child.GetVisuals().Icon}  {child.Name}",
                            FontWeight = FontWeights.Bold,
                            Foreground = Brushes.White,
                            FontSize = 13
                        });
                        stack.Children.Add(new TextBlock
                        {
                            Text = $"Tipo: {child.ClassName} | Hijos: {child.ChildrenIds.Count}",
                            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#94A3B8")!,
                            FontSize = 11.5,
                            Margin = new Thickness(0, 4, 0, 0)
                        });
                        elem.Child = stack;

                        Canvas.SetLeft(elem, posX);
                        Canvas.SetTop(elem, posY);
                        canvas.Children.Add(elem);

                        posY += 85;
                        if (posY > parentH - 80)
                        {
                            posY = offsetY + 20;
                            posX += 340;
                        }

                        if (child.ChildrenIds.Count > 0)
                        {
                            DrawGuiChildren(child, canvas, posX + 20, posY, parentW, parentH);
                        }
                    }
                }
            }
        }

        private void TxtEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingEditorText || _selectedInstance == null)
                return;

            if (_selectedInstance.Properties.ContainsKey("Source"))
            {
                string code = TxtEditor.Text;
                _selectedInstance.Properties["Source"] = code;
                UpdateMetrics(code);
            }
        }

        private void UpdateMetrics(string text)
        {
            int chars = text.Length;
            int lines = text.Count(c => c == '\n') + 1;
            LblScriptMetrics.Text = $"{chars:N0} caracteres | {lines:N0} líneas";
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!_manager.IsLoaded)
                return;

            var candidates = _manager.Instances.Values
                .Where(i => i.IsService || i.ClassName is "Folder" or "ScreenGui" or "StarterGui" or "ServerScriptService" or "ReplicatedStorage" or "Workspace")
                .OrderBy(i => i.Name);

            var modal = new AddScriptModal(candidates) { Owner = this };

            if (modal.ShowDialog() == true)
            {
                try
                {
                    var newInst = _manager.CreateScript(modal.ScriptName, modal.ScriptType, modal.ParentId);
                    PopulateTreeLazy();
                    MessageBox.Show($"Script '{newInst.Name}' ({newInst.ClassName}) agregado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al crear script:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedInstance == null)
            {
                MessageBox.Show("Por favor selecciona primero el script u objeto que deseas eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedInstance.IsService)
            {
                MessageBox.Show("No se puede eliminar un Servicio raíz de Roblox Studio.", "No permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var res = MessageBox.Show($"¿Estás seguro de que deseas eliminar '{_selectedInstance.Name}' ({_selectedInstance.ClassName})?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                int id = _selectedInstance.Id;
                _manager.DeleteInstance(id);
                _selectedInstance = null;
                _selectedIds.Remove(id);
                PopulateTreeLazy();
                TxtEditor.Text = string.Empty;
                TxtEditor.IsEnabled = false;
                LblScriptTitle.Text = "Objeto eliminado";
                LblScriptMetrics.Text = string.Empty;
                GridProperties.ItemsSource = null;
                GuiCanvas.Children.Clear();
                MessageBox.Show("Objeto eliminado correctamente.", "Eliminado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!_manager.IsLoaded)
                return;

            // 1. Create a rolling 5-backup in AppData/Local/RobloxScriptExplorer/Backups BEFORE anything is modified
            string backupPath = BackupService.CreateBackup(_manager.FilePath);

            LoadingOverlay.Visibility = Visibility.Visible;
            LblOverlayTitle.Text = "Guardando y Verificando Archivo...";
            ProgressLoading.Value = 10;
            LblLoadingStatus.Text = "Iniciando verificación y guardado seguro...";

            try
            {
                string bak = await _manager.SaveAsync(null, (status, progress) =>
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                    {
                        LblLoadingStatus.Text = status;
                        ProgressLoading.Value = progress * 100;
                    }));
                });

                MessageBox.Show($"¡Guardado y Verificación Exitosa al 100%!\n\n" +
                                $"📁 Archivo Actualizado: {Path.GetFileName(_manager.FilePath)}\n" +
                                $"🛡️ Backup Creado en AppData: {Path.GetFileName(backupPath)}\n" +
                                $"✅ Firma Binaria Validada: <roblox!\\x89\\xff\\r\\n\\x1a\\n\n" +
                                $"✅ Clases (0..C-1) y IDs (0..N-1) Contiguos\n" +
                                $"✅ Propiedades y Capacidades Escaladas.", 
                                "Guardado y Verificado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"⚠️ Error durante la verificación del archivo:\n{ex.Message}\n\n" +
                                $"🛡️ PROTECCIÓN ACTIVA: El archivo original NO fue modificado y se encuentra intacto.", 
                                "Error en Verificación", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnBackups_Click(object sender, RoutedEventArgs e)
        {
            string? placeName = _manager.IsLoaded ? Path.GetFileNameWithoutExtension(_manager.FilePath) : null;
            var modal = new BackupRecoveryModal(placeName) { Owner = this };

            if (modal.ShowDialog() == true && modal.RestoreRequested && modal.SelectedBackup != null)
            {
                try
                {
                    if (_manager.IsLoaded)
                    {
                        BackupService.RestoreBackup(modal.SelectedBackup.FullPath, _manager.FilePath);
                        await LoadPlaceAsync(_manager.FilePath);
                        MessageBox.Show($"¡Backup restaurado exitosamente!\n\nVersión restaurada: {modal.SelectedBackup.TimeFormatted}", "Backup Restaurado", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al restaurar backup:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!_manager.IsLoaded)
                return;

            var targetIds = _selectedIds.Count > 0 ? _selectedIds.ToList() : (_selectedInstance != null ? new List<int> { _selectedInstance.Id } : new List<int>());
            if (targetIds.Count == 0)
            {
                MessageBox.Show("Selecciona al menos un archivo o carpeta en el explorador.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFolderDialog { Title = "Seleccionar carpeta donde guardar los elementos exportados" };
            if (dlg.ShowDialog() == true)
            {
                string targetDir = dlg.FolderName;
                int scriptCount = 0;
                int folderCount = 0;
                int modelCount = 0;

                var topSelectedRoots = targetIds
                    .Where(id => !_selectedIds.Any(otherId => otherId != id && IsAncestor(otherId, id)))
                    .ToList();

                foreach (int id in topSelectedRoots)
                {
                    if (_manager.Instances.TryGetValue(id, out var inst))
                    {
                        _manager.ExportHierarchyNodeRecursive(inst, targetDir, ref scriptCount, ref folderCount, ref modelCount);
                    }
                }

                MessageBox.Show($"¡Exportación completada con éxito!\n\n📂 Carpetas creadas: {folderCount}\n📜 Scripts exportados: {scriptCount}\n🧊 Modelos exportados: {modelCount}\n\nUbicación:\n{targetDir}", "Exportación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool IsAncestor(int ancestorId, int childId)
        {
            int? curr = childId;
            while (curr.HasValue && _manager.Instances.TryGetValue(curr.Value, out var inst))
            {
                if (inst.ParentId == ancestorId) return true;
                curr = inst.ParentId;
            }
            return false;
        }

        private void BtnExportModel_Click(object sender, RoutedEventArgs e)
        {
            if (!_manager.IsLoaded)
                return;

            if (_selectedInstance == null)
            {
                MessageBox.Show("Selecciona primero el modelo 3D, carpeta, GUI o script que deseas exportar como modelo Roblox.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Guardar como Modelo Roblox (.rbxmx)",
                FileName = $"{_selectedInstance.Name}.rbxmx",
                Filter = "Roblox Model XML (*.rbxmx)|*.rbxmx|Todos los archivos (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _manager.ExportAsRbxmx(_selectedInstance, dlg.FileName);
                    MessageBox.Show($"¡Modelo Roblox exportado con éxito!\n\n📦 Archivo: {Path.GetFileName(dlg.FileName)}\n\n💡 Puedes arrastrar este archivo .rbxmx directamente dentro de cualquier ventana de Roblox Studio.", "Modelo Exportado", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al exportar modelo:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_manager.IsLoaded)
                return;

            var dlg = new OpenFolderDialog { Title = "Seleccionar carpeta donde crear el proyecto exportado" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string createdFolder = _manager.ExportCompleteProject(dlg.FolderName);
                    MessageBox.Show($"¡Proyecto exportado exitosamente!\n\n📁 Carpeta creada:\n{Path.GetFileName(createdFolder)}\n\nUbicación:\n{createdFolder}\n\n✅ Se crearon todas las carpetas, subcarpetas, scripts Luau, modelos 3D y el manifest.", "Exportación Completa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al exportar proyecto:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
