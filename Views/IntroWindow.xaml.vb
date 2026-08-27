Namespace CreoPublisherApp
    Public Class IntroWindow
        Inherits Window

        'Private Sub ContinueButton_Click(sender As Object, e As RoutedEventArgs)
        '    Dim mainWindow = New MainWindow()

        '    ' When MainWindow closes, also close IntroWindow
        '    AddHandler mainWindow.Closed, Sub()
        '                                      Me.Close() ' this will close IntroWindow when MainWindow closes
        '                                  End Sub

        '    mainWindow.Show()

        '    Me.Hide() ' hide IntroWindow but keep it alive
        'End Sub
        Private Async Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
            ' Optional: Simulate loading delay
            Await Task.Delay(2500)

            ' Show MainWindow
            Dim mainWindow As New MainWindow()
            mainWindow.Show()
            SettingsManager.Instance.WorkingDirectory = My.Settings.WorkingDirectory

            ' Close IntroWindow
            Me.Close()
        End Sub
    End Class
End Namespace
