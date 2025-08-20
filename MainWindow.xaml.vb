Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports CreoPublisherApp.CreoPublisherApp.ViewModels

Namespace CreoPublisherApp

    Partial Public Class MainWindow
        Inherits Window


        Private ReadOnly viewModel As MainViewModel
        ' Keep a reference to the fadeOut Completed handler for removal
        Private fadeOutCompletedHandler As EventHandler
        Private isDarkTheme As Boolean = False

        Public Sub New()
            InitializeComponent()
            viewModel = New MainViewModel()
            DataContext = viewModel
            LoadThemePreference()
        End Sub

        Private Sub DataGrid_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            ' Your existing code or leave empty
        End Sub

        Private Sub Grid_DragEnter(sender As Object, e As DragEventArgs)
            If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                e.Effects = DragDropEffects.Copy
                ShowDragDropOverlay()
            Else
                e.Effects = DragDropEffects.None
            End If
            e.Handled = True
        End Sub

        Private Sub Grid_DragLeave(sender As Object, e As DragEventArgs)
            HideDragDropOverlay()
            e.Handled = True
        End Sub

        Private Sub Grid_Drop(sender As Object, e As DragEventArgs)
            HideDragDropOverlay()

            ' Only handle if the actual drop target is the input folder border
            If sender Is InputFolderBorder Then
                If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                    Dim droppedItems = CType(e.Data.GetData(DataFormats.FileDrop), String())
                    If droppedItems IsNot Nothing AndAlso droppedItems.Length > 0 Then
                        Dim firstItem = droppedItems(0)
                        If Directory.Exists(firstItem) Then
                            viewModel.InputPath = firstItem
                        ElseIf File.Exists(firstItem) Then
                            viewModel.AddDroppedFiles(droppedItems)
                            If droppedItems.Length > 1 Then
                                viewModel.InputPath = "Multiple files selected"
                            End If
                        End If
                    End If
                End If
            End If

            e.Handled = True
        End Sub

        Private Sub ShowDragDropOverlay()
            DragDropOverlay.Visibility = Visibility.Visible
            Dim fadeIn As Storyboard = CType(Resources("FadeInOverlay"), Storyboard)

            ' Check if PulseOverlay exists before using it
            If Resources.Contains("PulseOverlay") Then
                Dim pulse As Storyboard = CType(Resources("PulseOverlay"), Storyboard)
                pulse.Begin(DragDropOverlay)
            End If

            fadeIn.Begin(DragDropOverlay)
        End Sub

        Private Sub HideDragDropOverlay()
            Dim fadeOut As Storyboard = CType(Resources("FadeOutOverlay"), Storyboard)

            ' Stop pulse animation if it exists
            If Resources.Contains("PulseOverlay") Then
                Dim pulse As Storyboard = CType(Resources("PulseOverlay"), Storyboard)
                pulse.Stop(DragDropOverlay)
            End If

            ' Define the handler delegate so it can be removed properly
            fadeOutCompletedHandler = Sub(s As Object, e As EventArgs)
                                          DragDropOverlay.Visibility = Visibility.Collapsed
                                          RemoveHandler fadeOut.Completed, fadeOutCompletedHandler
                                      End Sub

            AddHandler fadeOut.Completed, fadeOutCompletedHandler
            fadeOut.Begin(DragDropOverlay)
        End Sub

        Private Sub ShowDragDropInstructions(sender As Object, e As RoutedEventArgs)
            MessageBox.Show("To add drawing files, simply drag and drop them (or folders) anywhere on this window." & vbCrLf & vbCrLf &
                          "You can also use the browse buttons in the sidebar to select input and output folders.",
                    "How to Drag and Drop", MessageBoxButton.OK, MessageBoxImage.Information)
        End Sub

        Private Sub Button_Click(sender As Object, e As RoutedEventArgs)
            ' Generic button click handler - can be removed if not used
        End Sub

        Private Sub ToggleTheme_Checked(sender As Object, e As RoutedEventArgs)
            isDarkTheme = True
            ApplyDarkTheme()
            SaveThemePreference(True)
        End Sub

        Private Sub ToggleTheme_Unchecked(sender As Object, e As RoutedEventArgs)
            isDarkTheme = False
            ApplyLightTheme()
            SaveThemePreference(False)
        End Sub

        Private Sub ApplyDarkTheme()
            ' Update all theme resources to dark variants
            Me.Resources("BackgroundColor") = Me.Resources("DarkBackgroundColor")
            Me.Resources("ForegroundColor") = Me.Resources("DarkForegroundColor")
            Me.Resources("ButtonBackground") = Me.Resources("DarkButtonBackground")
            Me.Resources("ButtonForeground") = Me.Resources("DarkButtonForeground")
            Me.Resources("ButtonHoverBackground") = Me.Resources("DarkButtonHoverBackground")
            Me.Resources("ButtonPressedBackground") = Me.Resources("DarkButtonPressedBackground")
            Me.Resources("TextBoxBackground") = Me.Resources("DarkTextBoxBackground")
            Me.Resources("TextBoxForeground") = Me.Resources("DarkTextBoxForeground")
            Me.Resources("TextBoxBorderBrush") = Me.Resources("DarkTextBoxBorderBrush")
            Me.Resources("SearchTextBoxBackground") = Me.Resources("DarkSearchTextBoxBackground")
            Me.Resources("LogTextBoxBackground") = Me.Resources("DarkLogTextBoxBackground")
            Me.Resources("LogTextBoxBorderBrush") = Me.Resources("DarkLogTextBoxBorderBrush")
            Me.Resources("FilterTextBoxBackground") = Me.Resources("DarkFilterTextBoxBackground")
            Me.Resources("DataGridBackground") = Me.Resources("DarkDataGridBackground")
            Me.Resources("DataGridAlternatingBackground") = Me.Resources("DarkDataGridAlternatingBackground")
            Me.Resources("DataGridHeaderBackground") = Me.Resources("DarkDataGridHeaderBackground")
            Me.Resources("DataGridHeaderForeground") = Me.Resources("DarkDataGridHeaderForeground")
            Me.Resources("BorderBrushColor") = Me.Resources("DarkBorderBrushColor")
            Me.Resources("ComboBoxBackground") = Me.Resources("DarkComboBoxBackground")
            Me.Resources("ComboBoxBorderBrush") = Me.Resources("DarkComboBoxBorderBrush")
            Me.Resources("ComboBoxForeground") = Me.Resources("DarkComboBoxForeground")
            Me.Resources("DatePickerBackground") = Me.Resources("DarkDatePickerBackground")
            Me.Resources("DatePickerForeground") = Me.Resources("DarkDatePickerForeground")
            Me.Resources("TitleForeground") = Me.Resources("DarkTitleForeground")
            Me.Resources("PaginationBorderBackground") = Me.Resources("DarkPaginationBorderBackground")
            Me.Resources("PaginationBorderBrush") = Me.Resources("DarkPaginationBorderBrush")
            Me.Resources("OverlayBackground") = Me.Resources("DarkOverlayBackground")

            ' Update new resources for modern UI
            Me.Resources("SidebarBackground") = Me.Resources("DarkSidebarBackground")
            Me.Resources("SectionBackground") = Me.Resources("DarkSectionBackground")
            Me.Resources("LogoSource") = Me.Resources("DarkLogoSource")
            Me.Resources("RefreshFilter") = Me.Resources("DarkRefreshFilter")
            Me.Resources("RepeatFilter") = Me.Resources("DarkRepeatFilter")
            Me.Resources("TickFilter") = Me.Resources("DarkTickFilter")
            ' Update toggle button text
            ToggleThemeButton.Content = "☀️"
            ToggleThemeButton.ToolTip = "Switch to Light Mode"
        End Sub

        Private Sub ApplyLightTheme()
            ' Reset all resources to light theme colors
            Me.Resources("BackgroundColor") = New SolidColorBrush(Color.FromRgb(&HF8, &HF9, &HFA))
            Me.Resources("ForegroundColor") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("ButtonBackground") = New SolidColorBrush(Color.FromRgb(&H0, &H7B, &HFF))
            Me.Resources("ButtonForeground") = New SolidColorBrush(Colors.White)
            Me.Resources("ButtonHoverBackground") = New SolidColorBrush(Color.FromRgb(&H0, &H56, &HB3))
            Me.Resources("ButtonPressedBackground") = New SolidColorBrush(Color.FromRgb(&H0, &H40, &H85))
            Me.Resources("TextBoxBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("TextBoxForeground") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("TextBoxBorderBrush") = New SolidColorBrush(Color.FromRgb(&HDE, &HE2, &HE6))
            Me.Resources("SearchTextBoxBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("LogTextBoxBackground") = New SolidColorBrush(Color.FromRgb(&HF8, &HF9, &HFA))
            Me.Resources("LogTextBoxBorderBrush") = New SolidColorBrush(Color.FromRgb(&HDE, &HE2, &HE6))
            Me.Resources("FilterTextBoxBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("DataGridBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("DataGridAlternatingBackground") = New SolidColorBrush(Color.FromRgb(&HF8, &HF9, &HFA))
            Me.Resources("DataGridHeaderBackground") = New SolidColorBrush(Color.FromRgb(&H49, &H50, &H57))
            Me.Resources("DataGridHeaderForeground") = New SolidColorBrush(Colors.White)
            Me.Resources("BorderBrushColor") = New SolidColorBrush(Color.FromRgb(&HDE, &HE2, &HE6))
            Me.Resources("ComboBoxBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("ComboBoxBorderBrush") = New SolidColorBrush(Color.FromRgb(&HDE, &HE2, &HE6))
            Me.Resources("ComboBoxForeground") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("DatePickerBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("DatePickerForeground") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("TitleForeground") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("PaginationBorderBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("PaginationBorderBrush") = New SolidColorBrush(Color.FromRgb(&HDE, &HE2, &HE6))
            Me.Resources("OverlayBackground") = New SolidColorBrush(Color.FromArgb(&HAA, &H0, &H0, &H0))

            ' Update new resources for modern UI
            Me.Resources("SidebarBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("SectionBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("LogoSource") = Me.Resources("LightLogoSource")
            Me.Resources("RefreshFilter") = Me.Resources("LightRefreshFilter")
            Me.Resources("RepeatFilter") = Me.Resources("LightRepeatFilter")
            Me.Resources("TickFilter") = Me.Resources("LightTickFilter")
            ' Update toggle button text
            ToggleThemeButton.Content = "🌙"
            ToggleThemeButton.ToolTip = "Switch to Dark Mode"
        End Sub

        Private Sub SaveThemePreference(isDarkMode As Boolean)
            isDarkTheme = isDarkMode
            ' Save to settings if you have My.Settings configured
            Try
                My.Settings.IsDarkTheme = isDarkMode
                My.Settings.Save()
            Catch ex As Exception
                ' Handle settings save error if needed
                System.Diagnostics.Debug.WriteLine($"Could not save theme preference: {ex.Message}")
            End Try
        End Sub

        Private Sub LoadThemePreference()
            Try
                ' Load from settings if available
                isDarkTheme = My.Settings.IsDarkTheme
            Catch ex As Exception
                ' Default to light theme if settings not available
                isDarkTheme = False
                System.Diagnostics.Debug.WriteLine($"Could not load theme preference: {ex.Message}")
            End Try

            ToggleThemeButton.IsChecked = isDarkTheme

            If isDarkTheme Then
                ApplyDarkTheme()
            Else
                ApplyLightTheme()
            End If
        End Sub

        Private Sub Button_Click_1(sender As Object, e As RoutedEventArgs)

        End Sub
        ' Handle click on folder icon
        Private Sub FolderIcon_Click(sender As Object, e As MouseButtonEventArgs)
            Dim dlg As New System.Windows.Forms.FolderBrowserDialog()
            If dlg.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                Dim vm = TryCast(Me.DataContext, MainViewModel)
                If vm IsNot Nothing Then
                    vm.InputPath = dlg.SelectedPath
                End If
            End If
        End Sub

        ' Handle drag/drop folder
        Private Sub FolderIcon_Drop(sender As Object, e As DragEventArgs)
            If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                Dim folders = CType(e.Data.GetData(DataFormats.FileDrop), String())
                If folders.Length > 0 AndAlso Directory.Exists(folders(0)) Then
                    Dim vm = TryCast(Me.DataContext, MainViewModel)
                    If vm IsNot Nothing Then
                        vm.InputPath = folders(0) ' ✅ Updates InputPath
                    End If
                End If
            End If
        End Sub
        Private Sub OutputFolderIcon_Click(sender As Object, e As MouseButtonEventArgs)
            Dim dlg As New System.Windows.Forms.FolderBrowserDialog()
            If dlg.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                Dim vm = TryCast(Me.DataContext, MainViewModel)
                If vm IsNot Nothing Then
                    vm.OutputPath = dlg.SelectedPath
                End If
            End If
        End Sub



        ' Drag over for input folder button
        Private Sub InputFolderIcon_DragOver(sender As Object, e As DragEventArgs)
            If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                e.Effects = DragDropEffects.Copy
            Else
                e.Effects = DragDropEffects.None
            End If
            e.Handled = True
        End Sub

        ' Drop for input folder button
        Private Sub InputFolderIcon_Drop(sender As Object, e As DragEventArgs)
            If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                Dim paths As String() = CType(e.Data.GetData(DataFormats.FileDrop), String())
                If paths.Length > 0 AndAlso IO.Directory.Exists(paths(0)) Then
                    Dim vm = TryCast(Me.DataContext, MainViewModel)
                    If vm IsNot Nothing Then
                        vm.InputPath = paths(0)
                        vm.IsStep1Completed = True
                    End If
                End If
            End If
        End Sub

        ' Drag over for output folder button
        Private Sub OutputFolderIcon_DragOver(sender As Object, e As DragEventArgs)
            If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                e.Effects = DragDropEffects.Copy
            Else
                e.Effects = DragDropEffects.None
            End If
            e.Handled = True
        End Sub

        ' Drop for output folder button
        Private Sub OutputFolderIcon_Drop(sender As Object, e As DragEventArgs)
            If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                Dim paths As String() = CType(e.Data.GetData(DataFormats.FileDrop), String())
                If paths.Length > 0 AndAlso IO.Directory.Exists(paths(0)) Then
                    Dim vm = TryCast(Me.DataContext, MainViewModel)
                    If vm IsNot Nothing Then
                        vm.OutputPath = paths(0)
                        vm.IsStep2Completed = True
                    End If
                End If
            End If
        End Sub

    End Class
End Namespace