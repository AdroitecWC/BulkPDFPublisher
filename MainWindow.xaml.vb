Imports System.IO
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media.Animation
Imports CreoPublisherApp.CreoPublisherApp.ViewModels

Namespace CreoPublisherApp

    Partial Public Class MainWindow
        Inherits Window

        Private ReadOnly viewModel As MainViewModel
        ' Keep a reference to the fadeOut Completed handler for removal
        Private fadeOutCompletedHandler As EventHandler
        'Private isDarkTheme As Boolean = False
        Public Sub New()
            InitializeComponent()
            viewModel = New MainViewModel()
            DataContext = viewModel
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
            If e.Data.GetDataPresent(DataFormats.FileDrop) Then
                Dim droppedItems() As String = CType(e.Data.GetData(DataFormats.FileDrop), String())
                If droppedItems IsNot Nothing AndAlso droppedItems.Length > 0 Then

                    ' Determine whether the drop is files or folders
                    Dim firstItem As String = droppedItems(0)

                    If Directory.Exists(firstItem) Then
                        ' User dropped a folder → load the folder directly
                        viewModel.InputPath = firstItem
                    ElseIf File.Exists(firstItem) Then
                        ' User dropped files → add them but do NOT reset InputPath to the folder,
                        ' to avoid adding all .drw files from that folder
                        viewModel.AddDroppedFiles(droppedItems)

                        ' Update UI indication for multiple files selected if applicable
                        If droppedItems.Length > 1 Then
                            viewModel.InputPath = "Multiple files selected"
                        Else
                            ' Do NOT set InputPath to folder; prevents unintended folder scan
                        End If
                    End If
                End If
            End If
            e.Handled = True
        End Sub


        Private Sub ShowDragDropOverlay()
            DragDropOverlay.Visibility = Visibility.Visible
            Dim fadeIn As Storyboard = CType(Resources("FadeInOverlay"), Storyboard)
            Dim pulse As Storyboard = CType(Resources("PulseOverlay"), Storyboard)

            fadeIn.Begin(DragDropOverlay)
            pulse.Begin(DragDropOverlay)
        End Sub

        Private Sub HideDragDropOverlay()
            Dim fadeOut As Storyboard = CType(Resources("FadeOutOverlay"), Storyboard)
            Dim pulse As Storyboard = CType(Resources("PulseOverlay"), Storyboard)

            ' Define the handler delegate so it can be removed properly
            fadeOutCompletedHandler = Sub(s As Object, e As EventArgs)
                                          DragDropOverlay.Visibility = Visibility.Collapsed
                                          RemoveHandler fadeOut.Completed, fadeOutCompletedHandler
                                      End Sub

            AddHandler fadeOut.Completed, fadeOutCompletedHandler

            pulse.Stop(DragDropOverlay)
            fadeOut.Begin(DragDropOverlay)
        End Sub
        Private Sub ShowDragDropInstructions(sender As Object, e As RoutedEventArgs)
            MessageBox.Show("To add drawing files, simply drag and drop them (or folders) anywhere on this window.",
                    "How to Drag and Drop", MessageBoxButton.OK, MessageBoxImage.Information)
        End Sub

        Private Sub Button_Click(sender As Object, e As RoutedEventArgs)

        End Sub

        Private Sub PageNumberTextBox_KeyDown(sender As Object, e As KeyEventArgs)
            If e.Key = Key.Enter Then
                Dim viewModel = TryCast(DataContext, MainViewModel)
                If viewModel IsNot Nothing AndAlso viewModel.GoToPageCommand.CanExecute(Nothing) Then
                    viewModel.GoToPageCommand.Execute(Nothing)
                End If
            End If
        End Sub


        Private isDarkTheme As Boolean = False
        Private Sub ToggleTheme_Checked(sender As Object, e As RoutedEventArgs)
            ApplyDarkTheme()
        End Sub

        Private Sub ToggleTheme_Unchecked(sender As Object, e As RoutedEventArgs)
            ApplyLightTheme()
        End Sub

        Private Sub ApplyDarkTheme()
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

            ToggleThemeButton.Content = "☀️"
            AppLogo.Source = New BitmapImage(New Uri("pack://application:,,,/Resources/logo-white.png"))
        End Sub

        Private Sub ApplyLightTheme()
            Me.Resources("BackgroundColor") = New SolidColorBrush(Colors.White)
            Me.Resources("ForegroundColor") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("ButtonBackground") = New SolidColorBrush(Color.FromRgb(&H2E, &H3A, &H59))
            Me.Resources("ButtonForeground") = New SolidColorBrush(Colors.White)
            Me.Resources("ButtonHoverBackground") = New SolidColorBrush(Color.FromRgb(&H5A, &H9A, &HF2))
            Me.Resources("ButtonPressedBackground") = New SolidColorBrush(Color.FromRgb(&H3A, &H80, &HD2))
            Me.Resources("TextBoxBackground") = New SolidColorBrush(Color.FromArgb(&HFF, &HFD, &HFD, &HFA))
            Me.Resources("TextBoxForeground") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("TextBoxBorderBrush") = New SolidColorBrush(Colors.DarkGray)
            Me.Resources("SearchTextBoxBackground") = New SolidColorBrush(Color.FromArgb(&HFF, &HF0, &HF0, &HF0))
            Me.Resources("LogTextBoxBackground") = New SolidColorBrush(Color.FromArgb(&HFF, &HFB, &HFB, &HFB))
            Me.Resources("LogTextBoxBorderBrush") = New SolidColorBrush(Colors.DarkSlateGray)
            Me.Resources("FilterTextBoxBackground") = New SolidColorBrush(Color.FromArgb(&HFF, &HF8, &HF8, &HF8))
            Me.Resources("DataGridBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("DataGridAlternatingBackground") = New SolidColorBrush(Color.FromRgb(&HF8, &HF9, &HFA))
            Me.Resources("DataGridHeaderBackground") = New SolidColorBrush(Color.FromRgb(&H2E, &H3A, &H59))
            Me.Resources("DataGridHeaderForeground") = New SolidColorBrush(Colors.White)
            Me.Resources("BorderBrushColor") = New SolidColorBrush(Color.FromRgb(&H4A, &H90, &HE2))
            Me.Resources("ComboBoxBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("ComboBoxBorderBrush") = New SolidColorBrush(Color.FromRgb(&HCC, &HCC, &HCC))
            Me.Resources("ComboBoxForeground") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("DatePickerBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("DatePickerForeground") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E))
            Me.Resources("TitleForeground") = New SolidColorBrush(Color.FromRgb(&H2E, &H3A, &H59))
            Me.Resources("PaginationBorderBackground") = New SolidColorBrush(Colors.White)
            Me.Resources("PaginationBorderBrush") = New SolidColorBrush(Color.FromRgb(&H2E, &H3A, &H59))
            Me.Resources("OverlayBackground") = New SolidColorBrush(Color.FromArgb(&HAA, &H0, &H0, &H0))

            ToggleThemeButton.Content = "🌙"
            AppLogo.Source = New BitmapImage(New Uri("pack://application:,,,/Resources/logo.png"))
        End Sub

        Private Sub SaveThemePreference(isDarkMode As Boolean)
            isDarkTheme = isDarkMode
            My.Settings.Save()
        End Sub

        Private Sub LoadThemePreference()
            Dim isDarkMode As Boolean = isDarkTheme
            ToggleThemeButton.IsChecked = isDarkMode

            If isDarkMode Then
                ApplyDarkTheme()
            Else
                ApplyLightTheme()
            End If
        End Sub



    End Class
End Namespace
