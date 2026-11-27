Imports System.Diagnostics
Imports System.Windows

Namespace CreoPublisherApp

    Class LicenseSettingsWindow

        Private Sub LicenseSettingsWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            Dim vm = TryCast(DataContext, LicenseSettingsViewModel)

            If vm IsNot Nothing Then
                AddHandler vm.RequestClose,
                    Sub()
                        Me.Close()
                    End Sub

                AddHandler vm.RequestShutdown,
                    Sub()
                        Me.Close()

                        ' Deactivate leaves the install with no valid license -- relaunch the
                        ' exe so the activation window comes back up on its own instead of
                        ' leaving the user to find and reopen it manually.
                        Try
                            Dim exePath = Process.GetCurrentProcess().MainModule?.FileName
                            If Not String.IsNullOrEmpty(exePath) Then Process.Start(exePath)
                        Catch
                            ' Best-effort -- if relaunch fails, the app still closes below.
                        End Try

                        If Application.Current.MainWindow IsNot Nothing Then
                            Application.Current.MainWindow.Close()
                        End If

                        Application.Current.Shutdown()
                    End Sub
            End If
        End Sub

    End Class

End Namespace
