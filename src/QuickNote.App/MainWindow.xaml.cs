using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuickNote.App.Dialogs;
using QuickNote.App.Helpers;
using QuickNote.App.ViewModels;
using QuickNote.Core.Interfaces;
using QuickNote.Core.Models;

namespace QuickNote.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private readonly MainViewModel _viewModel;
        private readonly IMarkdownBehaviorEngine _behaviorEngine;
        private readonly ICloudSyncService _cloudSyncService;
        private readonly ISettingsService _settingsService;
        private readonly ITrashBinService _trashBinService;

        private bool _isUpdatingEditor;
        private bool _isUpdatingFromNotion;
        private TableCell? _activeTableCell;
        private bool _isDraggingColumnBorder;
        private Table? _resizingTable;
        private int _resizingColIndex = -1;
        private double _dragStartX;
        private double _initialColWidth;
        private Paragraph? _activeEditingCodeBlock;

        // Explorer multi-select
        private readonly List<FolderNode> _selectedNodes = new();
        private FolderNode? _anchorNode;

        public MainWindow(
            MainViewModel viewModel, 
            IMarkdownBehaviorEngine behaviorEngine,
            ICloudSyncService cloudSyncService,
            ISettingsService settingsService,
            ITrashBinService trashBinService)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _behaviorEngine = behaviorEngine ?? throw new ArgumentNullException(nameof(behaviorEngine));
            _cloudSyncService = cloudSyncService ?? throw new ArgumentNullException(nameof(cloudSyncService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _trashBinService = trashBinService ?? throw new ArgumentNullException(nameof(trashBinService));

            InitializeComponent();
            DataContext = _viewModel;

            _viewModel.NavigationRequested += OnNavigationRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.ThemeChanged += (s, isDark) => ApplyTheme(isDark);
            _viewModel.RequestOpenSyncDialog += async (s, e) =>
            {
                var dlg = new OneDriveSyncDialog(_cloudSyncService, _settingsService, _settingsService.Settings.NotesRoot, _viewModel.IsDarkMode)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true && dlg.SyncSettingsChanged)
                {
                    await _viewModel.SwitchNotesRootAsync(_settingsService.Settings.NotesRoot);
                }
            };
            _viewModel.RequestOpenTrashBin += (s, e) =>
            {
                var dlg = new TrashBinDialog(_trashBinService, _viewModel.IsDarkMode)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true && dlg.HasRestoredItems)
                {
                    _ = _viewModel.LoadAllAsync();
                }
            };

            SourceInitialized += (s, e) =>
            {
                UpdateTitleBarTheme(_viewModel.IsDarkMode);
                var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                source?.AddHook(WndProc);
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    e.Handled = true;
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                }
                else if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    e.Handled = true;
                    ToggleSidebar();
                }
                else if (e.Key == Key.E && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    e.Handled = true;
                    _viewModel.IsReadingMode = !_viewModel.IsReadingMode;
                }
                else if ((e.Key == Key.OemPipe || e.Key == Key.OemBackslash) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    e.Handled = true;
                    _viewModel.IsSplitMode = !_viewModel.IsSplitMode;
                }
            };

            LocationChanged += (s, e) => RepositionSearchPopup();
            Deactivated += (s, e) =>
            {
                if (_viewModel.HasSearchResults)
                {
                    _viewModel.HasSearchResults = false;
                }
            };
            PreviewMouseDown += (s, e) =>
            {
                if (_viewModel.HasSearchResults && SearchResultsPopup != null && SearchResultsPopup.IsOpen)
                {
                    var hit = e.OriginalSource as DependencyObject;
                    if (!IsDescendantOf(hit, SearchBoxBorder) && !IsDescendantOf(hit, SearchResultsPopup.Child))
                    {
                        _viewModel.HasSearchResults = false;
                    }
                }
            };

            Loaded += (s, e) =>
            {
                ApplyTheme(_viewModel.IsDarkMode);
                LoadDocumentIntoEditor();
                ApplyResponsiveLayout(ActualWidth);
            };

            DataObject.AddPastingHandler(NotionEditorBox, OnNotionEditorBoxPaste);
            NotionEditorBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnNotionEditorBoxCopy, CanExecuteCopy));
            NotionEditorBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, OnNotionEditorBoxCut, CanExecuteCut));
            NotionEditorBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, OnNotionEditorBoxDelete, CanExecuteDelete));
            NotionEditorBox.CommandBindings.Add(new CommandBinding(EditingCommands.Delete, OnNotionEditorBoxDelete, CanExecuteDelete));
            NotionEditorBox.CommandBindings.Add(new CommandBinding(EditingCommands.Backspace, OnNotionEditorBoxBackspace, CanExecuteDelete));

            NotionMarkdownHelper.OnDocumentContentChanged = () =>
            {
                if (!_isUpdatingEditor)
                {
                    SyncMarkdownFromEditor();
                }
            };

            bool isClosingAsync = false;
            Closing += async (s, e) =>
            {
                if (isClosingAsync) return;

                if (_viewModel.IsModified)
                {
                    e.Cancel = true;
                    isClosingAsync = true;
                    try
                    {
                        await _viewModel.SaveNoteAsync();
                        await _settingsService.SaveSettingsAsync();
                    }
                    catch { }
                    finally
                    {
                        Close();
                    }
                }
                else
                {
                    try
                    {
                        await _settingsService.SaveSettingsAsync();
                    }
                    catch { }
                }
            };
        }

        public void UpdateTitleBarTheme(bool isDark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int darkMode = isDark ? 1 : 0;
                    if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                    }
                }
            }
            catch { }
        }

        private void ApplyTheme(bool isDark)
        {
            UpdateTitleBarTheme(isDark);
            NotionMarkdownHelper.IsDarkMode = isDark;

            if (isDark)
            {
                Resources["NotionCanvasBg"] = new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x19));
                Resources["NotionSidebarBg"] = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                Resources["NotionHeaderBg"] = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                Resources["NotionBorder"] = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C));
                Resources["NotionItemHover"] = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C));
                Resources["NotionItemSelected"] = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                Resources["NotionTextPrimary"] = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
                Resources["NotionTextSecondary"] = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C));
                Resources["NotionTextMuted"] = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                Resources["NotionSearchBg"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                Resources["NotionButtonBg"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                Resources["NotionButtonBorder"] = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
                Resources["NotionButtonText"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
                Resources["NotionStatusBarBg"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                Resources["NotionOverlayBg"] = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
                Resources["NotionOverlayHeaderBg"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                Resources["NotionScrollbarThumb"] = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x3E));
                Resources["NotionScrollbarThumbHover"] = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));
                NotionEditorBox.CaretBrush = Brushes.White;
                NoteTitleBox.CaretBrush = Brushes.White;
            }
            else
            {
                // Notion classic light palette
                Resources["NotionCanvasBg"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Resources["NotionSidebarBg"] = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                Resources["NotionHeaderBg"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Resources["NotionBorder"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xE8));
                Resources["NotionItemHover"] = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEB));
                Resources["NotionItemSelected"] = new SolidColorBrush(Color.FromRgb(0xE3, 0xE2, 0xDE));
                Resources["NotionTextPrimary"] = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                Resources["NotionTextSecondary"] = new SolidColorBrush(Color.FromRgb(0x78, 0x77, 0x74));
                Resources["NotionTextMuted"] = new SolidColorBrush(Color.FromRgb(0x9B, 0x9A, 0x97));
                Resources["NotionSearchBg"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xEE));
                Resources["NotionButtonBg"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Resources["NotionButtonBorder"] = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xD9));
                Resources["NotionButtonText"] = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                Resources["NotionStatusBarBg"] = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                Resources["NotionOverlayBg"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Resources["NotionOverlayHeaderBg"] = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
                Resources["NotionScrollbarThumb"] = new SolidColorBrush(Color.FromRgb(0xD3, 0xD1, 0xCB));
                Resources["NotionScrollbarThumbHover"] = new SolidColorBrush(Color.FromRgb(0xAE, 0xAC, 0xA6));
                NotionEditorBox.CaretBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
                NoteTitleBox.CaretBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x35, 0x2F));
            }

            LoadDocumentIntoEditor();
        }

        private void LoadDocumentIntoEditor()
        {
            _isUpdatingEditor = true;
            try
            {
                NotionMarkdownHelper.FoldedHeadingSections.Clear();
                NotionEditorBox.Document = NotionMarkdownHelper.MarkdownToFlowDocument(_viewModel.EditorContent, _settingsService.Settings.NotesRoot);
                UpdateResponsiveImages();
                UpdateDocumentLayoutWidth();
            }
            finally
            {
                _isUpdatingEditor = false;
            }
        }

        // ── Responsive Layout & Panels ──────────────────────────────────────
        public const double BreakpointCompact = 720;
        public const double BreakpointMedium = 950;

        private double _lastResponsiveWidth = -1;
        private bool _leftPanelUserToggled = false;
        private bool _rightPanelUserToggled = false;
        private bool _leftPanelManualState = true;
        private bool _rightPanelManualState = true;
        private double _userLeftPanelWidth = 240;
        private double _userRightPanelWidth = 240;

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                ApplyResponsiveLayout(e.NewSize.Width);
            }
            RepositionSearchPopup();
        }

        public void ApplyResponsiveLayout(double windowWidth)
        {
            if (windowWidth <= 0) return;

            // Reset manual overrides when crossing breakpoint thresholds
            if (_lastResponsiveWidth > 0)
            {
                bool wasCompact = _lastResponsiveWidth < BreakpointCompact;
                bool isCompact = windowWidth < BreakpointCompact;
                bool wasMedium = _lastResponsiveWidth >= BreakpointCompact && _lastResponsiveWidth < BreakpointMedium;
                bool isMedium = windowWidth >= BreakpointCompact && windowWidth < BreakpointMedium;

                if (wasCompact != isCompact || wasMedium != isMedium)
                {
                    _leftPanelUserToggled = false;
                    _rightPanelUserToggled = false;
                }
            }
            _lastResponsiveWidth = windowWidth;

            // Narrow / Mobile screen (< 720px): hide BOTH Explorer and Outline
            if (windowWidth < BreakpointCompact)
            {
                SetLeftPanelVisibility(_leftPanelUserToggled ? _leftPanelManualState : false);
                SetRightPanelVisibility(_rightPanelUserToggled ? _rightPanelManualState : false);
            }
            // Medium screen (720px - 950px): hide Outline, show Explorer
            else if (windowWidth < BreakpointMedium)
            {
                SetLeftPanelVisibility(_leftPanelUserToggled ? _leftPanelManualState : true);
                SetRightPanelVisibility(_rightPanelUserToggled ? _rightPanelManualState : false);
            }
            // Wide / Desktop screen (>= 950px): show both panels
            else
            {
                SetLeftPanelVisibility(_leftPanelUserToggled ? _leftPanelManualState : true);
                SetRightPanelVisibility(_rightPanelUserToggled ? _rightPanelManualState : true);
            }

            UpdateResponsiveImages();
            UpdateDocumentLayoutWidth();
        }

        public void ToggleSidebar()
        {
            if (LeftPanelBorder == null) return;
            bool isCurrentlyVisible = LeftPanelBorder.Visibility == Visibility.Visible;
            _leftPanelUserToggled = true;
            _leftPanelManualState = !isCurrentlyVisible;
            SetLeftPanelVisibility(!isCurrentlyVisible);
            UpdateResponsiveImages();
            UpdateDocumentLayoutWidth();
        }

        public void ToggleOutline()
        {
            if (RightPanelBorder == null) return;
            bool isCurrentlyVisible = RightPanelBorder.Visibility == Visibility.Visible;
            _rightPanelUserToggled = true;
            _rightPanelManualState = !isCurrentlyVisible;
            SetRightPanelVisibility(!isCurrentlyVisible);
            UpdateResponsiveImages();
            UpdateDocumentLayoutWidth();
        }

        private void ToggleSidebarBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleSidebar();
        }

        private void ToggleOutlineBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleOutline();
        }

        private void SetLeftPanelVisibility(bool show)
        {
            if (LeftPanelBorder == null || LeftPanelColumn == null) return;
            if (show)
            {
                LeftPanelBorder.Visibility = Visibility.Visible;
                LeftPanelColumn.MinWidth = 160;
                LeftPanelColumn.MaxWidth = 800;
                double targetWidth = _userLeftPanelWidth >= 160 ? _userLeftPanelWidth : 240;
                LeftPanelColumn.Width = new GridLength(targetWidth);
            }
            else
            {
                if (LeftPanelColumn.ActualWidth >= 160)
                {
                    _userLeftPanelWidth = LeftPanelColumn.ActualWidth;
                }
                else if (LeftPanelColumn.Width.IsAbsolute && LeftPanelColumn.Width.Value >= 160)
                {
                    _userLeftPanelWidth = LeftPanelColumn.Width.Value;
                }
                LeftPanelBorder.Visibility = Visibility.Collapsed;
                LeftPanelColumn.MinWidth = 0;
                LeftPanelColumn.Width = new GridLength(0);
            }
        }

        private void SetRightPanelVisibility(bool show)
        {
            if (RightPanelBorder == null || RightPanelColumn == null) return;
            if (show)
            {
                RightPanelBorder.Visibility = Visibility.Visible;
                RightPanelColumn.MinWidth = 160;
                RightPanelColumn.MaxWidth = 500;
                double targetWidth = _userRightPanelWidth >= 160 ? _userRightPanelWidth : 240;
                RightPanelColumn.Width = new GridLength(targetWidth);
            }
            else
            {
                if (RightPanelColumn.ActualWidth >= 160)
                {
                    _userRightPanelWidth = RightPanelColumn.ActualWidth;
                }
                else if (RightPanelColumn.Width.IsAbsolute && RightPanelColumn.Width.Value >= 160)
                {
                    _userRightPanelWidth = RightPanelColumn.Width.Value;
                }
                RightPanelBorder.Visibility = Visibility.Collapsed;
                RightPanelColumn.MinWidth = 0;
                RightPanelColumn.Width = new GridLength(0);
            }
        }

        private void LeftPanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (LeftPanelColumn != null && LeftPanelColumn.ActualWidth >= 160)
            {
                _userLeftPanelWidth = LeftPanelColumn.ActualWidth;
            }
            UpdateResponsiveImages();
            UpdateDocumentLayoutWidth();
        }

        private void LeftPanelSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LeftPanelColumn != null)
            {
                _userLeftPanelWidth = 240;
                LeftPanelColumn.Width = new GridLength(240);
                UpdateResponsiveImages();
                UpdateDocumentLayoutWidth();
            }
        }

        private void RightPanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (RightPanelColumn != null && RightPanelColumn.ActualWidth >= 160)
            {
                _userRightPanelWidth = RightPanelColumn.ActualWidth;
            }
            UpdateResponsiveImages();
            UpdateDocumentLayoutWidth();
        }

        private void RightPanelSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RightPanelColumn != null)
            {
                _userRightPanelWidth = 240;
                RightPanelColumn.Width = new GridLength(240);
                UpdateResponsiveImages();
                UpdateDocumentLayoutWidth();
            }
        }

        private void NotionEditorBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveImages();
            UpdateDocumentLayoutWidth();
        }

        public void UpdateDocumentLayoutWidth()
        {
            if (NotionEditorBox?.Document == null) return;
            if (NotionEditorBox.ActualWidth <= 40) return;

            double viewportWidth = NotionEditorBox.ActualWidth - NotionEditorBox.Padding.Left - NotionEditorBox.Padding.Right - 20;
            if (viewportWidth < 100) return;

            double maxTableWidth = 0;
            foreach (var block in NotionEditorBox.Document.Blocks)
            {
                if (block is Table table)
                {
                    double tableWidth = 0;
                    foreach (var col in table.Columns)
                    {
                        tableWidth += col.Width.IsAbsolute ? col.Width.Value : 160;
                    }
                    if (tableWidth > maxTableWidth)
                    {
                        maxTableWidth = tableWidth;
                    }
                }
            }

            double targetPageWidth = (maxTableWidth > viewportWidth) ? maxTableWidth + 40 : viewportWidth;
            NotionEditorBox.Document.PageWidth = targetPageWidth;

            double codeBlockWidth = Math.Max(100, targetPageWidth - 52);
            foreach (var block in NotionEditorBox.Document.Blocks)
            {
                if (block is Paragraph p && NotionMarkdownHelper.IsCodeBlock(p))
                {
                    NotionMarkdownHelper.UpdateCodeBlockHeaderWidth(p, codeBlockWidth);
                }
            }
        }

        public void UpdateResponsiveImages()
        {
            if (NotionEditorBox?.Document == null) return;

            double maxAllowedWidth = Math.Max(100, NotionEditorBox.ActualWidth - 40);

            foreach (var block in NotionEditorBox.Document.Blocks)
            {
                if (block is BlockUIContainer container && container.Child is Image img)
                {
                    img.MaxWidth = maxAllowedWidth;
                }
            }
        }

        private const int WM_MOUSEHWHEEL = 0x020E;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEHWHEEL)
            {
                short delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                int screenX = (short)(lParam.ToInt64() & 0xFFFF);
                int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                Point screenPt = new Point(screenX, screenY);

                try
                {
                    var windowPt = this.PointFromScreen(screenPt);
                    var hitResult = VisualTreeHelper.HitTest(this, windowPt);
                    var hitElement = hitResult?.VisualHit;
                    if (hitElement != null)
                    {
                        var scrollViewer = FindAncestor<ScrollViewer>(hitElement);
                        if (scrollViewer != null && scrollViewer.ScrollableWidth > 0)
                        {
                            // delta > 0 is tilt/swipe right, delta < 0 is tilt/swipe left
                            double shift = (delta / 120.0) * 48.0;
                            double targetOffset = Math.Clamp(scrollViewer.HorizontalOffset + shift, 0, scrollViewer.ScrollableWidth);
                            scrollViewer.ScrollToHorizontalOffset(targetOffset);
                            handled = true;
                            return IntPtr.Zero;
                        }
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        public static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

        public static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                else
                {
                    current = LogicalTreeHelper.GetParent(current);
                }
            }
            return null;
        }
    }
}