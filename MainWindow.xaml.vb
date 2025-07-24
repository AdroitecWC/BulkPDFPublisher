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
        Private isDarkTheme As Boolean = False
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


        Private Sub ToggleTheme_Click(sender As Object, e As RoutedEventArgs)
            Try
                If isDarkTheme Then
                    ' Switch to Light Theme
                    Me.Resources("BackgroundColor") = New SolidColorBrush(Colors.White)
                    Me.Resources("ForegroundColor") = New SolidColorBrush(Colors.Black)

                    Dim lightGray As Color = Color.FromRgb(230, 230, 230)
                    Me.Resources("ButtonBackground") = New SolidColorBrush(lightGray)
                    Me.Resources("ButtonForeground") = New SolidColorBrush(Colors.Black)
                    Me.Resources("TextBoxBackground") = New SolidColorBrush(lightGray)
                    Me.Resources("TextBoxForeground") = New SolidColorBrush(Colors.Black)
                Else
                    ' Switch to Dark Theme
                    Me.Resources("BackgroundColor") = New SolidColorBrush(Color.FromRgb(&H1E, &H1E, &H1E)) ' #1E1E1E
                    Me.Resources("ForegroundColor") = New SolidColorBrush(Colors.White)

                    Me.Resources("ButtonBackground") = New SolidColorBrush(Color.FromRgb(&H3A, &H3A, &H3A)) ' #3A3A3A
                    Me.Resources("ButtonForeground") = New SolidColorBrush(Colors.White)
                    Me.Resources("TextBoxBackground") = New SolidColorBrush(Color.FromRgb(&H2E, &H2E, &H2E)) ' #2E2E2E
                    Me.Resources("TextBoxForeground") = New SolidColorBrush(Colors.White)
                End If

                ' Flip the theme mode
                isDarkTheme = Not isDarkTheme

                ' Update button label based on new theme state
                ToggleThemeButton.Content = If(isDarkTheme, "☀️ Light Mode", "🌙 Dark Mode")

            Catch ex As Exception
                MessageBox.Show("Error switching theme: " & ex.Message)
            End Try
        End Sub


    End Class
End Namespace
